namespace PepperX.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The result of a paginated, filtered enumeration.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    public class EnumerationResult<T>
    {
        #region Public-Members

        /// <summary>Whether the enumeration completed successfully.</summary>
        public bool Success { get; set; } = true;

        /// <summary>UTC time the enumeration started.</summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC time the enumeration completed.</summary>
        public DateTime EndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Maximum records requested per page.</summary>
        public int MaxResults { get; set; } = 100;

        /// <summary>Continuation token for the next page, or null when there are no more results.</summary>
        public string? ContinuationToken { get; set; } = null;

        /// <summary>Whether this page is the final page.</summary>
        public bool EndOfResults { get; set; } = true;

        /// <summary>Total records matching the query across all pages.</summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>Records not yet returned after this page.</summary>
        public long RecordsRemaining { get; set; } = 0;

        /// <summary>The records in this page. Never null.</summary>
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
