namespace PepperX.Server.Api.Rest
{
    using System;
    using System.Threading.Tasks;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using PepperX.Core.Settings;
    using SyslogLogging;
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
        private readonly PepperXSettings _Settings;
        private readonly string _NodeId;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[AdminRoutes] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate admin routes.
        /// </summary>
        /// <param name="statistics">Statistics service.</param>
        /// <param name="rehydration">Rehydration service.</param>
        /// <param name="settings">Node settings.</param>
        /// <param name="nodeId">Resolved node identifier.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public AdminRoutes(StatisticsService statistics, RehydrationService rehydration, PepperXSettings settings, string nodeId, LoggingModule logging)
        {
            _Statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            _Rehydration = rehydration ?? throw new ArgumentNullException(nameof(rehydration));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
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

            server.Get("/v1.0/admin/settings", SettingsAsync, openApi => openApi
                .WithTag("Admin").WithDescription("Non-secret view of this node's configuration and protocol listeners.")
                .WithResponse(200, OpenApiResponseMetadata.Json("Settings", null)));

            server.Put<UpdateSettingsRequest>("/v1.0/admin/settings", UpdateSettingsAsync, openApi => openApi
                .WithTag("Admin").WithDescription("Persist a partial settings update to the node's settings file. Changes take effect after a restart.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json(null, "Settings update", true))
                .WithResponse(200, OpenApiResponseMetadata.Json("Updated settings", null)));

            server.Post("/v1.0/admin/restart", RestartAsync, openApi => openApi
                .WithTag("Admin").WithDescription("Exit the process so a container restart policy brings it back up on the current settings file. No effect when not run under such a policy.")
                .WithResponse(202, OpenApiResponseMetadata.Create("Restart scheduled")));

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

        private Task<object> SettingsAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, () =>
            {
                return Task.FromResult<object>(ServerSettingsResponse.FromSettings(_Settings, _NodeId));
            });
        }

        private Task<object> UpdateSettingsAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, () =>
            {
                UpdateSettingsRequest body = request.GetData<UpdateSettingsRequest>() ?? new UpdateSettingsRequest();

                // Apply onto the in-memory settings and persist to the file the next startup reads.
                // Mutating the running instance keeps the response consistent with what was requested;
                // the services already captured their own values, so nothing hot-applies -- a restart
                // is what makes these live.
                body.ApplyTo(_Settings);

                string path = SettingsManager.ResolveSettingsPath();
                SettingsManager.Save(_Settings, path);
                _Logging.Info(_Header + "settings updated and persisted to " + path + " (restart required to apply)");

                return Task.FromResult<object>(ServerSettingsResponse.FromSettings(_Settings, _NodeId));
            });
        }

        private Task<object> RestartAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, () =>
            {
                request.Http.Response.StatusCode = 202;

                // Exit shortly after the response flushes. A container restart policy (unless-stopped
                // or always) restarts on the exit and the fresh process reads the updated settings
                // file. Run without such a policy, this simply stops the node -- documented on the
                // dashboard's Restart button.
                _Logging.Info(_Header + "restart requested; exiting so the container restart policy applies updated settings");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(750).ConfigureAwait(false);
                    Environment.Exit(0);
                });

                return Task.FromResult<object>(new { Restarting = true });
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
