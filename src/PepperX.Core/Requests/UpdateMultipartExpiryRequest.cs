namespace PepperX.Core.Requests
{
    /// <summary>
    /// A request to set or clear a container's per-container multipart-upload expiry. A null
    /// <see cref="Days"/> clears the override so the container inherits the system-wide
    /// <c>S3.MultipartUploadExpiryDays</c>; a value of 1..365 sets the container's own window.
    /// </summary>
    public class UpdateMultipartExpiryRequest
    {
        #region Public-Members

        /// <summary>
        /// The expiry, in days, to apply to in-progress multipart uploads started in this container, or
        /// null to clear the override and inherit the system-wide default. Values are clamped to 1..365.
        /// </summary>
        public int? Days { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a default request.
        /// </summary>
        public UpdateMultipartExpiryRequest()
        {
        }

        #endregion
    }
}
