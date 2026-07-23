namespace PepperX.Sdk.Models
{
    using System;

    /// <summary>
    /// Per-container rollup within a statistics response.
    /// </summary>
    public class ContainerStatistics
    {
        #region Public-Members

        /// <summary>Container identifier.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Container name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Number of active objects.</summary>
        public long ObjectCount { get; set; } = 0;

        /// <summary>Total bytes across active objects.</summary>
        public long TotalBytes { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty container statistics rollup.
        /// </summary>
        public ContainerStatistics()
        {
        }

        #endregion
    }
}
