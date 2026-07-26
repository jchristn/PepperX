namespace PepperX.Core.Caching
{
    using System;
    using PepperX.Core.Models;

    /// <summary>
    /// One entry in a container cache: an object's payload bytes together with its metadata and the id of
    /// the extent they came from. The extent id is the coherence token — a read serves this entry only
    /// while it still matches the container's current active extent for the key.
    /// </summary>
    public class CachedObject
    {
        #region Public-Members

        /// <summary>
        /// Object key.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Identifier of the extent these bytes were read from. Compared against the current active extent
        /// on every read to detect a replace or delete on this or another node.
        /// </summary>
        public string ExtentId { get; }

        /// <summary>
        /// The object's metadata (size, checksum, content type, labels, tags, freeform object).
        /// </summary>
        public ObjectMetadata Metadata { get; }

        /// <summary>
        /// The full payload bytes.
        /// </summary>
        public byte[] Payload { get; }

        /// <summary>
        /// Estimated in-memory footprint of this entry, in bytes: the payload plus a fixed allowance for
        /// the metadata and object overhead. Used by the cache's memory-based eviction.
        /// </summary>
        public long SizeBytes { get; }

        #endregion

        #region Private-Members

        private const long _MetadataOverheadBytes = 512;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a cache entry.
        /// </summary>
        /// <param name="key">Object key.</param>
        /// <param name="extentId">Source extent id (coherence token).</param>
        /// <param name="metadata">Object metadata.</param>
        /// <param name="payload">Full payload bytes.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public CachedObject(string key, string extentId, ObjectMetadata metadata, byte[] payload)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (String.IsNullOrEmpty(extentId)) throw new ArgumentNullException(nameof(extentId));

            Key = key;
            ExtentId = extentId;
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            SizeBytes = payload.LongLength + _MetadataOverheadBytes;
        }

        #endregion
    }
}
