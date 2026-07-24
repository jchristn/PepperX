namespace PepperX.Server.Api.Websockets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core;
    using PepperX.Core.Database;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Enums;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Serialization;
    using PepperX.Core.Services;
    using PepperX.Core.Settings;
    using SyslogLogging;
    using WatsonWebsocket;
    using WebsocketSettings = PepperX.Core.Settings.WebsocketSettings;

    /// <summary>
    /// Hosts the WebSocket envelope protocol, offering REST-equivalent operations over a persistent
    /// connection. Requests are handled concurrently and correlated to responses by request identifier.
    /// </summary>
    public sealed class WebsocketProtocolHandler
    {
        #region Private-Members

        private readonly PepperXSerializer _Serializer = new PepperXSerializer();
        private readonly ContainerService _Containers;
        private readonly ObjectWriteService _Writes;
        private readonly ObjectReadService _Reads;
        private readonly ObjectDeleteService _Deletes;
        private readonly SearchService _Search;
        private readonly StatisticsService _Statistics;
        private readonly WebsocketSettings _Settings;
        private readonly string _Header = "[WebsocketProtocolHandler] ";
        private readonly LoggingModule? _Logging;

        private WatsonWsServer? _Server;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the WebSocket protocol handler.
        /// </summary>
        /// <param name="containers">Container service.</param>
        /// <param name="writes">Write service.</param>
        /// <param name="reads">Read service.</param>
        /// <param name="deletes">Delete service.</param>
        /// <param name="search">Search service.</param>
        /// <param name="statistics">Statistics service.</param>
        /// <param name="settings">WebSocket settings.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public WebsocketProtocolHandler(ContainerService containers, ObjectWriteService writes, ObjectReadService reads, ObjectDeleteService deletes, SearchService search, StatisticsService statistics, WebsocketSettings settings, LoggingModule? logging)
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
        /// Start the WebSocket listener.
        /// </summary>
        public void Start()
        {
            if (_Settings.Hostname != "*")
            {
                StartWith(new List<string> { _Settings.Hostname });
                return;
            }

            // WebSocket upgrades are matched by listener prefix, so the set of bound names decides who
            // can connect at all -- and there is no single choice that works everywhere.
            //
            // "+" binds every interface, which is what a container needs: binding only the loopback
            // names left the listener unreachable from outside while REST and S3 on the same node
            // worked, because the port was published but nothing listened on the interface Docker
            // forwards to. On Windows, though, "+" requires a urlacl reservation or an elevated
            // process, and failing there would break `dotnet run` out of the box.
            //
            // So: try the wildcard, and fall back to the loopback names when the platform refuses it.
            // Both names are bound in the fallback so a local client reaches the node whichever one
            // it addresses. They cannot be combined with "+" -- that double-binds the port and the
            // listener fails with "address in use".
            try
            {
                StartWith(new List<string> { "+" });
            }
            catch (HttpListenerException ex)
            {
                _Logging?.Warn(
                    _Header + "could not bind all interfaces on port " + _Settings.Port + " (" + ex.Message +
                    "); falling back to loopback only. On Windows, reserve the prefix with: netsh http add urlacl url=http://+:" +
                    _Settings.Port + "/ user=Everyone");

                StartWith(new List<string> { "localhost", "127.0.0.1" });
            }
        }

        private void StartWith(List<string> hosts)
        {
            _Server = new WatsonWsServer(hosts, _Settings.Port, false);
            _Server.MessageReceived += OnMessageReceived;
            _Server.Start();
        }

        /// <summary>
        /// Stop the WebSocket listener.
        /// </summary>
        public void Stop()
        {
            try { _Server?.Stop(); } catch (Exception) { }
            _Server?.Dispose();
            _Server = null;
        }

        #endregion

        #region Private-Methods

        private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            Guid clientGuid = e.Client.Guid;
            byte[] data = e.Data.ToArray();

            _ = Task.Run(async () =>
            {
                WsResponseEnvelope response = await HandleAsync(data).ConfigureAwait(false);
                try
                {
                    string json = _Serializer.SerializeJson(response, false) ?? "{}";
                    if (_Server != null) await _Server.SendAsync(clientGuid, json, WebSocketMessageType.Text, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging?.Debug("[WebsocketProtocolHandler] send failed: " + ex.Message);
                }
            });
        }

        private async Task<WsResponseEnvelope> HandleAsync(byte[] data)
        {
            WsRequestEnvelope request;
            try
            {
                request = _Serializer.DeserializeJson<WsRequestEnvelope>(Encoding.UTF8.GetString(data));
                if (request == null) return Malformed("Empty request.");
            }
            catch (Exception ex)
            {
                return Malformed("Unparseable request: " + ex.Message);
            }

            try
            {
                return await DispatchAsync(request).ConfigureAwait(false);
            }
            catch (PepperXException ex)
            {
                return Fail(request.RequestId, ex.ErrorType, ex.StatusCode, ex.Message);
            }
            catch (ArgumentException ex)
            {
                return Fail(request.RequestId, ApiErrorEnum.BadRequest, 400, ex.Message);
            }
            catch (Exception ex)
            {
                return Fail(request.RequestId, ApiErrorEnum.InternalError, 500, ex.Message);
            }
        }

        private async Task<WsResponseEnvelope> DispatchAsync(WsRequestEnvelope request)
        {
            CancellationToken ct = CancellationToken.None;

            switch (request.Operation)
            {
                case WsOperationEnum.Health:
                    return Ok(request, new { Name = Constants.ProductName, Version = Constants.ProductVersion });

                case WsOperationEnum.ContainerCreate:
                    return Ok(request, await _Containers.CreateAsync(Bind<ContainerCreateRequest>(request.Body) ?? new ContainerCreateRequest(), ct).ConfigureAwait(false), 201);

                case WsOperationEnum.ContainerRead:
                {
                    ContainerResponse? container = await _Containers.ReadAsync(RequireContainer(request), ct).ConfigureAwait(false);
                    return container == null ? Fail(request.RequestId, ApiErrorEnum.NotFound, 404, "Container not found.") : Ok(request, container);
                }

                case WsOperationEnum.ContainerList:
                case WsOperationEnum.ContainerEnumerate:
                    return Ok(request, await _Containers.EnumerateAsync(BindQuery(request.Body), ct).ConfigureAwait(false));

                case WsOperationEnum.ContainerUpdateTags:
                    return Ok(request, await _Containers.UpdateTagsAsync(RequireContainer(request), Bind<System.Collections.Generic.Dictionary<string, string>>(request.Body) ?? new System.Collections.Generic.Dictionary<string, string>(), ct).ConfigureAwait(false));

                case WsOperationEnum.ContainerDelete:
                    await _Containers.DeleteAsync(RequireContainer(request), false, ct).ConfigureAwait(false);
                    return Ok(request, new { Deleted = true }, 204);

                case WsOperationEnum.ContainerExists:
                    return Ok(request, new { Exists = await _Containers.ExistsAsync(RequireContainer(request), ct).ConfigureAwait(false) });

                case WsOperationEnum.ObjectWrite:
                    return await ObjectWriteAsync(request, ct).ConfigureAwait(false);

                case WsOperationEnum.ObjectRead:
                    return await ObjectReadAsync(request, ct).ConfigureAwait(false);

                case WsOperationEnum.ObjectReadMetadata:
                {
                    ObjectMetadata? metadata = await _Reads.ReadMetadataAsync(RequireContainer(request), RequireKey(request), ct).ConfigureAwait(false);
                    return metadata == null ? Fail(request.RequestId, ApiErrorEnum.NotFound, 404, "Object not found.") : Ok(request, metadata);
                }

                case WsOperationEnum.ObjectUpdateMetadata:
                    return Ok(request, await _Writes.UpdateMetadataAsync(RequireContainer(request), RequireKey(request), Bind<UpdateMetadataRequest>(request.Body) ?? new UpdateMetadataRequest(), ct).ConfigureAwait(false));

                case WsOperationEnum.ObjectDelete:
                {
                    bool deleted = await _Deletes.DeleteAsync(RequireContainer(request), RequireKey(request), ct).ConfigureAwait(false);
                    return deleted ? Ok(request, new { Deleted = true }, 204) : Fail(request.RequestId, ApiErrorEnum.NotFound, 404, "Object not found.");
                }

                case WsOperationEnum.ObjectExists:
                    return Ok(request, new { Exists = await _Reads.ExistsAsync(RequireContainer(request), RequireKey(request), ct).ConfigureAwait(false) });

                case WsOperationEnum.ObjectList:
                case WsOperationEnum.ObjectEnumerate:
                    return Ok(request, await _Search.EnumerateContainerAsync(RequireContainer(request), BindQuery(request.Body), ct).ConfigureAwait(false));

                case WsOperationEnum.SearchEnumerate:
                    return Ok(request, await _Search.SearchAllAsync(BindQuery(request.Body), ct).ConfigureAwait(false));

                case WsOperationEnum.AdminStats:
                    return Ok(request, await _Statistics.GetAsync(ct).ConfigureAwait(false));

                case WsOperationEnum.AdminNodes:
                    return Ok(request, await _Statistics.GetNodesAsync(ct).ConfigureAwait(false));

                default:
                    return Fail(request.RequestId, ApiErrorEnum.BadRequest, 400, "Unsupported operation.");
            }
        }

        private async Task<WsResponseEnvelope> ObjectWriteAsync(WsRequestEnvelope request, CancellationToken ct)
        {
            WriteObjectRequest body = Bind<WriteObjectRequest>(request.Body) ?? new WriteObjectRequest();
            byte[] payload = String.IsNullOrEmpty(body.DataBase64) ? Array.Empty<byte>() : Convert.FromBase64String(body.DataBase64);

            using (MemoryStream ms = new MemoryStream(payload))
            {
                ObjectWriteResponse write = await _Writes.WriteAsync(RequireContainer(request), RequireKey(request), ms, body.ContentType, body.Labels, body.Tags, body.Object, body.NoOverwrite, ct).ConfigureAwait(false);
                return Ok(request, write, 201);
            }
        }

        private async Task<WsResponseEnvelope> ObjectReadAsync(WsRequestEnvelope request, CancellationToken ct)
        {
            await using (ObjectReadHandle? handle = await _Reads.ReadAsync(RequireContainer(request), RequireKey(request), null, null, ct).ConfigureAwait(false))
            {
                if (handle == null) return Fail(request.RequestId, ApiErrorEnum.NotFound, 404, "Object not found.");

                using (MemoryStream ms = new MemoryStream())
                {
                    await handle.Payload.CopyToAsync(ms, ct).ConfigureAwait(false);
                    WsResponseEnvelope response = Ok(request, new
                    {
                        handle.Extent.Key,
                        ExtentId = handle.Extent.Id,
                        handle.Extent.SizeBytes,
                        handle.Extent.Sha256,
                        handle.Extent.ContentType
                    });
                    response.DataBase64 = Convert.ToBase64String(ms.ToArray());
                    return response;
                }
            }
        }

        private T? Bind<T>(object? body) where T : class
        {
            if (body == null) return null;
            string json = _Serializer.SerializeJson(body, false) ?? "null";
            return _Serializer.DeserializeJson<T>(json);
        }

        private EnumerationQuery BindQuery(object? body)
        {
            EnumerationQuery query = Bind<EnumerationQuery>(body) ?? new EnumerationQuery();
            if (!query.Validate(out string? error)) throw new ArgumentException(error);
            return query;
        }

        private static string RequireContainer(WsRequestEnvelope request)
        {
            if (String.IsNullOrEmpty(request.Container)) throw new ArgumentException("Container is required.");
            return request.Container;
        }

        private static string RequireKey(WsRequestEnvelope request)
        {
            if (String.IsNullOrEmpty(request.Key)) throw new ArgumentException("Key is required.");
            return request.Key;
        }

        private static WsResponseEnvelope Ok(WsRequestEnvelope request, object? result, int statusCode = 200)
        {
            return new WsResponseEnvelope { RequestId = request.RequestId, Success = true, StatusCode = statusCode, Result = result };
        }

        private static WsResponseEnvelope Fail(string? requestId, ApiErrorEnum error, int statusCode, string message)
        {
            return new WsResponseEnvelope
            {
                RequestId = requestId,
                Success = false,
                StatusCode = statusCode,
                Error = new ApiErrorResponse(error, message, statusCode)
            };
        }

        private static WsResponseEnvelope Malformed(string message)
        {
            return new WsResponseEnvelope
            {
                RequestId = null,
                Success = false,
                StatusCode = 400,
                Error = new ApiErrorResponse(ApiErrorEnum.BadRequest, message, 400)
            };
        }

        #endregion
    }
}
