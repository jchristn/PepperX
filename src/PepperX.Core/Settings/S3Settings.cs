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

        /// <summary>
        /// Whether S3 multipart upload operations are enabled. When false the multipart callbacks are not
        /// wired and the library returns NotImplemented. Default true.
        /// </summary>
        public bool MultipartEnabled { get; set; } = true;

        /// <summary>
        /// Number of days an initiated-but-uncompleted multipart upload is retained before the janitor
        /// reclaims its staged parts. Clamped to the range 1 to 365. Default 7.
        /// </summary>
        public int MultipartUploadExpiryDays
        {
            get
            {
                return _MultipartUploadExpiryDays;
            }
            set
            {
                _MultipartUploadExpiryDays = Math.Clamp(value, 1, 365);
            }
        }

        /// <summary>
        /// Minimum size in bytes of every part except the last one, enforced at completion (S3's 5 MiB
        /// rule). A value of 0 disables the check. Clamped to the range 0 to 5368709120 (5 GiB). Default
        /// 5242880 (5 MiB).
        /// </summary>
        public long MultipartMinPartBytes
        {
            get
            {
                return _MultipartMinPartBytes;
            }
            set
            {
                _MultipartMinPartBytes = Math.Clamp(value, 0L, 5L * 1024 * 1024 * 1024);
            }
        }

        /// <summary>
        /// Maximum number of parts permitted in a single multipart upload. Clamped to the range 1 to
        /// 10000 (S3's limit). Default 10000.
        /// </summary>
        public int MultipartMaxParts
        {
            get
            {
                return _MultipartMaxParts;
            }
            set
            {
                _MultipartMaxParts = Math.Clamp(value, 1, 10000);
            }
        }

        #endregion

        #region Private-Members

        private string _Hostname = "*";
        private int _Port = 8001;
        private string _Region = "us-west-1";
        private string _StaticAccessKey = "pepperx";
        private string _StaticSecretKey = "pepperx";
        private int _MultipartUploadExpiryDays = 7;
        private long _MultipartMinPartBytes = 5 * 1024 * 1024;
        private int _MultipartMaxParts = 10000;

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
