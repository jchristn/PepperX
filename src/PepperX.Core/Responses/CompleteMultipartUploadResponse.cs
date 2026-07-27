namespace PepperX.Core.Responses
{
    using System;

    /// <summary>
    /// Result of completing a multipart upload: the assembled object's identity and its S3 multipart ETag.
    /// </summary>
    public class CompleteMultipartUploadResponse
    {
        #region Public-Members

        /// <summary>
        /// Owning container name.
        /// </summary>
        public string ContainerName { get; set; } = String.Empty;

        /// <summary>
        /// Assembled object key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// The S3 multipart ETag, of the form <c>hex(md5-of-part-md5s)-N</c> (without surrounding quotes).
        /// </summary>
        public string ETag { get; set; } = String.Empty;

        /// <summary>
        /// Identifier of the extent that now backs the assembled object.
        /// </summary>
        public string ExtentId { get; set; } = String.Empty;

        /// <summary>
        /// Assembled object size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a complete-multipart-upload response.
        /// </summary>
        public CompleteMultipartUploadResponse()
        {
        }

        #endregion
    }
}
