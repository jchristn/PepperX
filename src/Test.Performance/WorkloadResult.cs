namespace Test.Performance
{
    using System;

    /// <summary>
    /// The outcome of one workload run.
    /// </summary>
    public class WorkloadResult
    {
        #region Public-Members

        /// <summary>Workload name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Transport exercised (REST, S3, RESP).</summary>
        public string Transport { get; set; } = "REST";

        /// <summary>Number of successful operations.</summary>
        public long Operations { get; set; } = 0;

        /// <summary>Number of failed operations.</summary>
        public long Errors { get; set; } = 0;

        /// <summary>Total bytes transferred.</summary>
        public long Bytes { get; set; } = 0;

        /// <summary>Measured duration in seconds (excluding warmup).</summary>
        public double DurationSeconds { get; set; } = 0;

        /// <summary>Concurrency level used.</summary>
        public int Concurrency { get; set; } = 0;

        /// <summary>Operations per second.</summary>
        public double OperationsPerSecond
        {
            get
            {
                return DurationSeconds <= 0 ? 0 : Operations / DurationSeconds;
            }
        }

        /// <summary>Megabytes per second.</summary>
        public double MegabytesPerSecond
        {
            get
            {
                return DurationSeconds <= 0 ? 0 : Bytes / 1048576.0 / DurationSeconds;
            }
        }

        /// <summary>Minimum latency in milliseconds.</summary>
        public double LatencyMinMs { get; set; } = 0;

        /// <summary>Mean latency in milliseconds.</summary>
        public double LatencyMeanMs { get; set; } = 0;

        /// <summary>Median latency in milliseconds.</summary>
        public double LatencyP50Ms { get; set; } = 0;

        /// <summary>95th percentile latency in milliseconds.</summary>
        public double LatencyP95Ms { get; set; } = 0;

        /// <summary>99th percentile latency in milliseconds.</summary>
        public double LatencyP99Ms { get; set; } = 0;

        /// <summary>Maximum latency in milliseconds.</summary>
        public double LatencyMaxMs { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty workload result.
        /// </summary>
        public WorkloadResult()
        {
        }

        #endregion
    }
}
