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

        /// <summary>S3 service URL when S3 is enabled; otherwise null.</summary>
        public string? S3ServiceUrl { get; }

        /// <summary>RESP port when RESP is enabled; otherwise zero.</summary>
        public int RespPort { get; }

        /// <summary>WebSocket port when WebSockets are enabled; otherwise zero.</summary>
        public int WsPort { get; }

        /// <summary>MCP Streamable HTTP port when MCP is enabled; otherwise zero.</summary>
        public int McpHttpPort { get; }

        #endregion

        #region Private-Members

        private readonly PepperXServer _Server;
        private readonly string _DatabaseName;
        private readonly string _StorageRoot;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        private RestTestServer(PepperXServer server, HttpClient client, string baseUrl, string? s3ServiceUrl, int respPort, int wsPort, int mcpHttpPort, string databaseName, string storageRoot, LoggingModule logging)
        {
            _Server = server;
            Client = client;
            BaseUrl = baseUrl;
            S3ServiceUrl = s3ServiceUrl;
            RespPort = respPort;
            WsPort = wsPort;
            McpHttpPort = mcpHttpPort;
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
            return await StartAsync(false, false, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Start a fresh in-process server, optionally enabling the S3 and RESP listeners.
        /// </summary>
        /// <param name="enableS3">Whether to enable the S3 listener.</param>
        /// <param name="enableResp">Whether to enable the RESP listener.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A running server handle.</returns>
        public static async Task<RestTestServer> StartAsync(bool enableS3, bool enableResp, CancellationToken token = default)
        {
            return await StartAsync(enableS3, enableResp, false, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Start a fresh in-process server, optionally enabling the S3, RESP, and WebSocket listeners.
        /// </summary>
        /// <param name="enableS3">Whether to enable the S3 listener.</param>
        /// <param name="enableResp">Whether to enable the RESP listener.</param>
        /// <param name="enableWs">Whether to enable the WebSocket listener.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A running server handle.</returns>
        public static async Task<RestTestServer> StartAsync(bool enableS3, bool enableResp, bool enableWs, CancellationToken token = default)
        {
            return await StartAsync(enableS3, enableResp, enableWs, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Start a fresh in-process server, optionally enabling the S3, RESP, WebSocket, and MCP listeners.
        /// </summary>
        /// <param name="enableS3">Whether to enable the S3 listener.</param>
        /// <param name="enableResp">Whether to enable the RESP listener.</param>
        /// <param name="enableWs">Whether to enable the WebSocket listener.</param>
        /// <param name="enableMcp">Whether to enable the MCP listeners.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A running server handle.</returns>
        public static async Task<RestTestServer> StartAsync(bool enableS3, bool enableResp, bool enableWs, bool enableMcp, CancellationToken token = default)
        {
            string dbName = await PostgresTestFixture.CreateDatabaseAsync(token).ConfigureAwait(false);
            string root = StorageTestHelper.NewRoot();
            int port = FreePort();
            int s3Port = enableS3 ? FreePort() : 0;
            int respPort = enableResp ? FreePort() : 0;
            int wsPort = enableWs ? FreePort() : 0;
            int mcpHttpPort = enableMcp ? FreePort() : 0;
            int mcpTcpPort = enableMcp ? FreePort() : 0;

            PepperXSettings settings = new PepperXSettings();
            settings.Database = TestEnvironment.SettingsFor(dbName);
            settings.Storage.Disk.RootDirectory = root;
            settings.Rest.Hostname = "localhost";
            settings.Rest.Port = port;
            settings.S3.Enabled = enableS3;
            settings.S3.Hostname = "localhost";
            if (enableS3) settings.S3.Port = s3Port;
            settings.Resp.Enabled = enableResp;
            if (enableResp) settings.Resp.Port = respPort;
            settings.Websocket.Enabled = enableWs;
            settings.Websocket.Hostname = "localhost";
            if (enableWs) settings.Websocket.Port = wsPort;
            settings.Mcp.Enabled = enableMcp;
            if (enableMcp)
            {
                settings.Mcp.HttpPort = mcpHttpPort;
                settings.Mcp.TcpPort = mcpTcpPort;
            }
            settings.Cluster.HeartbeatIntervalSeconds = 60;
            settings.Cluster.JanitorIntervalSeconds = 3600;

            string logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pepperx-test-" + Guid.NewGuid().ToString("N") + ".log");
            LoggingModule logging = new LoggingModule(logPath, FileLoggingMode.SingleLogFile, false);
            PepperXServer server = new PepperXServer(settings, logging);
            await server.StartAsync(token).ConfigureAwait(false);

            string baseUrl = "http://localhost:" + port;
            HttpClient client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
            string? s3Url = enableS3 ? "http://localhost:" + s3Port : null;

            return new RestTestServer(server, client, baseUrl, s3Url, respPort, wsPort, mcpHttpPort, dbName, root, logging);
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
