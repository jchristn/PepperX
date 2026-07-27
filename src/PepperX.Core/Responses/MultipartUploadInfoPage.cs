namespace PepperX.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// A REST-facing page of in-progress multipart uploads for a container, with pagination markers.
    /// </summary>
    public class MultipartUploadInfoPage
    {
        #region Public-Members

        /// <summary>
        /// The uploads in this page. Never null.
        /// </summary>
        public List<MultipartUploadInfo> Uploads
        {
            get
            {
                return _Uploads;
            }
            set
            {
                _Uploads = value ?? new List<MultipartUploadInfo>();
            }
        }

        /// <summary>
        /// Whether more uploads exist beyond this page.
        /// </summary>
        public bool IsTruncated { get; set; } = false;

        /// <summary>
        /// Key marker to pass to fetch the next page. Null when not truncated.
        /// </summary>
        public string? NextKeyMarker { get; set; } = null;

        /// <summary>
        /// Upload-id marker to pass to fetch the next page. Null when not truncated.
        /// </summary>
        public string? NextUploadIdMarker { get; set; } = null;

        #endregion

        #region Private-Members

        private List<MultipartUploadInfo> _Uploads = new List<MultipartUploadInfo>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty page.
        /// </summary>
        public MultipartUploadInfoPage()
        {
        }

        #endregion
    }
}
