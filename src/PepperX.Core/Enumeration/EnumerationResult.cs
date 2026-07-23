namespace PepperX.Core.Enumeration
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The result of a paginated, filtered enumeration.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    public class EnumerationResult<T>
    {
        #region Public-Members

        /// <summary>
        /// Whether the enumeration completed successfully.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// UTC time the enumeration started.
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the enumeration completed.
        /// </summary>
        public DateTime EndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Maximum number of records requested per page. Minimum 1.
        /// </summary>
        public int MaxResults
        {
            get
            {
                return _MaxResults;
            }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(MaxResults));
                _MaxResults = value;
            }
        }

        /// <summary>
        /// Continuation token for the next page, or null when there are no more results. The token is the
        /// identifier of the last record returned and is only meaningful for creation-ordered enumerations.
        /// </summary>
        public string? ContinuationToken { get; set; } = null;

        /// <summary>
        /// Whether this page is the final page.
        /// </summary>
        public bool EndOfResults { get; set; } = true;

        /// <summary>
        /// Total number of records matching the query across all pages. Zero or greater.
        /// </summary>
        public long TotalRecords
        {
            get
            {
                return _TotalRecords;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(TotalRecords));
                _TotalRecords = value;
            }
        }

        /// <summary>
        /// Number of matching records not yet returned after this page. Zero or greater.
        /// </summary>
        public long RecordsRemaining
        {
            get
            {
                return _RecordsRemaining;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(RecordsRemaining));
                _RecordsRemaining = value;
            }
        }

        /// <summary>
        /// The records in this page. Never null.
        /// </summary>
        [JsonPropertyOrder(999)]
        public List<T> Objects
        {
            get
            {
                return _Objects;
            }
            set
            {
                _Objects = value ?? new List<T>();
            }
        }

        #endregion

        #region Private-Members

        private int _MaxResults = 100;
        private long _TotalRecords = 0;
        private long _RecordsRemaining = 0;
        private List<T> _Objects = new List<T>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty enumeration result.
        /// </summary>
        public EnumerationResult()
        {
        }

        #endregion
    }
}
