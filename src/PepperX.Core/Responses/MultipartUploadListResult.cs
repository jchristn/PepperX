namespace PepperX.Core.Responses
{
    using System.Collections.Generic;
    using PepperX.Core.Models;

    /// <summary>
    /// A page of in-progress multipart uploads for a container, with the pagination state needed to
    /// populate an S3 ListMultipartUploads response.
    /// </summary>
    public class MultipartUploadListResult
    {
        #region Public-Members

        /// <summary>
        /// The uploads in this page, ordered by key then upload id. Never null.
        /// </summary>
        public List<MultipartUpload> Uploads
        {
            get
            {
                return _Uploads;
            }
            set
            {
                _Uploads = value ?? new List<MultipartUpload>();
            }
        }

        /// <summary>
        /// Whether more uploads exist beyond this page.
        /// </summary>
        public bool IsTruncated { get; set; } = false;

        /// <summary>
        /// The object key to pass as the key marker to fetch the next page. Null when not truncated.
        /// </summary>
        public string? NextKeyMarker { get; set; } = null;

        /// <summary>
        /// The upload id to pass as the upload-id marker to fetch the next page. Null when not truncated.
        /// </summary>
        public string? NextUploadIdMarker { get; set; } = null;

        #endregion

        #region Private-Members

        private List<MultipartUpload> _Uploads = new List<MultipartUpload>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty upload list result.
        /// </summary>
        public MultipartUploadListResult()
        {
        }

        #endregion
    }
}
