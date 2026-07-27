namespace PepperX.Server.Api.Rest
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;
    using PepperX.Core;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Enums;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using ApiErrorResponse = PepperX.Core.Responses.ApiErrorResponse;

    /// <summary>
    /// Object data-plane and object-listing routes. The object key is carried as the <c>key</c> query
    /// parameter so it may contain any characters, including slashes.
    /// </summary>
    public sealed class ObjectRoutes
    {
        #region Private-Members

        private const int _ChunkBytes = 65536;
        private readonly ObjectWriteService _Writes;
        private readonly ObjectReadService _Reads;
        private readonly ObjectDeleteService _Deletes;
        private readonly SearchService _Search;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate object routes.
        /// </summary>
        /// <param name="writes">Write service.</param>
        /// <param name="reads">Read service.</param>
        /// <param name="deletes">Delete service.</param>
        /// <param name="search">Search service.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public ObjectRoutes(ObjectWriteService writes, ObjectReadService reads, ObjectDeleteService deletes, SearchService search)
        {
            _Writes = writes ?? throw new ArgumentNullException(nameof(writes));
            _Reads = reads ?? throw new ArgumentNullException(nameof(reads));
            _Deletes = deletes ?? throw new ArgumentNullException(nameof(deletes));
            _Search = search ?? throw new ArgumentNullException(nameof(search));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the object routes.
        /// </summary>
        /// <param name="server">Web server.</param>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Put("/v1.0/containers/{container}/object", RawWriteAsync, openApi => openApi
                .WithTag("Objects").WithDescription("Write an object with the payload as the request body. Metadata via x-pepperx-* headers.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithParameter(OpenApiParameterMetadata.Query("key", "Object key", true))
                .WithParameter(OpenApiParameterMetadata.Query("nooverwrite", "Fail if the key exists", false))
                .WithResponse(201, OpenApiResponseMetadata.Json("Write result", null))
                .WithResponse(409, OpenApiResponseMetadata.Create("Key exists")));

            server.Post<WriteObjectRequest>("/v1.0/containers/{container}/object", EnvelopeWriteAsync, openApi => openApi
                .WithTag("Objects").WithDescription("Write an object using a JSON envelope with base64 payload and metadata.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithParameter(OpenApiParameterMetadata.Query("key", "Object key", true))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json(null, "Write envelope", true))
                .WithResponse(201, OpenApiResponseMetadata.Json("Write result", null)));

            server.Get("/v1.0/containers/{container}/object", ReadAsync, openApi => openApi
                .WithTag("Objects").WithDescription("Read an object payload. Supports the Range header.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithParameter(OpenApiParameterMetadata.Query("key", "Object key", true))
                .WithResponse(200, OpenApiResponseMetadata.Binary("Object payload"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Head("/v1.0/containers/{container}/object", HeadAsync, openApi => openApi
                .WithTag("Objects").WithDescription("Check object existence and return metadata headers.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithParameter(OpenApiParameterMetadata.Query("key", "Object key", true))
                .WithResponse(200, OpenApiResponseMetadata.Create("Exists"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Get("/v1.0/containers/{container}/object/metadata", ReadMetadataAsync, openApi => openApi
                .WithTag("Objects").WithDescription("Read an object's full metadata, including labels, tags, and the metadata object.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithParameter(OpenApiParameterMetadata.Query("key", "Object key", true))
                .WithResponse(200, OpenApiResponseMetadata.Json("Object metadata", null))
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Put<UpdateMetadataRequest>("/v1.0/containers/{container}/object/metadata", UpdateMetadataAsync, openApi => openApi
                .WithTag("Objects").WithDescription("Update an object's metadata (rewrites the extent, preserving the payload).")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithParameter(OpenApiParameterMetadata.Query("key", "Object key", true))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json(null, "Metadata update", true))
                .WithResponse(200, OpenApiResponseMetadata.Json("Write result", null))
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Delete("/v1.0/containers/{container}/object", DeleteAsync, openApi => openApi
                .WithTag("Objects").WithDescription("Delete an object. Waits for in-flight reads before destroying the payload.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithParameter(OpenApiParameterMetadata.Query("key", "Object key", true))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Get("/v1.0/containers/{container}/objects", ListAsync, openApi => openApi
                .WithTag("Objects").WithDescription("List objects in a container with pagination and filters.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithParameter(OpenApiParameterMetadata.Query("maxResults", "Maximum results (1-1000)", false))
                .WithParameter(OpenApiParameterMetadata.Query("prefix", "Key prefix", false))
                .WithResponse(200, OpenApiResponseMetadata.Json("Paginated object metadata", null)));

            server.Post<EnumerationQuery>("/v1.0/containers/{container}/objects/enumerate", EnumerateAsync, openApi => openApi
                .WithTag("Objects").WithDescription("Search objects in a container by labels, tags, prefix, and more.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json(null, "Enumeration query", false))
                .WithResponse(200, OpenApiResponseMetadata.Json("Paginated object metadata", null)));
        }

        #endregion

        #region Private-Methods

        private Task<object> RawWriteAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string container = RouteHelpers.Container(request);
                string key = RouteHelpers.Key(request);
                bool noOverwrite = RouteHelpers.BoolQuery(request, "nooverwrite");
                string? contentType = request.Http.Request.ContentType;
                List<string>? labels = RouteHelpers.ParseLabelsHeader(request);
                Dictionary<string, string>? tags = RouteHelpers.ParseTagsHeader(request);
                object? metadataObject = RouteHelpers.ParseObjectHeader(request);

                // A request that declares a length streams straight through without buffering. A chunked
                // upload (a client streaming a body of unknown length) is decoded by the web server instead:
                // its framing and the connection's read position are owned by the server's parser, and
                // consuming the socket directly leaves the connection unusable for the next request.
                // Chunked uploads are therefore materialized, and are bounded by the same size limits.
                ObjectWriteResponse response;
                if (request.Http.Request.ChunkedTransfer)
                {
                    byte[] decoded = request.Http.Request.DataAsBytes ?? Array.Empty<byte>();
                    using (MemoryStream buffered = new MemoryStream(decoded, false))
                    {
                        response = await _Writes.WriteAsync(container, key, buffered, contentType, labels, tags, metadataObject, noOverwrite, request.CancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    Stream payload = request.Http.Request.Data ?? Stream.Null;
                    response = await _Writes.WriteAsync(container, key, payload, contentType, labels, tags, metadataObject, noOverwrite, request.CancellationToken).ConfigureAwait(false);
                }

                request.Http.Response.StatusCode = 201;
                return response;
            });
        }

        private Task<object> EnvelopeWriteAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string container = RouteHelpers.Container(request);
                string key = RouteHelpers.Key(request);
                WriteObjectRequest body = request.GetData<WriteObjectRequest>() ?? new WriteObjectRequest();
                byte[] data = String.IsNullOrEmpty(body.DataBase64) ? Array.Empty<byte>() : Convert.FromBase64String(body.DataBase64);

                using (MemoryStream ms = new MemoryStream(data))
                {
                    ObjectWriteResponse response = await _Writes.WriteAsync(container, key, ms, body.ContentType, body.Labels, body.Tags, body.Object, body.NoOverwrite, request.CancellationToken).ConfigureAwait(false);
                    request.Http.Response.StatusCode = 201;
                    return response;
                }
            });
        }

        private Task<object> ReadAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string container = RouteHelpers.Container(request);
                string key = RouteHelpers.Key(request);
                ParseRange(request, out long? offset, out long? count);

                ObjectReadHandle? handle = await _Reads.ReadAsync(container, key, offset, count, request.CancellationToken).ConfigureAwait(false);
                if (handle == null)
                {
                    request.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse(ApiErrorEnum.NotFound, "Object '" + key + "' not found.", 404);
                }

                await using (handle)
                {
                    HttpResponseBase response = request.Http.Response;
                    response.StatusCode = offset.HasValue ? 206 : 200;
                    response.ContentType = handle.Extent.ContentType ?? Constants.OctetStreamContentType;
                    response.Headers.Add(Constants.ExtentIdHeader, handle.Extent.Id);
                    response.Headers.Add(Constants.Sha256Header, handle.Extent.Sha256);
                    if (!String.IsNullOrEmpty(handle.Extent.Md5)) response.Headers.Add(Constants.Md5Header, handle.Extent.Md5);
                    if (handle.Extent.HasMetadataObject) response.Headers.Add(Constants.ObjectAvailableHeader, "true");
                    if (offset.HasValue)
                    {
                        long end = offset.Value + handle.Payload.Length - 1;
                        response.Headers.Add("Content-Range", "bytes " + offset.Value + "-" + end + "/" + handle.Extent.SizeBytes);
                    }
                    // Send the payload as a length-declared stream in a single call rather than
                    // hand-rolling chunked framing.
                    //
                    // Chunking here emitted the terminator twice: the loop wrote its own final chunk,
                    // and then the typed-route wrapper sent an empty response for the null return,
                    // appending a second "0\r\n\r\n". curl tolerated the trailing bytes; Node's HTTP
                    // parser rejected the whole response with "Data after Connection: close", which
                    // broke every Node-based client on object reads.
                    //
                    // Content-Length is also simply better here: the size is known up front, so
                    // clients can show progress and size the buffer, and range responses no longer
                    // have to reconcile Content-Range against chunked framing.
                    await response.Send(handle.Payload.Length, handle.Payload, request.CancellationToken).ConfigureAwait(false);
                    return null!;
                }
            });
        }

        private Task<object> HeadAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string container = RouteHelpers.Container(request);
                string key = RouteHelpers.Key(request);
                ObjectMetadata? metadata = await _Reads.ReadMetadataAsync(container, key, request.CancellationToken).ConfigureAwait(false);
                if (metadata == null)
                {
                    request.Http.Response.StatusCode = 404;
                    return null!;
                }

                HttpResponseBase response = request.Http.Response;
                response.Headers.Add(Constants.ExtentIdHeader, metadata.ExtentId);
                response.Headers.Add(Constants.Sha256Header, metadata.Sha256);
                if (!String.IsNullOrEmpty(metadata.Md5)) response.Headers.Add(Constants.Md5Header, metadata.Md5);
                if (!String.IsNullOrEmpty(metadata.ContentType)) response.ContentType = metadata.ContentType;
                if (metadata.HasMetadataObject) response.Headers.Add(Constants.ObjectAvailableHeader, "true");
                response.StatusCode = 200;
                return null!;
            });
        }

        private Task<object> ReadMetadataAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string container = RouteHelpers.Container(request);
                string key = RouteHelpers.Key(request);
                ObjectMetadata? metadata = await _Reads.ReadMetadataAsync(container, key, request.CancellationToken).ConfigureAwait(false);
                if (metadata == null)
                {
                    request.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse(ApiErrorEnum.NotFound, "Object '" + key + "' not found.", 404);
                }
                return metadata;
            });
        }

        private Task<object> UpdateMetadataAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string container = RouteHelpers.Container(request);
                string key = RouteHelpers.Key(request);
                UpdateMetadataRequest body = request.GetData<UpdateMetadataRequest>() ?? new UpdateMetadataRequest();
                return await _Writes.UpdateMetadataAsync(container, key, body, request.CancellationToken).ConfigureAwait(false);
            });
        }

        private Task<object> DeleteAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string container = RouteHelpers.Container(request);
                string key = RouteHelpers.Key(request);
                bool deleted = await _Deletes.DeleteAsync(container, key, request.CancellationToken).ConfigureAwait(false);
                if (!deleted)
                {
                    request.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse(ApiErrorEnum.NotFound, "Object '" + key + "' not found.", 404);
                }
                request.Http.Response.StatusCode = 204;
                return null!;
            });
        }

        private Task<object> ListAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string container = RouteHelpers.Container(request);
                EnumerationQuery query = EnumerationQuery.FromQueryString(RouteHelpers.QueryToNvc(request));
                if (!query.Validate(out string? error)) throw new ArgumentException(error);
                return await _Search.EnumerateContainerAsync(container, query, request.CancellationToken).ConfigureAwait(false);
            });
        }

        private Task<object> EnumerateAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string container = RouteHelpers.Container(request);
                EnumerationQuery query = request.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                if (!query.Validate(out string? error)) throw new ArgumentException(error);
                return await _Search.EnumerateContainerAsync(container, query, request.CancellationToken).ConfigureAwait(false);
            });
        }

        private static void ParseRange(ApiRequest request, out long? offset, out long? count)
        {
            offset = null;
            count = null;
            string? range = request.Headers["Range"];
            if (String.IsNullOrEmpty(range) || !range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return;

            string spec = range.Substring("bytes=".Length);
            int dash = spec.IndexOf('-');
            if (dash < 0) return;

            string startText = spec.Substring(0, dash);
            string endText = spec.Substring(dash + 1);
            if (!Int64.TryParse(startText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long start)) return;
            offset = start;

            if (Int64.TryParse(endText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long end) && end >= start)
            {
                count = end - start + 1;
            }
        }

        #endregion
    }
}
