namespace PepperX.Server.Api.Rest
{
    using System;
    using System.Threading.Tasks;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Administrative routes: statistics, nodes, and rehydration.
    /// </summary>
    public sealed class AdminRoutes
    {
        #region Private-Members

        private readonly StatisticsService _Statistics;
        private readonly RehydrationService _Rehydration;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate admin routes.
        /// </summary>
        /// <param name="statistics">Statistics service.</param>
        /// <param name="rehydration">Rehydration service.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public AdminRoutes(StatisticsService statistics, RehydrationService rehydration)
        {
            _Statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            _Rehydration = rehydration ?? throw new ArgumentNullException(nameof(rehydration));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the admin routes.
        /// </summary>
        /// <param name="server">Web server.</param>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Get("/v1.0/admin/stats", StatsAsync, openApi => openApi
                .WithTag("Admin").WithDescription("Aggregate container, storage, database, and cluster statistics.")
                .WithResponse(200, OpenApiResponseMetadata.Json("Statistics", null)));

            server.Get("/v1.0/admin/nodes", NodesAsync, openApi => openApi
                .WithTag("Admin").WithDescription("List cluster nodes and their liveness.")
                .WithResponse(200, OpenApiResponseMetadata.Json("Nodes", null)));

            server.Post<RehydrationRequest>("/v1.0/admin/rehydrate", RehydrateAsync, openApi => openApi
                .WithTag("Admin").WithDescription("Reconcile the database with raw extent storage (Verify, Repair, or Rebuild).")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json(null, "Rehydration request", true))
                .WithResponse(200, OpenApiResponseMetadata.Json("Rehydration report", null)));
        }

        #endregion

        #region Private-Methods

        private Task<object> StatsAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                return await _Statistics.GetAsync(request.CancellationToken).ConfigureAwait(false);
            });
        }

        private Task<object> NodesAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                return await _Statistics.GetNodesAsync(request.CancellationToken).ConfigureAwait(false);
            });
        }

        private Task<object> RehydrateAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                RehydrationRequest body = request.GetData<RehydrationRequest>() ?? new RehydrationRequest();
                RehydrationReport report = await _Rehydration.RehydrateAsync(body.Mode, request.CancellationToken).ConfigureAwait(false);
                return report;
            });
        }

        #endregion
    }
}
