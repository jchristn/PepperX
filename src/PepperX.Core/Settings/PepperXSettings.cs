namespace PepperX.Core.Settings
{
    using System;

    /// <summary>
    /// Root application settings aggregating every subsystem. All sub-settings are non-null.
    /// </summary>
    public class PepperXSettings
    {
        #region Public-Members

        /// <summary>
        /// UTC time the settings file was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Logging settings. Never null.
        /// </summary>
        public LoggingSettings Logging
        {
            get
            {
                return _Logging;
            }
            set
            {
                _Logging = value ?? new LoggingSettings();
            }
        }

        /// <summary>
        /// Database settings. Never null.
        /// </summary>
        public DatabaseSettings Database
        {
            get
            {
                return _Database;
            }
            set
            {
                _Database = value ?? new DatabaseSettings();
            }
        }

        /// <summary>
        /// Storage settings. Never null.
        /// </summary>
        public StorageSettings Storage
        {
            get
            {
                return _Storage;
            }
            set
            {
                _Storage = value ?? new StorageSettings();
            }
        }

        /// <summary>
        /// Cluster settings. Never null.
        /// </summary>
        public ClusterSettings Cluster
        {
            get
            {
                return _Cluster;
            }
            set
            {
                _Cluster = value ?? new ClusterSettings();
            }
        }

        /// <summary>
        /// REST listener settings. Never null.
        /// </summary>
        public RestSettings Rest
        {
            get
            {
                return _Rest;
            }
            set
            {
                _Rest = value ?? new RestSettings();
            }
        }

        /// <summary>
        /// S3 listener settings. Never null.
        /// </summary>
        public S3Settings S3
        {
            get
            {
                return _S3;
            }
            set
            {
                _S3 = value ?? new S3Settings();
            }
        }

        /// <summary>
        /// RESP listener settings. Never null.
        /// </summary>
        public RespSettings Resp
        {
            get
            {
                return _Resp;
            }
            set
            {
                _Resp = value ?? new RespSettings();
            }
        }

        /// <summary>
        /// WebSockets listener settings. Never null.
        /// </summary>
        public WebsocketSettings Websocket
        {
            get
            {
                return _Websocket;
            }
            set
            {
                _Websocket = value ?? new WebsocketSettings();
            }
        }

        /// <summary>
        /// MCP listener settings. Never null.
        /// </summary>
        public McpSettings Mcp
        {
            get
            {
                return _Mcp;
            }
            set
            {
                _Mcp = value ?? new McpSettings();
            }
        }

        /// <summary>
        /// Request history settings. Never null.
        /// </summary>
        public RequestHistorySettings RequestHistory
        {
            get
            {
                return _RequestHistory;
            }
            set
            {
                _RequestHistory = value ?? new RequestHistorySettings();
            }
        }

        /// <summary>
        /// Telemetry (OpenTelemetry metrics, traces, logs) settings. Never null.
        /// </summary>
        public TelemetrySettings Telemetry
        {
            get
            {
                return _Telemetry;
            }
            set
            {
                _Telemetry = value ?? new TelemetrySettings();
            }
        }

        #endregion

        #region Private-Members

        private LoggingSettings _Logging = new LoggingSettings();
        private DatabaseSettings _Database = new DatabaseSettings();
        private StorageSettings _Storage = new StorageSettings();
        private ClusterSettings _Cluster = new ClusterSettings();
        private RestSettings _Rest = new RestSettings();
        private S3Settings _S3 = new S3Settings();
        private RespSettings _Resp = new RespSettings();
        private WebsocketSettings _Websocket = new WebsocketSettings();
        private McpSettings _Mcp = new McpSettings();
        private RequestHistorySettings _RequestHistory = new RequestHistorySettings();
        private TelemetrySettings _Telemetry = new TelemetrySettings();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate default settings.
        /// </summary>
        public PepperXSettings()
        {
        }

        #endregion
    }
}
