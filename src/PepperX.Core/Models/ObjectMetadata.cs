namespace PepperX.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A response-facing, merged view of an object's metadata: its identity, size and checksum, content
    /// type, labels, tags, and the freeform JSON metadata object. The <see cref="Object"/> property is the
    /// single intentionally schemaless surface in PepperX.
    /// </summary>
    public class ObjectMetadata
    {
        #region Public-Members

        /// <summary>
        /// Object key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Backing extent identifier.
        /// </summary>
        public string ExtentId { get; set; } = String.Empty;

        /// <summary>
        /// Owning container identifier.
        /// </summary>
        public string ContainerId { get; set; } = String.Empty;

        /// <summary>
        /// Owning container name. May be null when not resolved.
        /// </summary>
        public string? ContainerName { get; set; } = null;

        /// <summary>
        /// Payload size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Lowercase hex SHA-256 of the payload.
        /// </summary>
        public string Sha256 { get; set; } = String.Empty;

        /// <summary>
        /// Lowercase hex MD5 of the payload (the content hash used to derive the S3 ETag). Null for
        /// legacy objects written before MD5 was recorded.
        /// </summary>
        public string? Md5 { get; set; } = null;

        /// <summary>
        /// Persisted S3 ETag for multipart-assembled objects, of the form <c>hex(md5-of-part-md5s)-N</c>.
        /// Null for single-part objects, whose S3 ETag is derived from <see cref="Md5"/>. This is an
        /// S3-surface value; the content hash is <see cref="Md5"/>.
        /// </summary>
        public string? Etag { get; set; } = null;

        /// <summary>
        /// Content type of the payload. May be null.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Labels attached to the object. Never null.
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
        /// Tags attached to the object. Never null.
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
        /// Freeform JSON metadata object or array, if present. Null when the object carries no metadata
        /// object, or when this view was produced by a listing that omits it for performance.
        /// </summary>
        public object? Object { get; set; } = null;

        /// <summary>
        /// Whether the backing extent carries a metadata object. This is true even when <see cref="Object"/>
        /// is null because a listing omitted the body.
        /// </summary>
        public bool HasMetadataObject { get; set; } = false;

        /// <summary>
        /// UTC creation timestamp.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty object metadata view.
        /// </summary>
        public ObjectMetadata()
        {
        }

        #endregion
    }
}
