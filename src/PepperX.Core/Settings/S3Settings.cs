namespace PepperX.Core.Settings
{
    using System;

    /// <summary>
    /// S3 listener configuration.
    /// </summary>
    public class S3Settings
    {
        #region Public-Members

        /// <summary>
        /// Whether the S3 listener is enabled. Default true.
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
        /// Listen port. Clamped to the range 1 to 65535. Default 8001.
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
        /// Region reported for GetBucketLocation. Default "us-west-1".
        /// </summary>
        public string Region
        {
            get
            {
                return _Region;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Region));
                _Region = value;
            }
        }

        /// <summary>
        /// Whether to allow unsigned anonymous requests. Default true.
        /// </summary>
        public bool AllowAnonymous { get; set; } = true;

        /// <summary>
        /// Static access key accepted from signed clients. Default "pepperx".
        /// </summary>
        public string StaticAccessKey
        {
            get
            {
                return _StaticAccessKey;
            }
            set
            {
                _StaticAccessKey = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Static secret key used to validate signed requests. Default "pepperx".
        /// </summary>
        public string StaticSecretKey
        {
            get
            {
                return _StaticSecretKey;
            }
            set
            {
                _StaticSecretKey = value ?? String.Empty;
            }
        }

        #endregion

        #region Private-Members

        private string _Hostname = "*";
        private int _Port = 8001;
        private string _Region = "us-west-1";
        private string _StaticAccessKey = "pepperx";
        private string _StaticSecretKey = "pepperx";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate S3 settings.
        /// </summary>
        public S3Settings()
        {
        }

        #endregion
    }
}
