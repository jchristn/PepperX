namespace PepperX.Core.Storage.Format
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The self-describing header of an extent file. It carries everything needed to rebuild the extent's
    /// database row and its labels, tags, and freeform metadata object, so a database can be fully rehydrated
    /// from raw storage.
    /// </summary>
    public class ExtentHeader
    {
        #region Public-Members

        /// <summary>
        /// Extent identifier.
        /// </summary>
        public string ExtentId { get; set; } = String.Empty;

        /// <summary>
        /// Owning container identifier.
        /// </summary>
        public string ContainerId { get; set; } = String.Empty;

        /// <summary>
        /// Owning container name.
        /// </summary>
        public string ContainerName { get; set; } = String.Empty;

        /// <summary>
        /// Object key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Content type of the payload. May be null.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Payload size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Lowercase hex SHA-256 of the payload.
        /// </summary>
        public string Sha256 { get; set; } = String.Empty;

        /// <summary>
        /// Lowercase hex MD5 of the payload (the content hash used to derive the S3 ETag). May be null
        /// for extents written before MD5 was recorded.
        /// </summary>
        public string? Md5 { get; set; } = null;

        /// <summary>
        /// Persisted S3 ETag for a multipart-assembled object (<c>digest-N</c>). Null for single-part
        /// objects. Stored in the header so a full rebuild restores it.
        /// </summary>
        public string? Etag { get; set; } = null;

        /// <summary>
        /// Labels attached to the extent. Never null.
        /// </summary>
        public List<string> Labels
        {
            get
            {
                return _Labels;
            }
            set
            {
                _Labels = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Tags attached to the extent. Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get
            {
                return _Tags;
            }
            set
            {
                _Tags = value ?? new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Freeform JSON metadata object or array. May be null.
        /// </summary>
        public object? Object { get; set; } = null;

        /// <summary>
        /// UTC creation timestamp.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Format version that produced the file.
        /// </summary>
        public int FormatVersion { get; set; } = ExtentFormatConstants.FormatVersion;

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty extent header.
        /// </summary>
        public ExtentHeader()
        {
        }

        #endregion
    }
}
