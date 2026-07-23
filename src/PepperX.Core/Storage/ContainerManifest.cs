namespace PepperX.Core.Storage
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A small per-container manifest persisted alongside a container's extents. Container tags cannot be
    /// derived from extent headers, so this manifest keeps them recoverable during rehydration.
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

        #endregion

        #region Private-Members

        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

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
