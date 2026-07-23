namespace PepperX.Server.Api.Mcp
{
    using System;
    using System.IO;
    using System.Net;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Serialization;
    using PepperX.Core.Services;
    using PepperX.Core.Settings;
    using SyslogLogging;
    using Voltaic.Mcp;

    /// <summary>
    /// Hosts the MCP protocol surface over Streamable HTTP (<c>/mcp</c>) and TCP JSON-RPC, exposing
    /// REST-equivalent operations as MCP tools built on Voltaic.
    /// </summary>
    public sealed class McpProtocolHandler
    {
        #region Private-Members

        private readonly PepperXSerializer _Serializer = new PepperXSerializer();
        private readonly ContainerService _Containers;
        private readonly ObjectWriteService _Writes;
        private readonly ObjectReadService _Reads;
        private readonly ObjectDeleteService _Deletes;
        private readonly SearchService _Search;
        private readonly StatisticsService _Statistics;
        private readonly McpSettings _Settings;
        private readonly LoggingModule? _Logging;

        private McpHttpServer? _Http;
        private McpTcpServer? _Tcp;
        private CancellationTokenSource? _Cts;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the MCP protocol handler.
        /// </summary>
        /// <param name="containers">Container service.</param>
        /// <param name="writes">Write service.</param>
        /// <param name="reads">Read service.</param>
        /// <param name="deletes">Delete service.</param>
        /// <param name="search">Search service.</param>
        /// <param name="statistics">Statistics service.</param>
        /// <param name="settings">MCP settings.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public McpProtocolHandler(ContainerService containers, ObjectWriteService writes, ObjectReadService reads, ObjectDeleteService deletes, SearchService search, StatisticsService statistics, McpSettings settings, LoggingModule? logging)
        {
            _Containers = containers ?? throw new ArgumentNullException(nameof(containers));
            _Writes = writes ?? throw new ArgumentNullException(nameof(writes));
            _Reads = reads ?? throw new ArgumentNullException(nameof(reads));
            _Deletes = deletes ?? throw new ArgumentNullException(nameof(deletes));
            _Search = search ?? throw new ArgumentNullException(nameof(search));
            _Statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the MCP HTTP and TCP listeners.
        /// </summary>
        public void Start()
        {
            _Cts = new CancellationTokenSource();
            _Http = new McpHttpServer(_Settings.Hostname, _Settings.HttpPort, "/rpc", "/events", true, "/mcp");
            _Tcp = new McpTcpServer(IPAddress.Loopback, _Settings.TcpPort, true);
            _Http.ServerName = Constants.ProductName;
            _Tcp.ServerName = Constants.ProductName;

            RegisterTools();

            _ = _Http.StartAsync(_Cts.Token);
            _ = _Tcp.StartAsync(_Cts.Token);
        }

        /// <summary>
        /// Stop the MCP listeners.
        /// </summary>
        public void Stop()
        {
            try { _Cts?.Cancel(); } catch (Exception) { }
            try { _Http?.Stop(); } catch (Exception) { }
            try { _Tcp?.Stop(); } catch (Exception) { }
            _Http?.Dispose();
            _Tcp?.Dispose();
            _Cts?.Dispose();
        }

        #endregion

        #region Private-Methods

        private void RegisterTools()
        {
            object schema = new { type = "object", additionalProperties = true };

            Reg("pepperx_container_create", "Create a container.", schema, async (a, ct) =>
            {
                ContainerCreateRequest req = new ContainerCreateRequest { Name = a.Name ?? a.Container ?? string.Empty, Tags = a.Tags };
                return Ok(await _Containers.CreateAsync(req, ct).ConfigureAwait(false));
            });

            Reg("pepperx_container_read", "Read a container by name.", schema, async (a, ct) =>
            {
                object? c = await _Containers.ReadAsync(a.Container ?? string.Empty, ct).ConfigureAwait(false);
                return c == null ? Err("Container not found.") : Ok(c);
            });

            Reg("pepperx_container_list", "List containers.", schema, async (a, ct) =>
                Ok(await _Containers.EnumerateAsync(BuildQuery(a), ct).ConfigureAwait(false)));

            Reg("pepperx_container_enumerate", "Enumerate containers.", schema, async (a, ct) =>
                Ok(await _Containers.EnumerateAsync(BuildQuery(a), ct).ConfigureAwait(false)));

            Reg("pepperx_container_update_tags", "Replace a container's tags.", schema, async (a, ct) =>
                Ok(await _Containers.UpdateTagsAsync(a.Container ?? string.Empty, a.Tags ?? new System.Collections.Generic.Dictionary<string, string>(), ct).ConfigureAwait(false)));

            Reg("pepperx_container_delete", "Delete a container.", schema, async (a, ct) =>
            {
                await _Containers.DeleteAsync(a.Container ?? string.Empty, a.Force, ct).ConfigureAwait(false);
                return Ok(new { Deleted = true });
            });

            Reg("pepperx_object_write", "Write an object (base64 payload).", schema, async (a, ct) =>
            {
                byte[] payload = string.IsNullOrEmpty(a.DataBase64) ? Array.Empty<byte>() : Convert.FromBase64String(a.DataBase64);
                using (MemoryStream ms = new MemoryStream(payload))
                {
                    return Ok(await _Writes.WriteAsync(a.Container ?? string.Empty, a.Key ?? string.Empty, ms, a.ContentType, a.Labels, a.Tags, a.Object, a.NoOverwrite, ct).ConfigureAwait(false));
                }
            });

            Reg("pepperx_object_read", "Read an object payload (base64, size-capped).", schema, async (a, ct) =>
            {
                await using (ObjectReadHandle? handle = await _Reads.ReadAsync(a.Container ?? string.Empty, a.Key ?? string.Empty, null, null, ct).ConfigureAwait(false))
                {
                    if (handle == null) return Err("Object not found.");
                    if (handle.Extent.SizeBytes > _Settings.MaxInlineBytes) return Err("Object exceeds the MCP inline limit; read it via the REST API.");
                    using (MemoryStream ms = new MemoryStream())
                    {
                        await handle.Payload.CopyToAsync(ms, ct).ConfigureAwait(false);
                        return Ok(new { handle.Extent.Key, ExtentId = handle.Extent.Id, handle.Extent.SizeBytes, handle.Extent.Sha256, handle.Extent.ContentType, DataBase64 = Convert.ToBase64String(ms.ToArray()) });
                    }
                }
            });

            Reg("pepperx_object_read_metadata", "Read an object's metadata.", schema, async (a, ct) =>
            {
                ObjectMetadata? meta = await _Reads.ReadMetadataAsync(a.Container ?? string.Empty, a.Key ?? string.Empty, ct).ConfigureAwait(false);
                return meta == null ? Err("Object not found.") : Ok(meta);
            });

            Reg("pepperx_object_update_metadata", "Update an object's metadata.", schema, async (a, ct) =>
                Ok(await _Writes.UpdateMetadataAsync(a.Container ?? string.Empty, a.Key ?? string.Empty, new UpdateMetadataRequest { Labels = a.Labels, Tags = a.Tags, Object = a.Object }, ct).ConfigureAwait(false)));

            Reg("pepperx_object_delete", "Delete an object.", schema, async (a, ct) =>
            {
                bool deleted = await _Deletes.DeleteAsync(a.Container ?? string.Empty, a.Key ?? string.Empty, ct).ConfigureAwait(false);
                return deleted ? Ok(new { Deleted = true }) : Err("Object not found.");
            });

            Reg("pepperx_object_exists", "Check object existence.", schema, async (a, ct) =>
                Ok(new { Exists = await _Reads.ExistsAsync(a.Container ?? string.Empty, a.Key ?? string.Empty, ct).ConfigureAwait(false) }));

            Reg("pepperx_object_enumerate", "Search objects in a container by labels, tags, and prefix.", schema, async (a, ct) =>
                Ok(await _Search.EnumerateContainerAsync(a.Container ?? string.Empty, BuildQuery(a), ct).ConfigureAwait(false)));

            Reg("pepperx_search", "Search objects across containers.", schema, async (a, ct) =>
                Ok(await _Search.SearchAllAsync(BuildQuery(a), ct).ConfigureAwait(false)));

            Reg("pepperx_stats", "Aggregate statistics.", schema, async (a, ct) =>
                Ok(await _Statistics.GetAsync(ct).ConfigureAwait(false)));

            Reg("pepperx_nodes", "List cluster nodes.", schema, async (a, ct) =>
                Ok(await _Statistics.GetNodesAsync(ct).ConfigureAwait(false)));
        }

        private void Reg(string name, string description, object schema, Func<McpToolArgs, CancellationToken, Task<McpToolCallResult>> handler)
        {
            Func<JsonElement?, CancellationToken, Task<object>> wrapped = async (JsonElement? input, CancellationToken ct) =>
            {
                try
                {
                    McpToolArgs args = Parse(input);
                    return await handler(args, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Err(ex.Message);
                }
            };

            _Http?.RegisterTool(name, description, schema, wrapped);
            _Tcp?.RegisterTool(name, description, schema, wrapped);
        }

        private McpToolArgs Parse(JsonElement? input)
        {
            if (input == null || input.Value.ValueKind == JsonValueKind.Null || input.Value.ValueKind == JsonValueKind.Undefined) return new McpToolArgs();
            return _Serializer.DeserializeJson<McpToolArgs>(input.Value.GetRawText()) ?? new McpToolArgs();
        }

        private EnumerationQuery BuildQuery(McpToolArgs a)
        {
            EnumerationQuery query = new EnumerationQuery();
            if (a.MaxResults.HasValue) query.MaxResults = a.MaxResults.Value;
            if (!string.IsNullOrEmpty(a.Prefix)) query.Prefix = a.Prefix;
            if (a.LabelsFilter != null && a.LabelsFilter.Count > 0) query.Labels = a.LabelsFilter;
            if (a.TagsFilter != null && a.TagsFilter.Count > 0) query.Tags = a.TagsFilter;
            if (a.Containers != null && a.Containers.Count > 0) query.Containers = a.Containers;
            return query;
        }

        private McpToolCallResult Ok(object result)
        {
            return McpToolCallResult.FromStructured(result);
        }

        private static McpToolCallResult Err(string message)
        {
            McpToolCallResult result = McpToolCallResult.FromText(message);
            result.IsError = true;
            return result;
        }

        #endregion
    }
}
