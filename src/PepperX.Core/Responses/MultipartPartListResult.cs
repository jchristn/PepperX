namespace PepperX.Core.Responses
{
    using System.Collections.Generic;
    using PepperX.Core.Models;

    /// <summary>
    /// A page of staged parts for a multipart upload, with the pagination state needed to populate an S3
    /// ListParts response.
    /// </summary>
    public class MultipartPartListResult
    {
        #region Public-Members

        /// <summary>
        /// The parts in this page, ascending by part number. Never null.
        /// </summary>
        public List<MultipartPart> Parts
        {
            get
            {
                return _Parts;
            }
            set
            {
                _Parts = value ?? new List<MultipartPart>();
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

        private List<MultipartPart> _Parts = new List<MultipartPart>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty part list result.
        /// </summary>
        public MultipartPartListResult()
        {
        }

        #endregion
    }
}
