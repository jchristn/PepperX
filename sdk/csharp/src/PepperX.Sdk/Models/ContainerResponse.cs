namespace PepperX.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A container: the top-level scope that holds objects.
    /// </summary>
    public class ContainerResponse
    {
        #region Public-Members

        /// <summary>Container identifier.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Container name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Container tags. Never null.</summary>
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

        /// <summary>Number of active objects in the container.</summary>
        public long ObjectCount { get; set; } = 0;

        /// <summary>Total bytes stored across active objects.</summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty container response.
        /// </summary>
        public ContainerResponse()
        {
        }

        #endregion
    }
}
