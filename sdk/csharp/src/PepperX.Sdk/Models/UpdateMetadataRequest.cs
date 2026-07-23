namespace PepperX.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Metadata changes to apply to an existing object. Because extents are immutable, applying this rewrites
    /// the object with the new metadata and its existing payload. A null collection leaves that field
    /// unchanged; an empty collection replaces it with an empty set.
    /// </summary>
    public class UpdateMetadataRequest
    {
        #region Public-Members

        /// <summary>New label set, or null to leave labels unchanged.</summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>New tag set, or null to leave tags unchanged.</summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>New metadata object, or null to leave it unchanged.</summary>
        public object? Object { get; set; } = null;

        /// <summary>When true, removes the metadata object regardless of <see cref="Object"/>.</summary>
        public bool ClearObject { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty metadata update.
        /// </summary>
        public UpdateMetadataRequest()
        {
        }

        #endregion
    }
}
