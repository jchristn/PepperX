namespace PepperX.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using PepperX.Core.Helpers;

    /// <summary>
    /// A container is the top-level scope that holds extents. A container maps to an S3 bucket and to a
    /// RESP database index. Container names are constrained so that every container is addressable across
    /// all protocol surfaces (in particular S3 path-style addressing).
    /// </summary>
    public class Container
    {
        #region Public-Members

        /// <summary>
        /// Container identifier (prefix <c>ctr_</c>). Never null or empty.
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
        /// Container name. Must be 3 to 63 characters, lowercase letters, digits, or hyphens, and must
        /// start and end with a letter or digit. This keeps every container addressable as an S3 bucket.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a valid container name.</exception>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = ValidateName(value);
            }
        }

        /// <summary>
        /// Container tags (string keys and values). Never null. Surfaced as S3 bucket tags.
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
        /// Number of active objects in the container. Maintained transactionally. Zero or greater.
        /// </summary>
        public long ObjectCount
        {
            get
            {
                return _ObjectCount;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(ObjectCount));
                _ObjectCount = value;
            }
        }

        /// <summary>
        /// Total bytes stored across active objects in the container. Maintained transactionally. Zero or greater.
        /// </summary>
        public long TotalBytes
        {
            get
            {
                return _TotalBytes;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(TotalBytes));
                _TotalBytes = value;
            }
        }

        /// <summary>
        /// Optional RESP (Redis) database index this container answers to. When set, a Redis client that
        /// issues <c>SELECT n</c> with this index addresses this container instead of the default
        /// <c>resp{n}</c>. Null means the container is not reachable over RESP by an explicit index.
        /// A negative value is coerced to null. Uniqueness across containers is enforced by the database.
        /// </summary>
        public int? RespDatabaseIndex
        {
            get
            {
                return _RespDatabaseIndex;
            }
            set
            {
                _RespDatabaseIndex = (value.HasValue && value.Value < 0) ? null : value;
            }
        }

        /// <summary>
        /// Per-container cache configuration, persisted in the metadata database. Never null; a null
        /// assignment is coalesced to a fresh default so downstream code never null-checks it.
        /// </summary>
        public ContainerCacheSettings Cache
        {
            get
            {
                return _Cache;
            }
            set
            {
                _Cache = value ?? new ContainerCacheSettings();
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

        private static readonly Regex _NamePattern = new Regex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);

        private string _Id = IdGenerator.GenerateContainerId();
        private string _Name = String.Empty;
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();
        private long _ObjectCount = 0;
        private long _TotalBytes = 0;
        private int? _RespDatabaseIndex = null;
        private ContainerCacheSettings _Cache = new ContainerCacheSettings();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a container. The identifier defaults to a generated value.
        /// </summary>
        public Container()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Determine whether a string is a valid container name without throwing.
        /// </summary>
        /// <param name="name">Candidate name.</param>
        /// <returns>True when the name is valid.</returns>
        public static bool IsValidName(string? name)
        {
            if (String.IsNullOrEmpty(name)) return false;
            if (name.Length < 3 || name.Length > 63) return false;
            return _NamePattern.IsMatch(name);
        }

        #endregion

        #region Private-Methods

        private static string ValidateName(string? name)
        {
            if (!IsValidName(name))
            {
                throw new ArgumentException(
                    "Container name must be 3 to 63 characters using lowercase letters, digits, or hyphens, and must start and end with a letter or digit.",
                    nameof(Name));
            }

            return name!;
        }

        #endregion
    }
}
