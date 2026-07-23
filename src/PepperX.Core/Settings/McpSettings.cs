namespace PepperX.Core.Settings
{
    using System;

    /// <summary>
    /// MCP listener configuration. MCP is served over Streamable HTTP and TCP JSON-RPC.
    /// </summary>
    public class McpSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the MCP listeners are enabled. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Streamable HTTP listen port. Clamped to the range 1 to 65535. Default 8003.
        /// </summary>
        public int HttpPort
        {
            get
            {
                return _HttpPort;
            }
            set
            {
                _HttpPort = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// TCP JSON-RPC listen port. Clamped to the range 1 to 65535. Default 8004.
        /// </summary>
        public int TcpPort
        {
            get
            {
                return _TcpPort;
            }
            set
            {
                _TcpPort = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Maximum inline payload size in bytes returned by object-read tools before the caller is directed to
        /// the REST surface. Clamped to the range 1 KiB to 64 MiB. Default 8 MiB.
        /// </summary>
        public int MaxInlineBytes
        {
            get
            {
                return _MaxInlineBytes;
            }
            set
            {
                _MaxInlineBytes = Math.Clamp(value, 1024, 67108864);
            }
        }

        #endregion

        #region Private-Members

        private int _HttpPort = 8003;
        private int _TcpPort = 8004;
        private int _MaxInlineBytes = 8388608;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate MCP settings.
        /// </summary>
        public McpSettings()
        {
        }

        #endregion
    }
}
