namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Settings;
    using PepperX.Server;
    using SyslogLogging;

    /// <summary>
    /// Boots an in-process PepperX server over a fresh test database and a temporary storage root for
    /// REST integration tests, and provides an <see cref="HttpClient"/> bound to it.
    /// </summary>
    public sealed class RestTestServer : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>HTTP client bound to the running server.</summary>
        public HttpClient Client { get; }

        /// <summary>Base URL of the running server.</summary>
        public string BaseUrl { get; }

        #endregion

        #region Private-Members

        private readonly PepperXServer _Server;
        private readonly string _DatabaseName;
        private readonly string _StorageRoot;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        private RestTestServer(PepperXServer server, HttpClient client, string baseUrl, string databaseName, string storageRoot, LoggingModule logging)
        {
            _Server = server;
            Client = client;
            BaseUrl = baseUrl;
            _DatabaseName = databaseName;
            _StorageRoot = storageRoot;
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start a fresh in-process server.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A running server handle.</returns>
        public static async Task<RestTestServer> StartAsync(CancellationToken token = default)
        {
            string dbName = await PostgresTestFixture.CreateDatabaseAsync(token).ConfigureAwait(false);
            string root = StorageTestHelper.NewRoot();
            int port = FreePort();

            PepperXSettings settings = new PepperXSettings();
            settings.Database = TestEnvironment.SettingsFor(dbName);
            settings.Storage.Disk.RootDirectory = root;
            settings.Rest.Hostname = "localhost";
            settings.Rest.Port = port;
            settings.S3.Enabled = false;
            settings.Resp.Enabled = false;
            settings.Websocket.Enabled = false;
            settings.Mcp.Enabled = false;
            settings.Cluster.HeartbeatIntervalSeconds = 60;
            settings.Cluster.JanitorIntervalSeconds = 3600;

            string logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pepperx-test-" + Guid.NewGuid().ToString("N") + ".log");
            LoggingModule logging = new LoggingModule(logPath, FileLoggingMode.SingleLogFile, false);
            PepperXServer server = new PepperXServer(settings, logging);
            await server.StartAsync(token).ConfigureAwait(false);

            string baseUrl = "http://localhost:" + port;
            HttpClient client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };

            return new RestTestServer(server, client, baseUrl, dbName, root, logging);
        }

        /// <summary>
        /// Stop the server, drop its database, and remove its storage root.
        /// </summary>
        /// <returns>Value task.</returns>
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _Server.StopAsync().ConfigureAwait(false);
            await PostgresTestFixture.DropDatabaseAsync(_DatabaseName).ConfigureAwait(false);
            StorageTestHelper.Cleanup(_StorageRoot);
            _Logging.Dispose();
        }

        #endregion

        #region Private-Methods

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
