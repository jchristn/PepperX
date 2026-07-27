namespace PepperX.Core.Responses
{
    using System;

    /// <summary>
    /// A REST-facing summary of a single staged multipart part. Excludes internal fields such as the
    /// storage location.
    /// </summary>
    public class MultipartPartInfo
    {
        #region Public-Members

        /// <summary>
        /// Part number.
        /// </summary>
        public int PartNumber { get; set; } = 0;

        /// <summary>
        /// Part ETag (the part's MD5).
        /// </summary>
        public string ETag { get; set; } = String.Empty;

        /// <summary>
        /// Staged part size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// UTC time the part was staged.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a part summary.
        /// </summary>
        public MultipartPartInfo()
        {
        }

        #endregion
    }
}
