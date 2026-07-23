namespace PepperX.Server.Api.Rest
{
    using System;
    using System.Threading.Tasks;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Cross-container search routes.
    /// </summary>
    public sealed class SearchRoutes
    {
        #region Private-Members

        private readonly SearchService _Search;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate search routes.
        /// </summary>
        /// <param name="search">Search service.</param>
        /// <exception cref="ArgumentNullException"><paramref name="search"/> is null.</exception>
        public SearchRoutes(SearchService search)
        {
            _Search = search ?? throw new ArgumentNullException(nameof(search));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the search routes.
        /// </summary>
        /// <param name="server">Web server.</param>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Post<EnumerationQuery>("/v1.0/objects/enumerate", SearchAsync, openApi => openApi
                .WithTag("Search").WithDescription("Search objects across all containers by labels, tags, prefix, and more. Restrict with the query's container list.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json(null, "Enumeration query", false))
                .WithResponse(200, OpenApiResponseMetadata.Json("Paginated object metadata", null)));
        }

        #endregion

        #region Private-Methods

        private Task<object> SearchAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                EnumerationQuery query = request.GetData<EnumerationQuery>() ?? new EnumerationQuery();
                if (!query.Validate(out string? error)) throw new ArgumentException(error);
                return await _Search.SearchAllAsync(query, request.CancellationToken).ConfigureAwait(false);
            });
        }

        #endregion
    }
}
