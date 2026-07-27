namespace PepperX.Server.Api.Rest
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using ApiErrorResponse = PepperX.Core.Responses.ApiErrorResponse;

    /// <summary>
    /// Container management routes.
    /// </summary>
    public sealed class ContainerRoutes
    {
        #region Private-Members

        private readonly ContainerService _Containers;
        private readonly MultipartUploadService _Multipart;
        private readonly int _DefaultMaxUploads = 1000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate container routes.
        /// </summary>
        /// <param name="containers">Container service.</param>
        /// <param name="multipart">Multipart upload service.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public ContainerRoutes(ContainerService containers, MultipartUploadService multipart)
        {
            _Containers = containers ?? throw new ArgumentNullException(nameof(containers));
            _Multipart = multipart ?? throw new ArgumentNullException(nameof(multipart));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the container routes.
        /// </summary>
        /// <param name="server">Web server.</param>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Put<ContainerCreateRequest>("/v1.0/containers", CreateAsync, openApi => openApi
                .WithTag("Containers").WithDescription("Create a container.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json(null, "Container to create", true))
                .WithResponse(201, OpenApiResponseMetadata.Json("Created container", null))
                .WithResponse(409, OpenApiResponseMetadata.Create("Container already exists")));

            server.Get("/v1.0/containers", ListAsync, openApi => openApi
                .WithTag("Containers").WithDescription("List containers with pagination.")
                .WithParameter(OpenApiParameterMetadata.Query("maxResults", "Maximum results (1-1000)", false))
                .WithParameter(OpenApiParameterMetadata.Query("skip", "Records to skip", false))
                .WithParameter(OpenApiParameterMetadata.Query("prefix", "Name prefix filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("order", "Ordering", false))
                .WithResponse(200, OpenApiResponseMetadata.Json("Paginated containers", null)));

            server.Post<EnumerationQuery>("/v1.0/containers/enumerate", EnumerateAsync, openApi => openApi
                .WithTag("Containers").WithDescription("Enumerate containers with a query body.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json(null, "Enumeration query", false))
                .WithResponse(200, OpenApiResponseMetadata.Json("Paginated containers", null)));

            server.Get("/v1.0/containers/{container}", ReadAsync, openApi => openApi
                .WithTag("Containers").WithDescription("Read a container by name.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithResponse(200, OpenApiResponseMetadata.Json("Container", null))
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Head("/v1.0/containers/{container}", ExistsAsync, openApi => openApi
                .WithTag("Containers").WithDescription("Check whether a container exists.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithResponse(200, OpenApiResponseMetadata.Create("Exists"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Put<Dictionary<string, string>>("/v1.0/containers/{container}/tags", UpdateTagsAsync, openApi => openApi
                .WithTag("Containers").WithDescription("Replace a container's tags.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json(null, "Tag map", true))
                .WithResponse(200, OpenApiResponseMetadata.Json("Updated container", null))
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Delete("/v1.0/containers/{container}", DeleteAsync, openApi => openApi
                .WithTag("Containers").WithDescription("Delete a container. Pass force=true to delete a non-empty container's contents.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithParameter(OpenApiParameterMetadata.Query("force", "Delete contents when not empty", false))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(409, OpenApiResponseMetadata.Create("Container not empty")));

            server.Get("/v1.0/containers/{container}/cache", ReadCacheAsync, openApi => openApi
                .WithTag("Containers").WithDescription("Read a container's cache settings and this node's live cache statistics.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithResponse(200, OpenApiResponseMetadata.Json("Cache settings and statistics", null))
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Put<UpdateCacheSettingsRequest>("/v1.0/containers/{container}/cache", UpdateCacheAsync, openApi => openApi
                .WithTag("Containers").WithDescription("Replace a container's cache settings and apply them to the live cache.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json(null, "Cache settings", true))
                .WithResponse(200, OpenApiResponseMetadata.Json("Applied cache settings and statistics", null))
                .WithResponse(400, OpenApiResponseMetadata.Create("Invalid cache settings"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Get("/v1.0/containers/{container}/multipart-uploads", ListMultipartUploadsAsync, openApi => openApi
                .WithTag("Containers").WithDescription("List in-progress S3 multipart uploads for the container, paginated.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithParameter(OpenApiParameterMetadata.Query("keyMarker", "Resume after this object key", false))
                .WithParameter(OpenApiParameterMetadata.Query("uploadIdMarker", "Resume after this upload id (paired with keyMarker)", false))
                .WithParameter(OpenApiParameterMetadata.Query("maxUploads", "Maximum uploads to return (1-1000)", false))
                .WithResponse(200, OpenApiResponseMetadata.Json("Paginated in-progress uploads", null))
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Delete("/v1.0/containers/{container}/multipart-uploads/{uploadId}", AbortMultipartUploadAsync, openApi => openApi
                .WithTag("Containers").WithDescription("Abort an in-progress S3 multipart upload, discarding its staged parts.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithParameter(OpenApiParameterMetadata.Path("uploadId", "Upload id"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Put<UpdateRespIndexRequest>("/v1.0/containers/{container}/resp-index", UpdateRespIndexAsync, openApi => openApi
                .WithTag("Containers").WithDescription("Assign or clear the container's RESP (Redis) database index. A Redis client issuing SELECT n with the assigned index addresses this container. The index must be unique across containers; a null index clears the mapping.")
                .WithParameter(OpenApiParameterMetadata.Path("container", "Container name"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json(null, "The RESP database index to claim, or null to clear", true))
                .WithResponse(200, OpenApiResponseMetadata.Json("Updated container", null))
                .WithResponse(400, OpenApiResponseMetadata.Create("Negative index"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiResponseMetadata.Create("Index already assigned to another container")));
        }

        #endregion

        #region Private-Methods

        private Task<object> CreateAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                ContainerCreateRequest body = request.GetData<ContainerCreateRequest>() ?? new ContainerCreateRequest();
                ContainerResponse response = await _Containers.CreateAsync(body, request.CancellationToken).ConfigureAwait(false);
                request.Http.Response.StatusCode = 201;
                return response;
            });
        }

        private Task<object> ListAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                EnumerationQuery query = EnumerationQuery.FromQueryString(RouteHelpers.QueryToNvc(request));
                if (!query.Validate(out string? error)) throw new ArgumentException(error);
                return await _Containers.EnumerateAsync(query, request.CancellationToken).ConfigureAwait(false);
            });
        }

        private Task<object> EnumerateAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                EnumerationQuery query = request.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                if (!query.Validate(out string? error)) throw new ArgumentException(error);
                return await _Containers.EnumerateAsync(query, request.CancellationToken).ConfigureAwait(false);
            });
        }

        private Task<object> ReadAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string name = RouteHelpers.Container(request);
                ContainerResponse? response = await _Containers.ReadAsync(name, request.CancellationToken).ConfigureAwait(false);
                if (response == null)
                {
                    request.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse(Core.Enums.ApiErrorEnum.NotFound, "Container '" + name + "' not found.", 404);
                }
                return response;
            });
        }

        private Task<object> ExistsAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string name = RouteHelpers.Container(request);
                bool exists = await _Containers.ExistsAsync(name, request.CancellationToken).ConfigureAwait(false);
                request.Http.Response.StatusCode = exists ? 200 : 404;
                return null!;
            });
        }

        private Task<object> UpdateTagsAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string name = RouteHelpers.Container(request);
                Dictionary<string, string> tags = request.GetData<Dictionary<string, string>>() ?? new Dictionary<string, string>();
                return await _Containers.UpdateTagsAsync(name, tags, request.CancellationToken).ConfigureAwait(false);
            });
        }

        private Task<object> DeleteAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string name = RouteHelpers.Container(request);
                bool force = RouteHelpers.BoolQuery(request, "force");
                await _Containers.DeleteAsync(name, force, request.CancellationToken).ConfigureAwait(false);
                request.Http.Response.StatusCode = 204;
                return null!;
            });
        }

        private Task<object> ReadCacheAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string name = RouteHelpers.Container(request);
                return await _Containers.ReadCacheAsync(name, request.CancellationToken).ConfigureAwait(false);
            });
        }

        private Task<object> UpdateCacheAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string name = RouteHelpers.Container(request);
                UpdateCacheSettingsRequest body = request.GetData<UpdateCacheSettingsRequest>() ?? new UpdateCacheSettingsRequest();
                return await _Containers.UpdateCacheSettingsAsync(name, body, request.CancellationToken).ConfigureAwait(false);
            });
        }

        private Task<object> ListMultipartUploadsAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string name = RouteHelpers.Container(request);
                System.Collections.Specialized.NameValueCollection query = RouteHelpers.QueryToNvc(request);
                string? keyMarker = String.IsNullOrEmpty(query["keyMarker"]) ? null : query["keyMarker"];
                string? uploadIdMarker = String.IsNullOrEmpty(query["uploadIdMarker"]) ? null : query["uploadIdMarker"];
                int maxUploads = _DefaultMaxUploads;
                if (!String.IsNullOrEmpty(query["maxUploads"]) && Int32.TryParse(query["maxUploads"], out int parsed)) maxUploads = Math.Clamp(parsed, 1, _DefaultMaxUploads);

                MultipartUploadListResult result = await _Multipart.ListUploadsAsync(name, keyMarker, uploadIdMarker, maxUploads, request.CancellationToken).ConfigureAwait(false);

                MultipartUploadInfoPage page = new MultipartUploadInfoPage
                {
                    IsTruncated = result.IsTruncated,
                    NextKeyMarker = result.NextKeyMarker,
                    NextUploadIdMarker = result.NextUploadIdMarker
                };
                foreach (Core.Models.MultipartUpload upload in result.Uploads)
                {
                    page.Uploads.Add(new MultipartUploadInfo
                    {
                        UploadId = upload.Id,
                        Key = upload.Key,
                        ContentType = upload.ContentType,
                        InitiatedUtc = upload.InitiatedUtc,
                        ExpiresUtc = upload.ExpiresUtc
                    });
                }

                return page;
            });
        }

        private Task<object> AbortMultipartUploadAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string name = RouteHelpers.Container(request);
                string uploadId = request.Parameters["uploadId"] ?? String.Empty;
                await _Multipart.AbortAsync(name, uploadId, request.CancellationToken).ConfigureAwait(false);
                request.Http.Response.StatusCode = 204;
                return null!;
            });
        }

        private Task<object> UpdateRespIndexAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string name = RouteHelpers.Container(request);
                UpdateRespIndexRequest body = request.GetData<UpdateRespIndexRequest>() ?? new UpdateRespIndexRequest();
                return await _Containers.SetRespDatabaseIndexAsync(name, body.Index, request.CancellationToken).ConfigureAwait(false);
            });
        }

        #endregion
    }
}
