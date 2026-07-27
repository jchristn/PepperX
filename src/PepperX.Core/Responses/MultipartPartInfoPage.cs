namespace PepperX.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// A REST-facing page of staged parts for a multipart upload, with the pagination marker.
    /// </summary>
    public class MultipartPartInfoPage
    {
        #region Public-Members

        /// <summary>
        /// The parts in this page, ascending by part number. Never null.
        /// </summary>
        public List<MultipartPartInfo> Parts
        {
            get
            {
                return _Parts;
            }
            set
            {
                _Parts = value ?? new List<MultipartPartInfo>();
            }
        }

        /// <summary>
        /// Whether more parts exist beyond this page.
        /// </summary>
        public bool IsTruncated { get; set; } = false;

        /// <summary>
        /// The part number to pass as the marker to fetch the next page. Null when not truncated.
        /// </summary>
        public int? NextPartNumberMarker { get; set; } = null;

        #endregion

        #region Private-Members

        private List<MultipartPartInfo> _Parts = new List<MultipartPartInfo>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty part page.
        /// </summary>
        public MultipartPartInfoPage()
        {
        }

        #endregion
    }
}
