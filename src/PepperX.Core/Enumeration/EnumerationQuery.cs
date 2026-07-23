namespace PepperX.Core.Enumeration
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Globalization;

    /// <summary>
    /// Query parameters for paginated, filtered enumeration of containers or objects.
    /// Can be bound from a query string via <see cref="FromQueryString"/>.
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
        /// Number of matching records to skip (offset pagination). Zero or greater. Default 0.
        /// Mutually exclusive with <see cref="ContinuationToken"/>.
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
        /// Continuation token from a previous page (the identifier of the last returned record). Only valid
        /// with creation-ordered enumerations. Mutually exclusive with a non-zero <see cref="Skip"/>.
        /// </summary>
        public string? ContinuationToken { get; set; } = null;

        /// <summary>
        /// Result ordering. Default <see cref="EnumerationOrderEnum.CreatedDescending"/>.
        /// </summary>
        public EnumerationOrderEnum Ordering { get; set; } = EnumerationOrderEnum.CreatedDescending;

        /// <summary>
        /// Keep only records whose key or name starts with this prefix. Null disables the filter.
        /// </summary>
        public string? Prefix { get; set; } = null;

        /// <summary>
        /// Keep only records whose key or name ends with this suffix. Null disables the filter.
        /// </summary>
        public string? Suffix { get; set; } = null;

        /// <summary>
        /// Keep only records created strictly after this UTC timestamp. Null disables the filter.
        /// </summary>
        public DateTime? CreatedAfterUtc { get; set; } = null;

        /// <summary>
        /// Keep only records created strictly before this UTC timestamp. Null disables the filter.
        /// </summary>
        public DateTime? CreatedBeforeUtc { get; set; } = null;

        /// <summary>
        /// Label filter with AND semantics: a record is kept only when every label listed is present.
        /// Null or empty disables the filter. Applies to objects only.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tag filter with AND semantics: a record is kept only when every key is present with the given
        /// value. Null or empty disables the filter. Applies to objects only.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// When true, label and tag comparisons are case-insensitive. Default false (exact match).
        /// </summary>
        public bool CaseInsensitive { get; set; } = false;

        /// <summary>
        /// Optional list of container names to restrict a cross-container search to. Null or empty searches
        /// all containers. Ignored by container-scoped enumerations.
        /// </summary>
        public List<string>? Containers { get; set; } = null;

        #endregion

        #region Private-Members

        private int _MaxResults = 100;
        private int _Skip = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a default query (MaxResults 100, Skip 0, CreatedDescending).
        /// </summary>
        public EnumerationQuery()
        {
        }

        /// <summary>
        /// Parse a query from query-string name/value pairs. Parameter names are matched case-insensitively.
        /// </summary>
        /// <param name="query">Query-string collection. Null is treated as empty.</param>
        /// <returns>A populated query.</returns>
        /// <exception cref="ArgumentException">A parameter value could not be parsed.</exception>
        public static EnumerationQuery FromQueryString(NameValueCollection? query)
        {
            EnumerationQuery result = new EnumerationQuery();
            if (query == null) return result;

            string? max = query["maxResults"] ?? query["max"] ?? query["limit"];
            if (!String.IsNullOrEmpty(max))
            {
                if (!Int32.TryParse(max, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    throw new ArgumentException("maxResults must be an integer.");
                result.MaxResults = parsed;
            }

            string? skip = query["skip"] ?? query["offset"];
            if (!String.IsNullOrEmpty(skip))
            {
                if (!Int32.TryParse(skip, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    throw new ArgumentException("skip must be an integer.");
                result.Skip = parsed;
            }

            string? token = query["continuationToken"] ?? query["token"];
            if (!String.IsNullOrEmpty(token)) result.ContinuationToken = token;

            string? order = query["ordering"] ?? query["order"];
            if (!String.IsNullOrEmpty(order))
            {
                if (!Enum.TryParse<EnumerationOrderEnum>(order, ignoreCase: true, out EnumerationOrderEnum parsed))
                    throw new ArgumentException("ordering must be one of CreatedAscending, CreatedDescending, KeyAscending, KeyDescending.");
                result.Ordering = parsed;
            }

            string? prefix = query["prefix"];
            if (!String.IsNullOrEmpty(prefix)) result.Prefix = prefix;

            string? suffix = query["suffix"];
            if (!String.IsNullOrEmpty(suffix)) result.Suffix = suffix;

            result.CreatedAfterUtc = ParseUtc(query["createdAfterUtc"] ?? query["after"], "createdAfterUtc");
            result.CreatedBeforeUtc = ParseUtc(query["createdBeforeUtc"] ?? query["before"], "createdBeforeUtc");

            string? labels = query["labels"];
            if (labels != null)
            {
                List<string> parsedLabels = new List<string>();
                foreach (string segment in labels.Split(','))
                {
                    string trimmed = segment.Trim();
                    if (trimmed.Length > 0) parsedLabels.Add(trimmed);
                }
                if (parsedLabels.Count > 0) result.Labels = parsedLabels;
            }

            string? tags = query["tags"];
            if (tags != null)
            {
                Dictionary<string, string> parsedTags = new Dictionary<string, string>();
                foreach (string segment in tags.Split(','))
                {
                    string trimmed = segment.Trim();
                    if (trimmed.Length == 0) continue;
                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0) throw new ArgumentException("tags must be a comma-separated list of key=value pairs.");
                    string key = trimmed.Substring(0, eq).Trim();
                    string value = trimmed.Substring(eq + 1).Trim();
                    if (key.Length == 0) throw new ArgumentException("tag keys must be non-empty.");
                    parsedTags[key] = value;
                }
                if (parsedTags.Count > 0) result.Tags = parsedTags;
            }

            string? ci = query["caseInsensitive"];
            if (!String.IsNullOrEmpty(ci))
            {
                if (String.Equals(ci, "true", StringComparison.OrdinalIgnoreCase) || ci == "1") result.CaseInsensitive = true;
                else if (String.Equals(ci, "false", StringComparison.OrdinalIgnoreCase) || ci == "0") result.CaseInsensitive = false;
                else throw new ArgumentException("caseInsensitive must be true, false, 1, or 0.");
            }

            return result;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate the query's internal consistency.
        /// </summary>
        /// <param name="errorMessage">Populated with a diagnostic when validation fails; otherwise null.</param>
        /// <returns>True when valid; otherwise false.</returns>
        public bool Validate(out string? errorMessage)
        {
            errorMessage = null;

            if (!String.IsNullOrEmpty(ContinuationToken) && Skip > 0)
            {
                errorMessage = "ContinuationToken and Skip cannot both be specified.";
                return false;
            }

            if (!String.IsNullOrEmpty(ContinuationToken)
                && Ordering != EnumerationOrderEnum.CreatedAscending
                && Ordering != EnumerationOrderEnum.CreatedDescending)
            {
                errorMessage = "ContinuationToken is only valid with a creation-ordered enumeration.";
                return false;
            }

            if (CreatedAfterUtc.HasValue && CreatedBeforeUtc.HasValue && CreatedAfterUtc.Value >= CreatedBeforeUtc.Value)
            {
                errorMessage = "CreatedAfterUtc must be strictly before CreatedBeforeUtc.";
                return false;
            }

            return true;
        }

        #endregion

        #region Private-Methods

        private static DateTime? ParseUtc(string? value, string field)
        {
            if (String.IsNullOrEmpty(value)) return null;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed))
                throw new ArgumentException(field + " must be an ISO-8601 timestamp.");
            return parsed;
        }

        #endregion
    }
}
