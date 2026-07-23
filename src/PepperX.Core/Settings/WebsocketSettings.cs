namespace PepperX.Core.Settings
{
    using System;

    /// <summary>
    /// WebSockets listener configuration.
    /// </summary>
    public class WebsocketSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the WebSockets listener is enabled. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Bind host name. Default "*" (all interfaces).
        /// </summary>
        public string Hostname
        {
            get
            {
                return _Hostname;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Hostname));
                _Hostname = value;
            }
        }

        /// <summary>
        /// Listen port. Clamped to the range 1 to 65535. Default 8002.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                _Port = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Maximum WebSocket message size in bytes. Clamped to the range 1 KiB to 1 GiB. Default 128 MiB.
        /// </summary>
        public long MaxMessageBytes
        {
            get
            {
                return _MaxMessageBytes;
            }
            set
            {
                _MaxMessageBytes = Math.Clamp(value, 1024L, 1073741824L);
            }
        }

        /// <summary>
        /// Keep-alive ping interval in seconds. Clamped to the range 5 to 300. Default 30.
        /// </summary>
        public int KeepAliveSeconds
        {
            get
            {
                return _KeepAliveSeconds;
            }
            set
            {
                _KeepAliveSeconds = Math.Clamp(value, 5, 300);
            }
        }

        #endregion

        #region Private-Members

        private string _Hostname = "*";
        private int _Port = 8002;
        private long _MaxMessageBytes = 134217728L;
        private int _KeepAliveSeconds = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate WebSockets settings.
        /// </summary>
        public WebsocketSettings()
        {
        }

        #endregion
    }
}
