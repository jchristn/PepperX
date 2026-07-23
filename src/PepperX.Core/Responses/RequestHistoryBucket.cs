namespace PepperX.Core.Responses
{
    using System;

    /// <summary>
    /// A single time bucket in a request history summary. The server emits an entry for every bucket in the
    /// requested range, including empty ones.
    /// </summary>
    public class RequestHistoryBucket
    {
        #region Public-Members

        /// <summary>
        /// UTC start of the bucket (inclusive).
        /// </summary>
        public DateTime BucketStartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC end of the bucket (exclusive).
        /// </summary>
        public DateTime BucketEndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Count of successful requests (status below 400) in the bucket.
        /// </summary>
        public long SuccessCount { get; set; } = 0;

        /// <summary>
        /// Count of failed requests (status 400 or above) in the bucket.
        /// </summary>
        public long FailureCount { get; set; } = 0;

        /// <summary>
        /// Average request duration in milliseconds within the bucket.
        /// </summary>
        public double AverageDurationMs { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty bucket.
        /// </summary>
        public RequestHistoryBucket()
        {
        }

        #endregion
    }
}
