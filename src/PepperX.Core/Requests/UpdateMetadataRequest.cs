namespace PepperX.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Request body to update an object's metadata. Because extents are immutable, applying this rewrites
    /// the extent with the new metadata and the existing payload. A null collection means "leave unchanged";
    /// an empty collection means "replace with empty".
    /// </summary>
    public class UpdateMetadataRequest
    {
        #region Public-Members

        /// <summary>
        /// New label set, or null to leave labels unchanged.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// New tag set, or null to leave tags unchanged.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// New metadata object, or null to leave the existing object unchanged (use <see cref="ClearObject"/>
        /// to remove it).
        /// </summary>
        public object? Object { get; set; } = null;

        /// <summary>
        /// When true, the metadata object is removed regardless of <see cref="Object"/>. Default false.
        /// </summary>
        public bool ClearObject { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an update metadata request.
        /// </summary>
        public UpdateMetadataRequest()
        {
        }

        #endregion
    }
}
