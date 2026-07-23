namespace PepperX.Sdk
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Sdk.Models;

    /// <summary>
    /// Typed client for the PepperX WebSocket surface. Requests are correlated to responses by identifier, so
    /// many operations may be in flight on one connection. Call <see cref="ConnectAsync"/> before issuing
    /// operations. This type is thread-safe for concurrent operations on a connected client.
    /// </summary>
    public sealed class PepperXWebsocketClient : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// WebSocket URL of the target PepperX node.
        /// </summary>
        public string Url
        {
            get
            {
                return _Url;
            }
        }

        /// <summary>
        /// Whether the client is currently connected.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                return _Socket != null && _Socket.State == WebSocketState.Open;
            }
        }

        /// <summary>
        /// How long an operation waits for its correlated response. Clamped to 1 second to 30 minutes.
        /// Default 100 seconds.
        /// </summary>
        public TimeSpan Timeout
        {
            get
            {
                return _Timeout;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) value = TimeSpan.FromSeconds(1);
                if (value > TimeSpan.FromMinutes(30)) value = TimeSpan.FromMinutes(30);
                _Timeout = value;
            }
        }

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _Url;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<WsResponseEnvelope>> _Pending =
            new ConcurrentDictionary<string, TaskCompletionSource<WsResponseEnvelope>>();
        private readonly SemaphoreSlim _SendGate = new SemaphoreSlim(1, 1);

        private ClientWebSocket? _Socket;
        private CancellationTokenSource? _Cts;
        private Task? _ReceiveLoop;
        private TimeSpan _Timeout = TimeSpan.FromSeconds(100);
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a client for a PepperX WebSocket endpoint.
        /// </summary>
        /// <param name="url">WebSocket URL, for example <c>ws://localhost:8002/</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="url"/> is null or empty.</exception>
        public PepperXWebsocketClient(string url)
        {
            if (String.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));
            _Url = url;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Connect to the server and start the receive loop.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ConnectAsync(CancellationToken token = default)
        {
            if (IsConnected) return;

            _Socket = new ClientWebSocket();
            await _Socket.ConnectAsync(new Uri(_Url), token).ConfigureAwait(false);

            _Cts = new CancellationTokenSource();
            _ReceiveLoop = Task.Run(() => ReceiveLoopAsync(_Cts.Token));
        }

        /// <summary>
        /// Check that the node responds.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the health operation succeeds.</returns>
        public async Task<bool> HealthAsync(CancellationToken token = default)
        {
            WsResponseEnvelope response = await SendAsync(WsOperationEnum.Health, null, null, null, token).ConfigureAwait(false);
            return response.Success;
        }

        /// <summary>
        /// Create a container.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="tags">Optional container tags.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created container.</returns>
        /// <exception cref="PepperXException">The server rejected the request.</exception>
        public async Task<ContainerResponse> CreateContainerAsync(string name, Dictionary<string, string>? tags = null, CancellationToken token = default)
        {
            object body = tags == null ? new { Name = name } : (object)new { Name = name, Tags = tags };
            WsResponseEnvelope response = await SendAsync(WsOperationEnum.ContainerCreate, null, null, body, token).ConfigureAwait(false);
            return Require<ContainerResponse>(response);
        }

        /// <summary>
        /// Read a container by name.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The container, or null when it does not exist.</returns>
        public async Task<ContainerResponse?> ReadContainerAsync(string name, CancellationToken token = default)
        {
            WsResponseEnvelope response = await SendAsync(WsOperationEnum.ContainerRead, name, null, null, token).ConfigureAwait(false);
            if (response.StatusCode == 404) return null;
            return Require<ContainerResponse>(response);
        }

        /// <summary>
        /// Enumerate containers.
        /// </summary>
        /// <param name="query">Enumeration query. Null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of containers.</returns>
        public async Task<EnumerationResult<ContainerResponse>> EnumerateContainersAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            WsResponseEnvelope response = await SendAsync(WsOperationEnum.ContainerEnumerate, null, null, query ?? new EnumerationQuery(), token).ConfigureAwait(false);
            return Require<EnumerationResult<ContainerResponse>>(response);
        }

        /// <summary>
        /// Delete a container.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="PepperXException">The container is missing or not empty.</exception>
        public async Task DeleteContainerAsync(string name, CancellationToken token = default)
        {
            WsResponseEnvelope response = await SendAsync(WsOperationEnum.ContainerDelete, name, null, null, token).ConfigureAwait(false);
            Throw(response);
        }

        /// <summary>
        /// Write an object.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="data">Payload bytes.</param>
        /// <param name="metadata">Optional content type, labels, tags, metadata object, and overwrite behavior.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The write result.</returns>
        /// <exception cref="PepperXException">The server rejected the write.</exception>
        public async Task<ObjectWriteResponse> WriteObjectAsync(string container, string key, byte[] data, WriteObjectRequest? metadata = null, CancellationToken token = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            WriteObjectRequest request = metadata ?? new WriteObjectRequest();
            object body = new
            {
                request.ContentType,
                request.Labels,
                request.Tags,
                request.Object,
                request.NoOverwrite,
                DataBase64 = Convert.ToBase64String(data)
            };

            WsResponseEnvelope response = await SendAsync(WsOperationEnum.ObjectWrite, container, key, body, token).ConfigureAwait(false);
            return Require<ObjectWriteResponse>(response);
        }

        /// <summary>
        /// Read an object's payload.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The payload bytes, or null when the object does not exist.</returns>
        public async Task<byte[]?> ReadObjectAsync(string container, string key, CancellationToken token = default)
        {
            WsResponseEnvelope response = await SendAsync(WsOperationEnum.ObjectRead, container, key, null, token).ConfigureAwait(false);
            if (response.StatusCode == 404) return null;
            Throw(response);
            return String.IsNullOrEmpty(response.DataBase64) ? Array.Empty<byte>() : Convert.FromBase64String(response.DataBase64);
        }

        /// <summary>
        /// Read an object's metadata.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The metadata, or null when the object does not exist.</returns>
        public async Task<ObjectMetadata?> ReadObjectMetadataAsync(string container, string key, CancellationToken token = default)
        {
            WsResponseEnvelope response = await SendAsync(WsOperationEnum.ObjectReadMetadata, container, key, null, token).ConfigureAwait(false);
            if (response.StatusCode == 404) return null;
            return Require<ObjectMetadata>(response);
        }

        /// <summary>
        /// Delete an object.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when an object was deleted; false when it did not exist.</returns>
        public async Task<bool> DeleteObjectAsync(string container, string key, CancellationToken token = default)
        {
            WsResponseEnvelope response = await SendAsync(WsOperationEnum.ObjectDelete, container, key, null, token).ConfigureAwait(false);
            if (response.StatusCode == 404) return false;
            Throw(response);
            return true;
        }

        /// <summary>
        /// Enumerate or search objects within a container.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="query">Enumeration query. Null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of object metadata.</returns>
        public async Task<EnumerationResult<ObjectMetadata>> EnumerateObjectsAsync(string container, EnumerationQuery? query = null, CancellationToken token = default)
        {
            WsResponseEnvelope response = await SendAsync(WsOperationEnum.ObjectEnumerate, container, null, query ?? new EnumerationQuery(), token).ConfigureAwait(false);
            return Require<EnumerationResult<ObjectMetadata>>(response);
        }

        /// <summary>
        /// Search objects across containers.
        /// </summary>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of object metadata.</returns>
        public async Task<EnumerationResult<ObjectMetadata>> SearchAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            WsResponseEnvelope response = await SendAsync(WsOperationEnum.SearchEnumerate, null, null, query ?? new EnumerationQuery(), token).ConfigureAwait(false);
            return Require<EnumerationResult<ObjectMetadata>>(response);
        }

        /// <summary>
        /// Read aggregate statistics.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Statistics across containers, storage, database, and nodes.</returns>
        public async Task<StatisticsResponse> StatisticsAsync(CancellationToken token = default)
        {
            WsResponseEnvelope response = await SendAsync(WsOperationEnum.AdminStats, null, null, null, token).ConfigureAwait(false);
            return Require<StatisticsResponse>(response);
        }

        /// <summary>
        /// Close the connection and release resources.
        /// </summary>
        /// <returns>Value task.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;
            _Disposed = true;

            try { _Cts?.Cancel(); } catch (Exception) { }

            if (_Socket != null && _Socket.State == WebSocketState.Open)
            {
                try
                {
                    await _Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The peer may already be gone; closing is best-effort.
                }
            }

            _Socket?.Dispose();
            _SendGate.Dispose();
            _Cts?.Dispose();

            foreach (KeyValuePair<string, TaskCompletionSource<WsResponseEnvelope>> pending in _Pending)
            {
                pending.Value.TrySetException(new PepperXException(ApiErrorEnum.InternalError, 499, "The client was disposed before a response arrived."));
            }
            _Pending.Clear();
        }

        #endregion

        #region Private-Methods

        private async Task<WsResponseEnvelope> SendAsync(WsOperationEnum operation, string? container, string? key, object? body, CancellationToken token)
        {
            if (!IsConnected) throw new InvalidOperationException("The client is not connected; call ConnectAsync first.");

            string requestId = Guid.NewGuid().ToString("N");
            TaskCompletionSource<WsResponseEnvelope> completion = new TaskCompletionSource<WsResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            _Pending[requestId] = completion;

            object envelope = new
            {
                RequestId = requestId,
                Operation = operation.ToString(),
                Container = container,
                Key = key,
                Body = body
            };

            byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, _Json));

            await _SendGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await _Socket!.SendAsync(payload, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            }
            finally
            {
                _SendGate.Release();
            }

            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(_Timeout);
                using (timeout.Token.Register(() => completion.TrySetException(new PepperXException(ApiErrorEnum.InternalError, 504, "Timed out waiting for a WebSocket response."))))
                {
                    try
                    {
                        return await completion.Task.ConfigureAwait(false);
                    }
                    finally
                    {
                        _Pending.TryRemove(requestId, out _);
                    }
                }
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            byte[] buffer = new byte[65536];

            try
            {
                while (!token.IsCancellationRequested && _Socket != null && _Socket.State == WebSocketState.Open)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _Socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close) return;
                            ms.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        WsResponseEnvelope? envelope = JsonSerializer.Deserialize<WsResponseEnvelope>(Encoding.UTF8.GetString(ms.ToArray()), _Json);
                        if (envelope?.RequestId != null && _Pending.TryGetValue(envelope.RequestId, out TaskCompletionSource<WsResponseEnvelope>? completion))
                        {
                            completion.TrySetResult(envelope);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on dispose.
            }
            catch (Exception ex)
            {
                foreach (KeyValuePair<string, TaskCompletionSource<WsResponseEnvelope>> pending in _Pending)
                {
                    pending.Value.TrySetException(new PepperXException(ApiErrorEnum.InternalError, 500, "The WebSocket connection failed: " + ex.Message));
                }
                _Pending.Clear();
            }
        }

        private static T Require<T>(WsResponseEnvelope response) where T : class
        {
            Throw(response);
            if (response.Result == null) throw new PepperXException(ApiErrorEnum.InternalError, response.StatusCode, "The server returned no result payload.");
            return JsonSerializer.Deserialize<T>(response.Result.Value.GetRawText(), _Json)!;
        }

        private static void Throw(WsResponseEnvelope response)
        {
            if (response.Success) return;
            ApiErrorEnum errorType = response.Error?.Error ?? ApiErrorEnum.InternalError;
            string message = response.Error?.Message ?? "The operation failed with status " + response.StatusCode + ".";
            throw new PepperXException(errorType, response.StatusCode, message);
        }

        #endregion
    }
}
