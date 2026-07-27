namespace PepperX.Core.Responses
{
    using System;

    /// <summary>
    /// Result of uploading (or copying) a single multipart part over the REST API.
    /// </summary>
    public class MultipartPartResponse
    {
        #region Public-Members

        /// <summary>
        /// Part number that was staged.
        /// </summary>
        public int PartNumber { get; set; } = 0;

        /// <summary>
        /// Part ETag (the part's MD5). Supply this value in the complete request for this part number.
        /// </summary>
        public string ETag { get; set; } = String.Empty;

        /// <summary>
        /// Staged part size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a part response.
        /// </summary>
        public MultipartPartResponse()
        {
        }

        #endregion
    }
}
