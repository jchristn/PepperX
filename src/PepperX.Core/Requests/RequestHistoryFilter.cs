namespace PepperX.Core.Requests
{
    using System;

    /// <summary>
    /// Typed filter for querying request history. Used by list, summary, and bulk-delete operations.
    /// </summary>
    public class RequestHistoryFilter
    {
        #region Public-Members

        /// <summary>
        /// Filter by HTTP method (exact, case-insensitive). Null disables the filter.
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// Filter by exact response status code. Null disables the filter.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Keep only entries whose path contains this substring. Null disables the filter.
        /// </summary>
        public string? PathContains { get; set; } = null;

        /// <summary>
        /// Keep only entries created at or after this UTC time. Null disables the lower bound.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Keep only entries created at or before this UTC time. Null disables the upper bound.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// One-based page number for list results. Clamped to 1 or greater. Default 1.
        /// </summary>
        public int PageNumber
        {
            get
            {
                return _PageNumber;
            }
            set
            {
                _PageNumber = value < 1 ? 1 : value;
            }
        }

        /// <summary>
        /// Page size for list results. Clamped to the range 1 to 1000. Default 25.
        /// </summary>
        public int PageSize
        {
            get
            {
                return _PageSize;
            }
            set
            {
                _PageSize = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// Bucket width in minutes for the summary operation. Clamped to the range 1 to 1440. Default 15.
        /// </summary>
        public int BucketMinutes
        {
            get
            {
                return _BucketMinutes;
            }
            set
            {
                _BucketMinutes = Math.Clamp(value, 1, 1440);
            }
        }

        #endregion

        #region Private-Members

        private int _PageNumber = 1;
        private int _PageSize = 25;
        private int _BucketMinutes = 15;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a request history filter.
        /// </summary>
        public RequestHistoryFilter()
        {
        }

        #endregion
    }
}
