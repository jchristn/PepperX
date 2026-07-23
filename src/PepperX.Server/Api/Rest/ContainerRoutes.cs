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

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate container routes.
        /// </summary>
        /// <param name="containers">Container service.</param>
        /// <exception cref="ArgumentNullException"><paramref name="containers"/> is null.</exception>
        public ContainerRoutes(ContainerService containers)
        {
            _Containers = containers ?? throw new ArgumentNullException(nameof(containers));
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

        #endregion
    }
}
