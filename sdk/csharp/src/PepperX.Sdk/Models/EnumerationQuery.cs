namespace PepperX.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Query parameters for paginated, filtered enumeration of containers or objects.
    /// </summary>
    public class EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Maximum number of results to return. Clamped to the range 1 to 1000. Default 100.
        /// </summary>
        public int MaxResults
        {
            get
            {
                return _MaxResults;
            }
            set
            {
                _MaxResults = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// Number of matching records to skip. Zero or greater. Mutually exclusive with
        /// <see cref="ContinuationToken"/>.
        /// </summary>
        public int Skip
        {
            get
            {
                return _Skip;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(Skip), "Skip must be zero or greater.");
                _Skip = value;
            }
        }

        /// <summary>
        /// Continuation token from a previous page. Valid only with creation-ordered enumerations.
        /// </summary>
        public string? ContinuationToken { get; set; } = null;

        /// <summary>
        /// Result ordering. Default <see cref="EnumerationOrderEnum.CreatedDescending"/>.
        /// </summary>
        public EnumerationOrderEnum Ordering { get; set; } = EnumerationOrderEnum.CreatedDescending;

        /// <summary>
        /// Keep only records whose key or name starts with this prefix.
        /// </summary>
        public string? Prefix { get; set; } = null;

        /// <summary>
        /// Keep only records whose key or name ends with this suffix.
        /// </summary>
        public string? Suffix { get; set; } = null;

        /// <summary>
        /// Keep only records created strictly after this UTC timestamp.
        /// </summary>
        public DateTime? CreatedAfterUtc { get; set; } = null;

        /// <summary>
        /// Keep only records created strictly before this UTC timestamp.
        /// </summary>
        public DateTime? CreatedBeforeUtc { get; set; } = null;

        /// <summary>
        /// Label filter with AND semantics: every label listed must be present.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tag filter with AND semantics: every key must be present with the given value.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// When true, label and tag comparisons are case-insensitive. Default false.
        /// </summary>
        public bool CaseInsensitive { get; set; } = false;

        /// <summary>
        /// Container names to restrict a cross-container search to. Null searches every container.
        /// </summary>
        public List<string>? Containers { get; set; } = null;

        #endregion

        #region Private-Members

        private int _MaxResults = 100;
        private int _Skip = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a default query.
        /// </summary>
        public EnumerationQuery()
        {
        }

        #endregion
    }
}
