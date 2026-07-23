namespace PepperX.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Aggregate statistics across containers, storage, database, and cluster nodes.
    /// </summary>
    public class StatisticsResponse
    {
        #region Public-Members

        /// <summary>Total number of containers.</summary>
        public long ContainerCount { get; set; } = 0;

        /// <summary>Total number of active objects.</summary>
        public long ObjectCount { get; set; } = 0;

        /// <summary>Total bytes stored.</summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>Per-container rollups. Never null.</summary>
        public List<ContainerStatistics> Containers
        {
            get
            {
                return _Containers;
            }
            set
            {
                _Containers = value ?? new List<ContainerStatistics>();
            }
        }

        /// <summary>Total capacity of the storage volume in bytes.</summary>
        public long StorageTotalBytes { get; set; } = 0;

        /// <summary>Free capacity of the storage volume in bytes.</summary>
        public long StorageFreeBytes { get; set; } = 0;

        /// <summary>Approximate metadata database size in bytes.</summary>
        public long DatabaseSizeBytes { get; set; } = 0;

        /// <summary>Cluster nodes and their liveness. Never null.</summary>
        public List<NodeResponse> Nodes
        {
            get
            {
                return _Nodes;
            }
            set
            {
                _Nodes = value ?? new List<NodeResponse>();
            }
        }

        #endregion

        #region Private-Members

        private List<ContainerStatistics> _Containers = new List<ContainerStatistics>();
        private List<NodeResponse> _Nodes = new List<NodeResponse>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty statistics response.
        /// </summary>
        public StatisticsResponse()
        {
        }

        #endregion
    }
}
