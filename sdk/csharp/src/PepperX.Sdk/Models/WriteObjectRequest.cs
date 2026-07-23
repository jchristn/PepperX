namespace PepperX.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Metadata to attach when writing an object.
    /// </summary>
    public class WriteObjectRequest
    {
        #region Public-Members

        /// <summary>Content type of the payload. May be null.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Labels to attach. May be null.</summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>Tags to attach. May be null.</summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>Freeform JSON metadata object or array to attach. May be null.</summary>
        public object? Object { get; set; } = null;

        /// <summary>When true, the write fails if the key already exists. Default false (overwrite).</summary>
        public bool NoOverwrite { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty write request.
        /// </summary>
        public WriteObjectRequest()
        {
        }

        #endregion
    }
}
