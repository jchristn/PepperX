namespace PepperX.Core.Responses
{
    using System;

    /// <summary>
    /// A REST-facing summary of a single in-progress multipart upload. Excludes internal identifiers such
    /// as the container id.
    /// </summary>
    public class MultipartUploadInfo
    {
        #region Public-Members

        /// <summary>
        /// Upload identifier (the S3 UploadId).
        /// </summary>
        public string UploadId { get; set; } = String.Empty;

        /// <summary>
        /// Target object key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Content type recorded at initiation, or null.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// UTC time the upload was initiated.
        /// </summary>
        public DateTime InitiatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time after which the upload is eligible for reclamation.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty multipart upload summary.
        /// </summary>
        public MultipartUploadInfo()
        {
        }

        #endregion
    }
}
