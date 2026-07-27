namespace PepperX.Core.Models
{
    using System;
    using System.Collections.Generic;
    using PepperX.Core.Enums;
    using PepperX.Core.Helpers;

    /// <summary>
    /// An extent is a single immutable stored object: a binary payload plus its metadata. The database row
    /// locates the payload and indexes its labels and tags; the freeform JSON metadata object is stored only
    /// in the extent file, not in the database.
    /// </summary>
    public class Extent
    {
        #region Public-Members

        /// <summary>
        /// Extent identifier (prefix <c>ext_</c>). Never null or empty.
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
        /// Owning container identifier. Never null or empty.
        /// </summary>
        public string ContainerId
        {
            get
            {
                return _ContainerId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ContainerId));
                _ContainerId = value;
            }
        }

        /// <summary>
        /// Caller-chosen object key, unique per container among active extents. Never null or empty.
        /// </summary>
        public string Key
        {
            get
            {
                return _Key;
            }
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Key));
                _Key = value;
            }
        }

        /// <summary>
        /// Lifecycle state. Default <see cref="ExtentStateEnum.Active"/>.
        /// </summary>
        public ExtentStateEnum State { get; set; } = ExtentStateEnum.Active;

        /// <summary>
        /// Payload size in bytes. Zero or greater.
        /// </summary>
        public long SizeBytes
        {
            get
            {
                return _SizeBytes;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(SizeBytes));
                _SizeBytes = value;
            }
        }

        /// <summary>
        /// Lowercase hex SHA-256 of the payload.
        /// </summary>
        public string Sha256
        {
            get
            {
                return _Sha256;
            }
            set
            {
                _Sha256 = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Lowercase hex MD5 of the payload (the content hash used to derive the S3 ETag). Null for
        /// extents written before MD5 was recorded.
        /// </summary>
        public string? Md5 { get; set; } = null;

        /// <summary>
        /// Persisted S3 ETag for a multipart-assembled object, of the form <c>hex(md5-of-part-md5s)-N</c>.
        /// Null for single-part objects (whose S3 ETag is derived from <see cref="Md5"/>).
        /// </summary>
        public string? Etag { get; set; } = null;

        /// <summary>
        /// Content type of the payload. May be null.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Storage driver that holds the payload. Default <see cref="StorageDriverTypeEnum.Disk"/>.
        /// </summary>
        public StorageDriverTypeEnum StorageDriver { get; set; } = StorageDriverTypeEnum.Disk;

        /// <summary>
        /// Driver-relative location of the payload. Never null or empty for a persisted extent.
        /// </summary>
        public string StorageLocation
        {
            get
            {
                return _StorageLocation;
            }
            set
            {
                _StorageLocation = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Whether the extent carries a freeform JSON metadata object (stored in the extent file).
        /// </summary>
        public bool HasMetadataObject { get; set; } = false;

        /// <summary>
        /// Labels attached to the extent. Never null. Not persisted on this object by the database layer
        /// unless explicitly hydrated.
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
        /// UTC creation timestamp.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC last-update timestamp.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateExtentId();
        private string _ContainerId = String.Empty;
        private string _Key = String.Empty;
        private long _SizeBytes = 0;
        private string _Sha256 = String.Empty;
        private string _StorageLocation = String.Empty;
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an extent. The identifier defaults to a generated value.
        /// </summary>
        public Extent()
        {
        }

        #endregion
    }
}
