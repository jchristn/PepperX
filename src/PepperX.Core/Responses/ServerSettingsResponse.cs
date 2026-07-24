namespace PepperX.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using PepperX.Core.Settings;

    /// <summary>
    /// Non-secret view of a node's configuration, for administrative dashboards.
    /// Credentials are deliberately absent: the settings file holds a database password and S3 static
    /// keys, and neither is required to answer "how is this node configured".
    /// </summary>
    public class ServerSettingsResponse
    {
        #region Public-Members

        /// <summary>
        /// Protocol listeners and their configuration. Never null.
        /// </summary>
        public List<ProtocolEndpoint> Protocols
        {
            get
            {
                return _Protocols;
            }
            set
            {
                _Protocols = value ?? new List<ProtocolEndpoint>();
            }
        }

        /// <summary>
        /// Storage driver in use.
        /// </summary>
        public string StorageDriver { get; set; } = String.Empty;

        /// <summary>
        /// Root directory for disk storage, when the disk driver is in use.
        /// </summary>
        public string? StorageDirectory { get; set; } = null;

        /// <summary>
        /// Whether checksums are verified on every read.
        /// </summary>
        public bool VerifyChecksumOnRead { get; set; } = false;

        /// <summary>
        /// Database engine in use.
        /// </summary>
        public string DatabaseType { get; set; } = String.Empty;

        /// <summary>
        /// Database hostname.
        /// </summary>
        public string DatabaseHostname { get; set; } = String.Empty;

        /// <summary>
        /// Database port.
        /// </summary>
        public int DatabasePort { get; set; } = 0;

        /// <summary>
        /// Database name.
        /// </summary>
        public string DatabaseName { get; set; } = String.Empty;

        /// <summary>
        /// Identifier this node registers under in the cluster.
        /// </summary>
        public string NodeId { get; set; } = String.Empty;

        /// <summary>
        /// How deletes coordinate with in-flight reads across nodes.
        /// </summary>
        public string DeleteCoordinationMode { get; set; } = String.Empty;

        /// <summary>
        /// Whether request history is being captured.
        /// </summary>
        public bool RequestHistoryEnabled { get; set; } = true;

        /// <summary>
        /// Days of request history retained.
        /// </summary>
        public int RequestHistoryRetentionDays { get; set; } = 0;

        #endregion

        #region Private-Members

        private List<ProtocolEndpoint> _Protocols = new List<ProtocolEndpoint>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty settings response.
        /// </summary>
        public ServerSettingsResponse()
        {
        }

        /// <summary>
        /// Build a redacted response from a node's settings.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="nodeId">Resolved node identifier.</param>
        /// <returns>Settings response.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        public static ServerSettingsResponse FromSettings(PepperXSettings settings, string nodeId)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            ServerSettingsResponse ret = new ServerSettingsResponse
            {
                StorageDriver = settings.Storage.Driver.ToString(),
                StorageDirectory = settings.Storage.Disk.RootDirectory,
                VerifyChecksumOnRead = settings.Storage.VerifyChecksumOnRead,
                DatabaseType = settings.Database.Type.ToString(),
                DatabaseHostname = settings.Database.Hostname,
                DatabasePort = settings.Database.Port,
                DatabaseName = settings.Database.DatabaseName,
                NodeId = nodeId ?? String.Empty,
                DeleteCoordinationMode = settings.Cluster.DeleteCoordinationMode.ToString(),
                RequestHistoryEnabled = settings.RequestHistory.Enabled,
                RequestHistoryRetentionDays = settings.RequestHistory.RetentionDays
            };

            ret.Protocols.Add(new ProtocolEndpoint("REST", settings.Rest.Enabled, settings.Rest.Hostname, settings.Rest.Port, settings.Rest.Ssl ? "https" : "http"));
            ret.Protocols.Add(new ProtocolEndpoint("S3", settings.S3.Enabled, settings.S3.Hostname, settings.S3.Port, settings.S3.Ssl ? "https" : "http"));
            ret.Protocols.Add(new ProtocolEndpoint("RESP", settings.Resp.Enabled, settings.Rest.Hostname, settings.Resp.Port, "redis"));
            ret.Protocols.Add(new ProtocolEndpoint("WebSockets", settings.Websocket.Enabled, settings.Websocket.Hostname, settings.Websocket.Port, "ws"));
            ret.Protocols.Add(new ProtocolEndpoint("MCP", settings.Mcp.Enabled, settings.Mcp.Hostname, settings.Mcp.HttpPort, "http"));

            return ret;
        }

        #endregion
    }

    /// <summary>
    /// One protocol listener.
    /// </summary>
    public class ProtocolEndpoint
    {
        #region Public-Members

        /// <summary>
        /// Protocol name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Whether the listener is enabled on this node.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Hostname the listener is bound to.
        /// </summary>
        public string Hostname { get; set; } = String.Empty;

        /// <summary>
        /// Port the listener is bound to.
        /// </summary>
        public int Port { get; set; } = 0;

        /// <summary>
        /// URI scheme clients should use.
        /// </summary>
        public string Scheme { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty protocol endpoint.
        /// </summary>
        public ProtocolEndpoint()
        {
        }

        /// <summary>
        /// Instantiate a protocol endpoint.
        /// </summary>
        /// <param name="name">Protocol name.</param>
        /// <param name="enabled">Whether the listener is enabled.</param>
        /// <param name="hostname">Hostname.</param>
        /// <param name="port">Port.</param>
        /// <param name="scheme">URI scheme.</param>
        public ProtocolEndpoint(string name, bool enabled, string hostname, int port, string scheme)
        {
            Name = name ?? String.Empty;
            Enabled = enabled;
            Hostname = hostname ?? String.Empty;
            Port = port;
            Scheme = scheme ?? String.Empty;
        }

        #endregion
    }
}
