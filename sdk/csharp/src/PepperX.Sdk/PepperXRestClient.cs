namespace PepperX.Sdk
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using PepperX.Sdk.Models;

    /// <summary>
    /// Typed client for the PepperX native REST API. Every method returns a typed object rather than a raw
    /// response body. PepperX is unauthenticated by design, so no credentials are required.
    /// This type is thread-safe and intended to be created once and reused.
    /// </summary>
    public sealed class PepperXRestClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL of the target PepperX node.
        /// </summary>
        public string BaseUrl
        {
            get
            {
                return _BaseUrl;
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

        private readonly string _BaseUrl;
        private readonly HttpClient _Http;
        private readonly bool _OwnsHttp;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a client against a PepperX node.
        /// </summary>
        /// <param name="baseUrl">Base URL, for example <c>http://localhost:8000</c>.</param>
        /// <param name="timeout">Request timeout. Default 100 seconds.</param>
        /// <exception cref="ArgumentNullException"><paramref name="baseUrl"/> is null or empty.</exception>
        public PepperXRestClient(string baseUrl, TimeSpan? timeout = null)
        {
            if (String.IsNullOrEmpty(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));

            _BaseUrl = baseUrl.TrimEnd('/');
            _Http = new HttpClient { BaseAddress = new Uri(_BaseUrl), Timeout = timeout ?? TimeSpan.FromSeconds(100) };
            _OwnsHttp = true;
        }

        /// <summary>
        /// Instantiate a client over a caller-supplied <see cref="HttpClient"/>. The client is not disposed by
        /// this instance.
        /// </summary>
        /// <param name="baseUrl">Base URL.</param>
        /// <param name="httpClient">HTTP client to use.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PepperXRestClient(string baseUrl, HttpClient httpClient)
        {
            if (String.IsNullOrEmpty(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            _BaseUrl = baseUrl.TrimEnd('/');
            _Http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _OwnsHttp = false;
        }

        #endregion

        #region Public-Methods-Health

        /// <summary>
        /// Check that the node is reachable and healthy.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the node responds successfully.</returns>
        public async Task<bool> HealthAsync(CancellationToken token = default)
        {
            try
            {
                using (HttpResponseMessage response = await _Http.GetAsync(Path("/v1.0/api/health"), token).ConfigureAwait(false))
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        #endregion

        #region Public-Methods-Containers

        /// <summary>
        /// Create a container.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="tags">Optional container tags.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created container.</returns>
        /// <exception cref="PepperXException">The server rejected the request, for example when the name exists.</exception>
        public async Task<ContainerResponse> CreateContainerAsync(string name, Dictionary<string, string>? tags = null, CancellationToken token = default)
        {
            object body = tags == null ? new { Name = name } : (object)new { Name = name, Tags = tags };
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, Path("/v1.0/containers")) { Content = JsonBody(body) })
            {
                return await SendAsync<ContainerResponse>(request, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Read a container by name.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The container, or null when it does not exist.</returns>
        public async Task<ContainerResponse?> ReadContainerAsync(string name, CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Path("/v1.0/containers/" + Escape(name))))
            {
                return await SendOrNullAsync<ContainerResponse>(request, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Determine whether a container exists.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the container exists.</returns>
        public async Task<bool> ContainerExistsAsync(string name, CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, Path("/v1.0/containers/" + Escape(name))))
            using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
            {
                return response.StatusCode != HttpStatusCode.NotFound;
            }
        }

        /// <summary>
        /// Enumerate containers.
        /// </summary>
        /// <param name="query">Enumeration query. Null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of containers.</returns>
        public async Task<EnumerationResult<ContainerResponse>> EnumerateContainersAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Path("/v1.0/containers/enumerate")) { Content = JsonBody(query ?? new EnumerationQuery()) })
            {
                return await SendAsync<EnumerationResult<ContainerResponse>>(request, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Replace a container's tags.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="tags">New tag set.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated container.</returns>
        /// <exception cref="PepperXException">The container does not exist.</exception>
        public async Task<ContainerResponse> UpdateContainerTagsAsync(string name, Dictionary<string, string> tags, CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, Path("/v1.0/containers/" + Escape(name) + "/tags")) { Content = JsonBody(tags ?? new Dictionary<string, string>()) })
            {
                return await SendAsync<ContainerResponse>(request, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Delete a container.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="force">When true, deletes the container's objects first.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="PepperXException">The container is missing, or is not empty and force was not requested.</exception>
        public async Task DeleteContainerAsync(string name, bool force = false, CancellationToken token = default)
        {
            string path = Path("/v1.0/containers/" + Escape(name)) + (force ? "?force=true" : String.Empty);
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, path))
            using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
            {
                await EnsureSuccessAsync(response, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Public-Methods-Objects

        /// <summary>
        /// Write an object from a byte array.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key. May contain any characters, including slashes.</param>
        /// <param name="data">Payload bytes.</param>
        /// <param name="metadata">Optional content type, labels, tags, metadata object, and overwrite behavior.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The write result.</returns>
        /// <exception cref="PepperXException">The server rejected the write.</exception>
        public async Task<ObjectWriteResponse> WriteObjectAsync(string container, string key, byte[] data, WriteObjectRequest? metadata = null, CancellationToken token = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            using (MemoryStream stream = new MemoryStream(data, false))
            {
                return await WriteObjectAsync(container, key, stream, metadata, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Write an object by streaming a payload, without buffering it in memory.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="payload">Payload stream, read to end.</param>
        /// <param name="metadata">Optional content type, labels, tags, metadata object, and overwrite behavior.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The write result.</returns>
        /// <exception cref="PepperXException">The server rejected the write.</exception>
        public async Task<ObjectWriteResponse> WriteObjectAsync(string container, string key, Stream payload, WriteObjectRequest? metadata = null, CancellationToken token = default)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            string path = ObjectPath(container, key);
            if (metadata != null && metadata.NoOverwrite) path += "&nooverwrite=true";

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, path))
            using (StreamContent content = new StreamContent(payload))
            {
                if (metadata != null)
                {
                    if (!String.IsNullOrEmpty(metadata.ContentType))
                        content.Headers.TryAddWithoutValidation("Content-Type", metadata.ContentType);

                    if (metadata.Labels != null && metadata.Labels.Count > 0)
                        content.Headers.TryAddWithoutValidation("x-pepperx-labels", String.Join(",", metadata.Labels));

                    if (metadata.Tags != null && metadata.Tags.Count > 0)
                        content.Headers.TryAddWithoutValidation("x-pepperx-tags", EncodeTags(metadata.Tags));

                    if (metadata.Object != null)
                    {
                        string json = JsonSerializer.Serialize(metadata.Object, _Json);
                        content.Headers.TryAddWithoutValidation("x-pepperx-object", Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
                    }
                }

                request.Content = content;
                return await SendAsync<ObjectWriteResponse>(request, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Read an object into memory.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The payload and identifying headers, or null when the object does not exist.</returns>
        public async Task<ObjectReadResult?> ReadObjectAsync(string container, string key, CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, ObjectPath(container, key)))
            using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                await EnsureSuccessAsync(response, token).ConfigureAwait(false);

                return new ObjectReadResult
                {
                    Data = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false),
                    ContentType = response.Content.Headers.ContentType?.ToString(),
                    ExtentId = Header(response, "x-pepperx-extent-id"),
                    Sha256 = Header(response, "x-pepperx-sha256"),
                    HasMetadataObject = Header(response, "x-pepperx-object-available") == "true"
                };
            }
        }

        /// <summary>
        /// Open an object for streaming, without buffering it in memory. The caller disposes the returned
        /// stream.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A readable payload stream, or null when the object does not exist.</returns>
        public async Task<Stream?> OpenObjectAsync(string container, string key, CancellationToken token = default)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, ObjectPath(container, key));
            HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                request.Dispose();
                return null;
            }

            try
            {
                await EnsureSuccessAsync(response, token).ConfigureAwait(false);
                return await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                response.Dispose();
                request.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Read an object's metadata, including labels, tags, and the freeform metadata object.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The metadata, or null when the object does not exist.</returns>
        public async Task<ObjectMetadata?> ReadObjectMetadataAsync(string container, string key, CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, ObjectMetadataPath(container, key)))
            {
                return await SendOrNullAsync<ObjectMetadata>(request, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Update an object's metadata. The payload is preserved.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="update">Metadata changes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The write result for the rewritten object.</returns>
        /// <exception cref="PepperXException">The object does not exist.</exception>
        public async Task<ObjectWriteResponse> UpdateObjectMetadataAsync(string container, string key, UpdateMetadataRequest update, CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, ObjectMetadataPath(container, key)) { Content = JsonBody(update ?? new UpdateMetadataRequest()) })
            {
                return await SendAsync<ObjectWriteResponse>(request, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Determine whether an object exists.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the object exists.</returns>
        public async Task<bool> ObjectExistsAsync(string container, string key, CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, ObjectPath(container, key)))
            using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
            {
                return response.StatusCode != HttpStatusCode.NotFound;
            }
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
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, ObjectPath(container, key)))
            using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return false;
                await EnsureSuccessAsync(response, token).ConfigureAwait(false);
                return true;
            }
        }

        /// <summary>
        /// Enumerate or search objects within a container by key prefix, labels, and tags.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="query">Enumeration query. Null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of object metadata.</returns>
        public async Task<EnumerationResult<ObjectMetadata>> EnumerateObjectsAsync(string container, EnumerationQuery? query = null, CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Path("/v1.0/containers/" + Escape(container) + "/objects/enumerate")) { Content = JsonBody(query ?? new EnumerationQuery()) })
            {
                return await SendAsync<EnumerationResult<ObjectMetadata>>(request, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Search objects across containers.
        /// </summary>
        /// <param name="query">Enumeration query; set its container list to restrict the search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of object metadata.</returns>
        public async Task<EnumerationResult<ObjectMetadata>> SearchAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Path("/v1.0/objects/enumerate")) { Content = JsonBody(query ?? new EnumerationQuery()) })
            {
                return await SendAsync<EnumerationResult<ObjectMetadata>>(request, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Public-Methods-Admin

        /// <summary>
        /// Read aggregate statistics.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Statistics across containers, storage, database, and nodes.</returns>
        public async Task<StatisticsResponse> StatisticsAsync(CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Path("/v1.0/admin/stats")))
            {
                return await SendAsync<StatisticsResponse>(request, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// List cluster nodes.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Cluster nodes and their liveness.</returns>
        public async Task<List<NodeResponse>> NodesAsync(CancellationToken token = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Path("/v1.0/admin/nodes")))
            {
                return await SendAsync<List<NodeResponse>>(request, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Public-Methods-Lifecycle

        /// <summary>
        /// Dispose the client, releasing the underlying HTTP client when this instance created it.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            if (_OwnsHttp) _Http.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private string Path(string relative)
        {
            return _BaseUrl + relative;
        }

        private string ObjectPath(string container, string key)
        {
            return Path("/v1.0/containers/" + Escape(container) + "/object?key=" + Escape(key));
        }

        private string ObjectMetadataPath(string container, string key)
        {
            return Path("/v1.0/containers/" + Escape(container) + "/object/metadata?key=" + Escape(key));
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value ?? String.Empty);
        }

        private static string EncodeTags(Dictionary<string, string> tags)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> tag in tags)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(HttpUtility.UrlEncode(tag.Key));
                sb.Append('=');
                sb.Append(HttpUtility.UrlEncode(tag.Value));
            }
            return sb.ToString();
        }

        private static StringContent JsonBody(object body)
        {
            return new StringContent(JsonSerializer.Serialize(body, _Json), Encoding.UTF8, "application/json");
        }

        private static string? Header(HttpResponseMessage response, string name)
        {
            if (response.Headers.TryGetValues(name, out IEnumerable<string>? values))
            {
                foreach (string value in values) return value;
            }
            return null;
        }

        private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken token)
        {
            using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
            {
                await EnsureSuccessAsync(response, token).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (String.IsNullOrEmpty(body)) return default!;
                return JsonSerializer.Deserialize<T>(body, _Json)!;
            }
        }

        private async Task<T?> SendOrNullAsync<T>(HttpRequestMessage request, CancellationToken token) where T : class
        {
            using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                await EnsureSuccessAsync(response, token).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (String.IsNullOrEmpty(body)) return null;
                return JsonSerializer.Deserialize<T>(body, _Json);
            }
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
        {
            if (response.IsSuccessStatusCode) return;

            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            ApiErrorEnum errorType = ApiErrorEnum.InternalError;
            string message = "Request failed with status " + (int)response.StatusCode + ".";

            if (!String.IsNullOrEmpty(body))
            {
                try
                {
                    ApiErrorResponse? error = JsonSerializer.Deserialize<ApiErrorResponse>(body, _Json);
                    if (error != null)
                    {
                        errorType = error.Error;
                        if (!String.IsNullOrEmpty(error.Message)) message = error.Message;
                    }
                }
                catch (JsonException)
                {
                    // The body was not a typed error payload; fall back to the status-derived message.
                }
            }

            throw new PepperXException(errorType, (int)response.StatusCode, message, body);
        }

        #endregion
    }
}
