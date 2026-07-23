namespace PepperX.Core.Models
{
    using System;
    using System.Collections.Generic;
    using PepperX.Core.Helpers;

    /// <summary>
    /// A captured HTTP request and its response, recorded on the native REST plane. PepperX is
    /// unauthenticated, so no tenant, user, or principal fields are captured.
    /// </summary>
    public class RequestHistoryEntry
    {
        #region Public-Members

        /// <summary>
        /// Entry identifier (prefix <c>req_</c>). Never null or empty.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// HTTP method (GET, POST, PUT, DELETE, HEAD).
        /// </summary>
        public string Method { get; set; } = String.Empty;

        /// <summary>
        /// Matched route template, or the raw path when no template matched.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Full request URL including query string.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Response HTTP status code.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Whether the response status indicates success (status code below 400).
        /// </summary>
        public bool Success
        {
            get
            {
                return StatusCode > 0 && StatusCode < 400;
            }
        }

        /// <summary>
        /// Request duration in milliseconds. Zero or greater.
        /// </summary>
        public double DurationMs
        {
            get
            {
                return _DurationMs;
            }
            set
            {
                _DurationMs = value < 0 ? 0 : value;
            }
        }

        /// <summary>
        /// Client source IP as observed by the server. May be null.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Request headers with secrets redacted. Never null.
        /// </summary>
        public Dictionary<string, string> RequestHeaders
        {
            get
            {
                return _RequestHeaders;
            }
            set
            {
                _RequestHeaders = value ?? new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Request body (possibly truncated). May be null.
        /// </summary>
        public string? RequestBody { get; set; } = null;

        /// <summary>
        /// Original request body length in bytes, before truncation. Zero or greater.
        /// </summary>
        public long RequestBodyBytes
        {
            get
            {
                return _RequestBodyBytes;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(RequestBodyBytes));
                _RequestBodyBytes = value;
            }
        }

        /// <summary>
        /// Whether the request body was truncated.
        /// </summary>
        public bool RequestBodyTruncated { get; set; } = false;

        /// <summary>
        /// Response headers. Never null.
        /// </summary>
        public Dictionary<string, string> ResponseHeaders
        {
            get
            {
                return _ResponseHeaders;
            }
            set
            {
                _ResponseHeaders = value ?? new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Response body (possibly truncated). May be null.
        /// </summary>
        public string? ResponseBody { get; set; } = null;

        /// <summary>
        /// Original response body length in bytes, before truncation. Zero or greater.
        /// </summary>
        public long ResponseBodyBytes
        {
            get
            {
                return _ResponseBodyBytes;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(ResponseBodyBytes));
                _ResponseBodyBytes = value;
            }
        }

        /// <summary>
        /// Whether the response body was truncated.
        /// </summary>
        public bool ResponseBodyTruncated { get; set; } = false;

        /// <summary>
        /// UTC time the request began.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the response was sent. May be null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateRequestHistoryId();
        private double _DurationMs = 0;
        private long _RequestBodyBytes = 0;
        private long _ResponseBodyBytes = 0;
        private Dictionary<string, string> _RequestHeaders = new Dictionary<string, string>();
        private Dictionary<string, string> _ResponseHeaders = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a request history entry. The identifier defaults to a generated value.
        /// </summary>
        public RequestHistoryEntry()
        {
        }

        #endregion
    }
}
