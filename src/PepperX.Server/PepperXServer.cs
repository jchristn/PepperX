namespace PepperX.Server
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core;
    using PepperX.Core.Database;
    using PepperX.Core.Helpers;
    using PepperX.Core.Services;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage.Disk;
    using PepperX.Server.Api.Rest;
    using PepperX.Server.Api.S3;
    using PepperX.Server.Serialization;
    using PepperX.Server.Services;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Server host for PepperX. Owns the database and storage drivers, the service layer, the background
    /// maintenance services, and the protocol listeners (REST in this phase; additional protocol surfaces are
    /// added in later phases). Composition and lifecycle live here; the entry point stays thin.
    /// </summary>
    public sealed class PepperXServer
    {
        #region Private-Members

        private readonly string _Header = "[PepperXServer] ";
        private readonly PepperXSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly string _NodeId;
        private readonly DateTime _StartUtc = DateTime.UtcNow;

        private IMetadataDatabaseDriver? _Db;
        private DiskExtentStorageDriver? _Storage;
        private LocalLockRegistry? _LocalLocks;
        private NodeHeartbeatService? _Heartbeat;
        private JanitorService? _Janitor;
        private RequestHistoryCaptureService? _Capture;
        private Webserver? _RestServer;
        private S3ProtocolHandler? _S3Handler;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the server host.
        /// </summary>
        /// <param name="settings">Application settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public PepperXServer(PepperXSettings settings, LoggingModule logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _NodeId = String.IsNullOrEmpty(settings.Cluster.NodeId) ? IdGenerator.GenerateNodeId() : settings.Cluster.NodeId;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize dependencies and start the enabled protocol listeners.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StartAsync(CancellationToken token = default)
        {
            _Db = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(_Settings.Database, _Logging, token).ConfigureAwait(false);

            _Storage = new DiskExtentStorageDriver(_Settings.Storage.Disk);
            await _Storage.InitializeAsync(token).ConfigureAwait(false);

            _LocalLocks = new LocalLockRegistry();
            ObjectDeleteService deletes = new ObjectDeleteService(_Db, _Storage, _Settings, _LocalLocks, _Logging);
            ObjectReadService reads = new ObjectReadService(_Db, _Storage, _Settings, _LocalLocks, _NodeId, _Logging);
            ObjectWriteService writes = new ObjectWriteService(_Db, _Storage, _Settings, reads, deletes);
            ContainerService containers = new ContainerService(_Db, _Storage, deletes);
            SearchService search = new SearchService(_Db);
            StatisticsService statistics = new StatisticsService(_Db, _Storage, _Settings);
            RehydrationService rehydration = new RehydrationService(_Db, _Storage, _Logging);

            _Heartbeat = new NodeHeartbeatService(_Db, _Settings, _NodeId, Environment.MachineName, _Logging);
            await _Heartbeat.StartAsync(token).ConfigureAwait(false);

            _Janitor = new JanitorService(_Db, _Storage, deletes, _Settings, _Logging);
            _Janitor.Start();

            _Capture = new RequestHistoryCaptureService(_Db, _Settings.RequestHistory, _Logging);

            if (_Settings.Rest.Enabled) StartRest(containers, writes, reads, deletes, search, statistics, rehydration);

            if (_Settings.S3.Enabled)
            {
                _S3Handler = new S3ProtocolHandler(containers, writes, reads, deletes, search, _Db, _Settings.S3, _Logging);
                _S3Handler.Start();
                _Logging.Info(_Header + "S3 listener started on port " + _Settings.S3.Port);
            }

            _Logging.Info(_Header + Constants.ProductName + " v" + Constants.ProductVersion + " started (node " + _NodeId + ")");
        }

        /// <summary>
        /// Start the host and block until the token is cancelled, then shut down gracefully.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task RunAsync(CancellationToken token)
        {
            await StartAsync(token).ConfigureAwait(false);

            try
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }

            await StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Stop the listeners and dispose dependencies.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task StopAsync()
        {
            _Logging.Info(_Header + "shutting down");

            try { _RestServer?.Stop(); } catch (Exception) { }
            _RestServer?.Dispose();

            _S3Handler?.Stop();

            _Janitor?.Dispose();
            _Heartbeat?.Dispose();
            _LocalLocks?.Dispose();

            if (_Db != null) await _Db.DisposeAsync().ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private void StartRest(ContainerService containers, ObjectWriteService writes, ObjectReadService reads, ObjectDeleteService deletes, SearchService search, StatisticsService statistics, RehydrationService rehydration)
        {
            WebserverSettings webserverSettings = new WebserverSettings(_Settings.Rest.Hostname, _Settings.Rest.Port, _Settings.Rest.Ssl);
            _RestServer = new Webserver(webserverSettings, DefaultRouteAsync);
            _RestServer.Serializer = new PepperXWatsonSerializer();

            _RestServer.Routes.Preflight = PreflightAsync;
            _RestServer.Routes.PreRouting = PreRoutingAsync;
            _RestServer.Routes.PostRouting = PostRoutingAsync;

            _RestServer.UseOpenApi(api =>
            {
                api.Info.Title = Constants.ProductName + " REST API";
                api.Info.Version = Constants.ProductVersion;
                api.Info.Description = "High-performance key-value store with rich metadata, immutable extents, and dual persistence. This backend is unauthenticated by design.";
                api.Info.License = new OpenApiLicense { Name = "MIT" };
                api.EnableSwaggerUi = true;
                api.Tags.Add(new OpenApiTag { Name = "Health", Description = "Health and liveness" });
                api.Tags.Add(new OpenApiTag { Name = "Containers", Description = "Container management" });
                api.Tags.Add(new OpenApiTag { Name = "Objects", Description = "Object read, write, metadata, and listing" });
                api.Tags.Add(new OpenApiTag { Name = "Search", Description = "Cross-container search" });
                api.Tags.Add(new OpenApiTag { Name = "Admin", Description = "Statistics, nodes, and rehydration" });
                api.Tags.Add(new OpenApiTag { Name = "Request History", Description = "Captured request observability" });
            });

            new HealthRoutes(_StartUtc).Register(_RestServer);
            new ContainerRoutes(containers).Register(_RestServer);
            new ObjectRoutes(writes, reads, deletes, search).Register(_RestServer);
            new SearchRoutes(search).Register(_RestServer);
            new AdminRoutes(statistics, rehydration).Register(_RestServer);
            new RequestHistoryRoutes(_Db!, _Settings.RequestHistory).Register(_RestServer);

            _RestServer.Start();
        }

        private static async Task PreflightAsync(HttpContextBase context)
        {
            context.Response.StatusCode = 200;
            AddCors(context);
            context.Response.Headers.Add("Access-Control-Max-Age", "86400");
            await context.Response.Send().ConfigureAwait(false);
        }

        private static async Task PreRoutingAsync(HttpContextBase context)
        {
            context.Timestamp.Start = DateTime.UtcNow;
            context.Response.ContentType = Constants.JsonContentType;
            AddCors(context);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private async Task PostRoutingAsync(HttpContextBase context)
        {
            context.Timestamp.End = DateTime.UtcNow;

            _Logging.Debug(
                _Header +
                context.Request.Method + " " +
                context.Request.Url.RawWithQuery + " " +
                context.Response.StatusCode + " (" +
                (context.Timestamp.TotalMs.HasValue ? context.Timestamp.TotalMs.Value.ToString("F2") : "?") + "ms)");

            if (_Settings.RequestHistory.Enabled && context.Request.Method != WatsonWebserver.Core.HttpMethod.OPTIONS)
            {
                _Capture?.Capture(context);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static async Task DefaultRouteAsync(HttpContextBase context)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = Constants.JsonContentType;
            await context.Response.Send("{\"Error\":\"NotFound\",\"Message\":\"No matching route.\",\"StatusCode\":404}").ConfigureAwait(false);
        }

        private static void AddCors(HttpContextBase context)
        {
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, HEAD");
            context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Range, X-Api-Key, x-pepperx-labels, x-pepperx-tags, x-pepperx-object");
        }

        #endregion
    }
}
