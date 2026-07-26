namespace PepperX.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using PepperX.Core.Models;

    /// <summary>
    /// A small per-container manifest persisted alongside a container's extents. Container tags and cache
    /// settings cannot be derived from extent headers, so this manifest keeps them recoverable during
    /// rehydration (including a full <c>Rebuild</c>).
    /// </summary>
    public class ContainerManifest
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
        /// UTC creation timestamp.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optional RESP database index, mirrored here so a full rebuild can restore it. Null when unset.
        /// </summary>
        public int? RespDatabaseIndex { get; set; } = null;

        /// <summary>
        /// Per-container cache configuration, mirrored here so a full rebuild restores it. Never null.
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
        /// Instantiate an empty container manifest.
        /// </summary>
        public ContainerManifest()
        {
        }

        #endregion
    }
}
