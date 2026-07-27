namespace PepperX.Core.Responses
{
    using System;

    /// <summary>
    /// Result of initiating a multipart upload over the REST API.
    /// </summary>
    public class MultipartInitiateResponse
    {
        #region Public-Members

        /// <summary>
        /// Upload identifier to use for subsequent part uploads, listing, completion, and abort.
        /// </summary>
        public string UploadId { get; set; } = String.Empty;

        /// <summary>
        /// Target object key the completed upload will create.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an initiate response.
        /// </summary>
        public MultipartInitiateResponse()
        {
        }

        #endregion
    }
}
