namespace PepperX.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Metadata for a stored object: identity, size, checksum, content type, labels, tags, and the freeform
    /// JSON metadata object.
    /// </summary>
    public class ObjectMetadata
    {
        #region Public-Members

        /// <summary>Object key.</summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>Backing extent identifier.</summary>
        public string ExtentId { get; set; } = String.Empty;

        /// <summary>Owning container identifier.</summary>
        public string ContainerId { get; set; } = String.Empty;

        /// <summary>Owning container name. May be null.</summary>
        public string? ContainerName { get; set; } = null;

        /// <summary>Payload size in bytes.</summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>Lowercase hex SHA-256 of the payload.</summary>
        public string Sha256 { get; set; } = String.Empty;

        /// <summary>Content type of the payload. May be null.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Labels attached to the object. Never null.</summary>
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

        /// <summary>Tags attached to the object. Never null.</summary>
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
        /// Freeform JSON metadata object or array, when present. Listings omit this for performance; read an
        /// object's metadata directly to retrieve it.
        /// </summary>
        public object? Object { get; set; } = null;

        /// <summary>Whether the object carries a metadata object.</summary>
        public bool HasMetadataObject { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate empty object metadata.
        /// </summary>
        public ObjectMetadata()
        {
        }

        #endregion
    }
}
