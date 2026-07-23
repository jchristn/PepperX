namespace PepperX.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Time-bucketed summary of request history, used to render the activity chart.
    /// </summary>
    public class RequestHistorySummary
    {
        #region Public-Members

        /// <summary>
        /// Total requests in the range.
        /// </summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>
        /// Total successful requests (status below 400) in the range.
        /// </summary>
        public long TotalSuccess { get; set; } = 0;

        /// <summary>
        /// Total failed requests (status 400 or above) in the range.
        /// </summary>
        public long TotalFailure { get; set; } = 0;

        /// <summary>
        /// Average request duration in milliseconds across the range.
        /// </summary>
        public double AverageDurationMs { get; set; } = 0;

        /// <summary>
        /// Buckets covering the full range, including empty ones. Never null.
        /// </summary>
        public List<RequestHistoryBucket> Buckets
        {
            get
            {
                return _Buckets;
            }
            set
            {
                _Buckets = value ?? new List<RequestHistoryBucket>();
            }
        }

        #endregion

        #region Private-Members

        private List<RequestHistoryBucket> _Buckets = new List<RequestHistoryBucket>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty summary.
        /// </summary>
        public RequestHistorySummary()
        {
        }

        #endregion
    }
}
