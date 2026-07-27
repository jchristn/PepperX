namespace PepperX.Core.Exceptions
{
    using PepperX.Core.Enums;

    /// <summary>
    /// Thrown when a referenced multipart upload id does not exist (never initiated, already completed, or
    /// aborted/expired). Maps to HTTP 404 and the S3 code NoSuchUpload.
    /// </summary>
    public class NoSuchUploadException : PepperXException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception for an upload id.
        /// </summary>
        /// <param name="uploadId">Upload id that was not found.</param>
        public NoSuchUploadException(string uploadId)
            : base(ApiErrorEnum.NotFound, 404, "Multipart upload '" + uploadId + "' does not exist.")
        {
        }

        #endregion
    }
}
