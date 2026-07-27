namespace PepperX.Core.Models
{
    using System;
    using PepperX.Core.Helpers;

    /// <summary>
    /// A single staged part of an in-progress multipart upload. Each part records both its content MD5
    /// (returned to the client as the part ETag and used to compute the multipart ETag at completion) and
    /// its content SHA-256 (PepperX's native content hash). The staged payload lives on shared storage at
    /// <see cref="StorageLocation"/> until the upload is completed or aborted.
    /// </summary>
    public class MultipartPart
    {
        #region Public-Members

        /// <summary>
        /// Part record identifier (prefix <c>mpp_</c>).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Owning upload identifier.
        /// </summary>
        public string UploadId
        {
            get
            {
                return _UploadId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(UploadId));
                _UploadId = value;
            }
        }

        /// <summary>
        /// Part number. Clamped to the range 1 to 10000 (S3's part-number range).
        /// </summary>
        public int PartNumber
        {
            get
            {
                return _PartNumber;
            }
            set
            {
                _PartNumber = Math.Clamp(value, 1, 10000);
            }
        }

        /// <summary>
        /// Staged part size in bytes; zero or greater.
        /// </summary>
        public long SizeBytes
        {
            get
            {
                return _SizeBytes;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(SizeBytes));
                _SizeBytes = value;
            }
        }

        /// <summary>
        /// Lowercase hex MD5 of the part (32 hex characters). Returned to the client as the part ETag.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not 32 hexadecimal characters.</exception>
        public string Md5
        {
            get
            {
                return _Md5;
            }
            set
            {
                _Md5 = NormalizeHex(value, 32, nameof(Md5));
            }
        }

        /// <summary>
        /// Lowercase hex SHA-256 of the part (64 hex characters).
        /// </summary>
        /// <exception cref="ArgumentException">The value is not 64 hexadecimal characters.</exception>
        public string Sha256
        {
            get
            {
                return _Sha256;
            }
            set
            {
                _Sha256 = NormalizeHex(value, 64, nameof(Sha256));
            }
        }

        /// <summary>
        /// Driver-relative location of the staged part payload on shared storage.
        /// </summary>
        public string StorageLocation
        {
            get
            {
                return _StorageLocation;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(StorageLocation));
                _StorageLocation = value;
            }
        }

        /// <summary>
        /// UTC time the part was staged.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateMultipartPartId();
        private string _UploadId = String.Empty;
        private int _PartNumber = 1;
        private long _SizeBytes = 0;
        private string _Md5 = String.Empty;
        private string _Sha256 = String.Empty;
        private string _StorageLocation = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty multipart part.
        /// </summary>
        public MultipartPart()
        {
        }

        #endregion

        #region Private-Methods

        private static string NormalizeHex(string value, int length, string field)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException(field + " must be a " + length + "-character hex string.", field);
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized.Length != length) throw new ArgumentException(field + " must be exactly " + length + " hex characters.", field);
            foreach (char c in normalized)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!isHex) throw new ArgumentException(field + " must contain only hexadecimal characters.", field);
            }
            return normalized;
        }

        #endregion
    }
}
