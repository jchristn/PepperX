namespace PepperX.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Models;
    using PepperX.Core.Responses;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage;

    /// <summary>
    /// Aggregates container, storage, database, and cluster statistics.
    /// </summary>
    public sealed class StatisticsService
    {
        #region Private-Members

        private readonly IMetadataDatabaseDriver _Db;
        private readonly IExtentStorageDriver _Storage;
        private readonly ClusterSettings _Cluster;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the statistics service.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="storage">Extent storage driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public StatisticsService(IMetadataDatabaseDriver db, IExtentStorageDriver storage, PepperXSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _Cluster = settings.Cluster;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Compute aggregate statistics.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The statistics response.</returns>
        public async Task<StatisticsResponse> GetAsync(CancellationToken token = default)
        {
            StatisticsResponse response = new StatisticsResponse();

            IReadOnlyList<ContainerStatistics> containers = await _Db.Containers.ReadAllStatisticsAsync(token).ConfigureAwait(false);
            long objectCount = 0;
            long totalBytes = 0;
            List<ContainerStatistics> list = new List<ContainerStatistics>();
            foreach (ContainerStatistics c in containers)
            {
                objectCount += c.ObjectCount;
                totalBytes += c.TotalBytes;
                list.Add(c);
            }

            response.ContainerCount = containers.Count;
            response.ObjectCount = objectCount;
            response.TotalBytes = totalBytes;
            response.Containers = list;

            StorageCapacity capacity = await _Storage.GetCapacityAsync(token).ConfigureAwait(false);
            response.StorageTotalBytes = capacity.TotalBytes;
            response.StorageFreeBytes = capacity.FreeBytes;

            response.DatabaseSizeBytes = await _Db.GetDatabaseSizeBytesAsync(token).ConfigureAwait(false);

            response.Nodes = await GetNodesAsync(token).ConfigureAwait(false);
            return response;
        }

        /// <summary>
        /// List cluster nodes with liveness.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Node responses.</returns>
        public async Task<List<NodeResponse>> GetNodesAsync(CancellationToken token = default)
        {
            IReadOnlyList<NodeRecord> nodes = await _Db.Nodes.ListAsync(token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            List<NodeResponse> nodeResponses = new List<NodeResponse>();
            foreach (NodeRecord node in nodes) nodeResponses.Add(NodeResponse.FromModel(node, _Cluster.NodeDeadAfterSeconds, now));
            return nodeResponses;
        }

        #endregion
    }
}
