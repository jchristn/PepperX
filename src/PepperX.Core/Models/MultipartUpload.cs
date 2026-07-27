namespace PepperX.Core.Models
{
    using System;
    using System.Collections.Generic;
    using PepperX.Core.Helpers;

    /// <summary>
    /// An in-progress S3 multipart upload. The identifier (prefix <c>mpu_</c>) is the opaque S3 UploadId
    /// returned to the client at initiation. The upload is transient state: it becomes an object only when
    /// completed, and is reclaimed by the janitor once <see cref="ExpiresUtc"/> passes.
    /// </summary>
    public class MultipartUpload
    {
        #region Public-Members

        /// <summary>
        /// Upload identifier (prefix <c>mpu_</c>). This value is the S3 UploadId.
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
        /// Owning container identifier.
        /// </summary>
        public string ContainerId
        {
            get
            {
                return _ContainerId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ContainerId));
                _ContainerId = value;
            }
        }

        /// <summary>
        /// Target object key that the completed upload will create.
        /// </summary>
        public string Key
        {
            get
            {
                return _Key;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Key));
                _Key = value;
            }
        }

        /// <summary>
        /// Content type recorded at initiation and applied to the assembled object. May be null.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Tags recorded at initiation and applied to the assembled object. Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get
            {
                return _Tags;
            }
            set
            {
                _Tags = value ?? new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// UTC time the upload was initiated.
        /// </summary>
        public DateTime InitiatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time after which the upload is eligible for janitor reclamation.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateMultipartUploadId();
        private string _ContainerId = String.Empty;
        private string _Key = String.Empty;
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty multipart upload.
        /// </summary>
        public MultipartUpload()
        {
        }

        #endregion
    }
}
