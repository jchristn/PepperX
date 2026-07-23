namespace PepperX.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Envelope request body for writing an object when the payload and metadata are supplied as JSON
    /// (the raw-write path carries the payload as the HTTP body instead).
    /// </summary>
    public class WriteObjectRequest
    {
        #region Public-Members

        /// <summary>
        /// Content type of the payload. May be null, in which case a default binary type is used.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Labels to attach. May be null.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags to attach. May be null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// Freeform JSON metadata object or array to attach. May be null.
        /// </summary>
        public object? Object { get; set; } = null;

        /// <summary>
        /// Base64-encoded payload bytes. May be null or empty for a zero-length object.
        /// </summary>
        public string? DataBase64 { get; set; } = null;

        /// <summary>
        /// When true, the write fails with a conflict if the key already exists. Default false (overwrite).
        /// </summary>
        public bool NoOverwrite { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a write object request.
        /// </summary>
        public WriteObjectRequest()
        {
        }

        #endregion
    }
}
