namespace PepperX.Core.Settings
{
    using System;

    /// <summary>
    /// Redis RESP listener configuration.
    /// </summary>
    public class RespSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the RESP listener is enabled. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Listen port. Clamped to the range 1 to 65535. Default 6379.
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
        /// Number of RESP database indices, each mapping to a container. Clamped to the range 1 to 256.
        /// Default 16.
        /// </summary>
        public int DatabaseCount
        {
            get
            {
                return _DatabaseCount;
            }
            set
            {
                _DatabaseCount = Math.Clamp(value, 1, 256);
            }
        }

        /// <summary>
        /// Container name prefix for RESP databases (e.g. "resp" yields "resp0".."respN"). Must be a valid
        /// container name prefix. Default "resp".
        /// </summary>
        public string ContainerPrefix
        {
            get
            {
                return _ContainerPrefix;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ContainerPrefix));
                _ContainerPrefix = value;
            }
        }

        /// <summary>
        /// Maximum inline value size in bytes accepted over RESP. Clamped to the range 1 KiB to 512 MiB.
        /// Default 64 MiB.
        /// </summary>
        public long MaxValueBytes
        {
            get
            {
                return _MaxValueBytes;
            }
            set
            {
                _MaxValueBytes = Math.Clamp(value, 1024L, 536870912L);
            }
        }

        /// <summary>
        /// Retry count for INCR-family compare-and-swap loops. Clamped to the range 1 to 100. Default 8.
        /// </summary>
        public int CasRetryCount
        {
            get
            {
                return _CasRetryCount;
            }
            set
            {
                _CasRetryCount = Math.Clamp(value, 1, 100);
            }
        }

        #endregion

        #region Private-Members

        private int _Port = 6379;
        private int _DatabaseCount = 16;
        private string _ContainerPrefix = "resp";
        private long _MaxValueBytes = 67108864L;
        private int _CasRetryCount = 8;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate RESP settings.
        /// </summary>
        public RespSettings()
        {
        }

        #endregion
    }
}
