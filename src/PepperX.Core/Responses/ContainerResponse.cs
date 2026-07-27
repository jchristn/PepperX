namespace PepperX.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using PepperX.Core.Models;

    /// <summary>
    /// Transport view of a container.
    /// </summary>
    public class ContainerResponse
    {
        #region Public-Members

        /// <summary>
        /// Container identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Container name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Container tags. Never null.
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
        /// Number of active objects in the container.
        /// </summary>
        public long ObjectCount { get; set; } = 0;

        /// <summary>
        /// Total bytes stored across active objects.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// UTC creation timestamp.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC last-update timestamp.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optional RESP database index this container answers to (null when it is not addressable over RESP
        /// by an explicit index).
        /// </summary>
        public int? RespDatabaseIndex { get; set; } = null;

        /// <summary>
        /// Optional per-container multipart-upload expiry in days. Null when the container inherits the
        /// system-wide <c>S3.MultipartUploadExpiryDays</c> default.
        /// </summary>
        public int? MultipartUploadExpiryDays { get; set; } = null;

        /// <summary>
        /// Per-container cache configuration. Never null.
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

        #endregion

        #region Private-Members

        private Dictionary<string, string> _Tags = new Dictionary<string, string>();
        private ContainerCacheSettings _Cache = new ContainerCacheSettings();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty container response.
        /// </summary>
        public ContainerResponse()
        {
        }

        /// <summary>
        /// Build a response from a container model.
        /// </summary>
        /// <param name="container">Source container.</param>
        /// <returns>Container response.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="container"/> is null.</exception>
        public static ContainerResponse FromModel(Container container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));

            return new ContainerResponse
            {
                Id = container.Id,
                Name = container.Name,
                Tags = new Dictionary<string, string>(container.Tags),
                ObjectCount = container.ObjectCount,
                TotalBytes = container.TotalBytes,
                CreatedUtc = container.CreatedUtc,
                LastUpdateUtc = container.LastUpdateUtc,
                RespDatabaseIndex = container.RespDatabaseIndex,
                MultipartUploadExpiryDays = container.MultipartUploadExpiryDays,
                Cache = container.Cache.Clone()
            };
        }

        #endregion
    }
}
