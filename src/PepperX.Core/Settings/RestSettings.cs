namespace PepperX.Core.Settings
{
    using System;

    /// <summary>
    /// Native REST listener configuration.
    /// </summary>
    public class RestSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the REST listener is enabled. Default true.
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
        /// Listen port. Clamped to the range 1 to 65535. Default 8000.
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
        /// Whether to serve over TLS. Default false.
        /// </summary>
        public bool Ssl { get; set; } = false;

        /// <summary>
        /// Maximum size in bytes of a metadata object echoed as a response header before it is offered via a
        /// separate metadata request instead. Clamped to the range 0 to 1 MiB. Default 16384.
        /// </summary>
        public int MetadataObjectHeaderLimitBytes
        {
            get
            {
                return _MetadataObjectHeaderLimitBytes;
            }
            set
            {
                _MetadataObjectHeaderLimitBytes = Math.Clamp(value, 0, 1048576);
            }
        }

        #endregion

        #region Private-Members

        private string _Hostname = "*";
        private int _Port = 8000;
        private int _MetadataObjectHeaderLimitBytes = 16384;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate REST settings.
        /// </summary>
        public RestSettings()
        {
        }

        #endregion
    }
}
