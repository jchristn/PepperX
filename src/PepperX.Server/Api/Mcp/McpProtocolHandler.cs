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

        /*
         * MCP arguments arrive camelCase -- that is what the published tool schemas declare and what
         * every client emits -- while McpToolArgs is PascalCase like the rest of the codebase. The
         * default case-sensitive binding silently left every property null, so a tool call arrived
         * with no container and no key and failed deep inside a service. Schema validation now
         * rejects off-schema names before they reach here, but binding stays case-insensitive so the
         * two casings can never drift apart again.
         */
        private static readonly JsonSerializerOptions _ArgumentOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ContainerService _Containers;
        private readonly ObjectWriteService _Writes;
        private readonly ObjectReadService _Reads;
        private readonly ObjectDeleteService _Deletes;
        private readonly SearchService _Search;
        private readonly StatisticsService _Statistics;
        private readonly McpSettings _Settings;
        private readonly string _Header = "[McpProtocolHandler] ";
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

            // A wildcard hostname has to bind every interface, not loopback. Binding loopback made MCP
            // unreachable from outside a container while every other protocol on the node worked --
            // the port was published but nothing listened on the interface Docker forwards to.
            //
            // HttpListener spells "all interfaces" as "+", but on Windows that prefix needs a urlacl
            // reservation or an elevated process. The listener is started fire-and-forget below, so a
            // failed bind would surface as nothing listening rather than as an exception; probing
            // first turns that silent failure into a deliberate fallback.
            bool bindAll = _Settings.Hostname == "*";
            string httpHost = _Settings.Hostname;

            if (bindAll)
            {
                if (CanBindWildcard(_Settings.HttpPort))
                {
                    httpHost = "+";
                }
                else
                {
                    httpHost = "localhost";
                    bindAll = false;
                    _Logging?.Warn(
                        _Header + "cannot bind all interfaces on port " + _Settings.HttpPort +
                        "; MCP will listen on loopback only. To bind every interface, reserve the prefix: " +
                        "netsh http add urlacl url=http://+:" + _Settings.HttpPort + "/ user=Everyone");
                }
            }

            _Http = new McpHttpServer(httpHost, _Settings.HttpPort, "/rpc", "/events", true, "/mcp");
            _Tcp = new McpTcpServer(bindAll ? IPAddress.Any : IPAddress.Loopback, _Settings.TcpPort, true);
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
            Reg("pepperx_container_create", "Create a container.", SchemaContainerCreate(), async (a, ct) =>
            {
                ContainerCreateRequest req = new ContainerCreateRequest { Name = a.Name ?? a.Container ?? string.Empty, Tags = a.Tags };
                return Ok(await _Containers.CreateAsync(req, ct).ConfigureAwait(false));
            });

            Reg("pepperx_container_read", "Read a container by name.", SchemaContainer(), async (a, ct) =>
            {
                object? c = await _Containers.ReadAsync(a.Container ?? string.Empty, ct).ConfigureAwait(false);
                return c == null ? Err("Container not found.") : Ok(c);
            });

            Reg("pepperx_container_list", "List containers.", SchemaEnumerate(false), async (a, ct) =>
                Ok(await _Containers.EnumerateAsync(BuildQuery(a), ct).ConfigureAwait(false)));

            Reg("pepperx_container_enumerate", "Enumerate containers.", SchemaEnumerate(false), async (a, ct) =>
                Ok(await _Containers.EnumerateAsync(BuildQuery(a), ct).ConfigureAwait(false)));

            Reg("pepperx_container_update_tags", "Replace a container's tags.", SchemaContainerTags(), async (a, ct) =>
                Ok(await _Containers.UpdateTagsAsync(a.Container ?? string.Empty, a.Tags ?? new System.Collections.Generic.Dictionary<string, string>(), ct).ConfigureAwait(false)));

            Reg("pepperx_container_delete", "Delete a container.", SchemaContainerDelete(), async (a, ct) =>
            {
                await _Containers.DeleteAsync(a.Container ?? string.Empty, a.Force, ct).ConfigureAwait(false);
                return Ok(new { Deleted = true });
            });

            Reg("pepperx_object_write", "Write an object (base64 payload).", SchemaObjectWrite(), async (a, ct) =>
            {
                byte[] payload = string.IsNullOrEmpty(a.DataBase64) ? Array.Empty<byte>() : Convert.FromBase64String(a.DataBase64);
                using (MemoryStream ms = new MemoryStream(payload))
                {
                    return Ok(await _Writes.WriteAsync(a.Container ?? string.Empty, a.Key ?? string.Empty, ms, a.ContentType, a.Labels, a.Tags, a.Object, a.NoOverwrite, ct).ConfigureAwait(false));
                }
            });

            Reg("pepperx_object_read", "Read an object payload (base64, size-capped).", SchemaObject(), async (a, ct) =>
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

            Reg("pepperx_object_read_metadata", "Read an object's metadata.", SchemaObject(), async (a, ct) =>
            {
                ObjectMetadata? meta = await _Reads.ReadMetadataAsync(a.Container ?? string.Empty, a.Key ?? string.Empty, ct).ConfigureAwait(false);
                return meta == null ? Err("Object not found.") : Ok(meta);
            });

            Reg("pepperx_object_update_metadata", "Update an object's metadata.", SchemaObjectMetadata(), async (a, ct) =>
                Ok(await _Writes.UpdateMetadataAsync(a.Container ?? string.Empty, a.Key ?? string.Empty, new UpdateMetadataRequest { Labels = a.Labels, Tags = a.Tags, Object = a.Object }, ct).ConfigureAwait(false)));

            Reg("pepperx_object_delete", "Delete an object.", SchemaObject(), async (a, ct) =>
            {
                bool deleted = await _Deletes.DeleteAsync(a.Container ?? string.Empty, a.Key ?? string.Empty, ct).ConfigureAwait(false);
                return deleted ? Ok(new { Deleted = true }) : Err("Object not found.");
            });

            Reg("pepperx_object_exists", "Check object existence.", SchemaObject(), async (a, ct) =>
                Ok(new { Exists = await _Reads.ExistsAsync(a.Container ?? string.Empty, a.Key ?? string.Empty, ct).ConfigureAwait(false) }));

            Reg("pepperx_object_enumerate", "Search objects in a container by labels, tags, and prefix.", SchemaEnumerate(true), async (a, ct) =>
                Ok(await _Search.EnumerateContainerAsync(a.Container ?? string.Empty, BuildQuery(a), ct).ConfigureAwait(false)));

            Reg("pepperx_search", "Search objects across containers.", SchemaSearch(), async (a, ct) =>
                Ok(await _Search.SearchAllAsync(BuildQuery(a), ct).ConfigureAwait(false)));

            Reg("pepperx_stats", "Aggregate statistics.", SchemaNoArgs(), async (a, ct) =>
                Ok(await _Statistics.GetAsync(ct).ConfigureAwait(false)));

            Reg("pepperx_nodes", "List cluster nodes.", SchemaNoArgs(), async (a, ct) =>
                Ok(await _Statistics.GetNodesAsync(ct).ConfigureAwait(false)));
        }

        /*
         * Per-tool JSON Schemas.
         *
         * These are the only description an MCP client gets of what a tool accepts. A shared
         * "additionalProperties: true" schema type-checks fine and is useless in practice: an agent
         * calling pepperx_object_write has no way to learn that the payload goes in "dataBase64" and
         * would have to guess. Every argument is therefore named and described here.
         *
         * Property names are camelCase because that is what MCP clients emit; McpToolArgs binds them
         * case-insensitively.
         */

        private static object Prop(string type, string description)
        {
            return new { type, description };
        }

        private static object StringArray(string description)
        {
            return new { type = "array", items = new { type = "string" }, description };
        }

        private static object StringMap(string description)
        {
            return new { type = "object", additionalProperties = new { type = "string" }, description };
        }

        private static object Schema(object properties, params string[] required)
        {
            return new { type = "object", properties, required, additionalProperties = false };
        }

        private static object SchemaNoArgs()
        {
            return new { type = "object", properties = new { }, additionalProperties = false };
        }

        private static object SchemaContainer()
        {
            return Schema(new { container = Prop("string", "Container name.") }, "container");
        }

        private static object SchemaContainerCreate()
        {
            return Schema(
                new
                {
                    name = Prop("string", "Container name: 3-63 characters, lowercase letters, digits, and hyphens, starting and ending with a letter or digit."),
                    tags = StringMap("Optional tags to attach to the container.")
                },
                "name");
        }

        private static object SchemaContainerTags()
        {
            return Schema(
                new
                {
                    container = Prop("string", "Container name."),
                    tags = StringMap("Complete replacement tag map. An empty object clears all tags.")
                },
                "container", "tags");
        }

        private static object SchemaContainerDelete()
        {
            return Schema(
                new
                {
                    container = Prop("string", "Container name."),
                    force = Prop("boolean", "Delete the container's objects along with it. Without this, deleting a non-empty container fails. This is not recoverable.")
                },
                "container");
        }

        private static object SchemaObject()
        {
            return Schema(
                new
                {
                    container = Prop("string", "Container name."),
                    key = Prop("string", "Object key. May contain slashes and any other character.")
                },
                "container", "key");
        }

        private static object SchemaObjectWrite()
        {
            return Schema(
                new
                {
                    container = Prop("string", "Container name."),
                    key = Prop("string", "Object key. May contain slashes and any other character."),
                    dataBase64 = Prop("string", "Payload, base64-encoded. Omit for an empty object."),
                    contentType = Prop("string", "MIME type of the payload, for example application/json."),
                    labels = StringArray("Flat list of labels to attach. Searchable."),
                    tags = StringMap("Key-value tags to attach. Searchable."),
                    @object = new { description = "Freeform JSON metadata of any shape. Stored alongside the object but not searchable field-by-field." },
                    noOverwrite = Prop("boolean", "Fail instead of replacing an existing object at this key.")
                },
                "container", "key");
        }

        private static object SchemaObjectMetadata()
        {
            return Schema(
                new
                {
                    container = Prop("string", "Container name."),
                    key = Prop("string", "Object key."),
                    labels = StringArray("Complete replacement label list. Omit to leave labels unchanged."),
                    tags = StringMap("Complete replacement tag map. Omit to leave tags unchanged."),
                    @object = new { description = "Replacement freeform JSON metadata. Omit to leave it unchanged." }
                },
                "container", "key");
        }

        private static object SchemaEnumerate(bool requiresContainer)
        {
            object properties = new
            {
                container = Prop("string", "Container name."),
                prefix = Prop("string", "Return only keys starting with this prefix."),
                labelsFilter = StringArray("Return only objects carrying all of these labels."),
                tagsFilter = StringMap("Return only objects matching all of these tag pairs."),
                maxResults = Prop("integer", "Maximum results to return. Defaults to 100.")
            };

            return requiresContainer ? Schema(properties, "container") : Schema(properties);
        }

        private static object SchemaSearch()
        {
            return Schema(new
            {
                containers = StringArray("Limit the search to these containers. Omit to search all of them."),
                prefix = Prop("string", "Return only keys starting with this prefix."),
                labelsFilter = StringArray("Return only objects carrying all of these labels."),
                tagsFilter = StringMap("Return only objects matching all of these tag pairs."),
                maxResults = Prop("integer", "Maximum results to return. Defaults to 100.")
            });
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

        /// <summary>
        /// Test whether this process may bind every interface on a port.
        /// <para>
        /// Opening and immediately closing a listener is the only reliable way to know: the answer
        /// depends on platform and privilege, not on anything inspectable. The port is free again by
        /// the time the real listener claims it.
        /// </para>
        /// </summary>
        private static bool CanBindWildcard(int port)
        {
            HttpListener probe = new HttpListener();
            try
            {
                probe.Prefixes.Add("http://+:" + port + "/");
                probe.Start();
                probe.Stop();
                return true;
            }
            catch (HttpListenerException)
            {
                return false;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
            finally
            {
                probe.Close();
            }
        }

        private static McpToolArgs Parse(JsonElement? input)
        {
            if (input == null || input.Value.ValueKind == JsonValueKind.Null || input.Value.ValueKind == JsonValueKind.Undefined) return new McpToolArgs();
            return JsonSerializer.Deserialize<McpToolArgs>(input.Value.GetRawText(), _ArgumentOptions) ?? new McpToolArgs();
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
