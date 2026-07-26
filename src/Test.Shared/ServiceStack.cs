namespace Test.Shared
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Caching;
    using PepperX.Core.Database;
    using PepperX.Core.Enums;
    using PepperX.Core.Helpers;
    using PepperX.Core.Services;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage.Disk;

    /// <summary>
    /// A fully wired set of PepperX services over a given database and a storage root, used by service-layer
    /// tests. Multiple stacks can share one database and one storage root to simulate a cluster.
    /// </summary>
    public sealed class ServiceStack
    {
        #region Public-Members

        /// <summary>This node's identifier.</summary>
        public string NodeId { get; }

        /// <summary>Metadata database driver.</summary>
        public IMetadataDatabaseDriver Db { get; }

        /// <summary>Storage driver.</summary>
        public DiskExtentStorageDriver Storage { get; }

        /// <summary>Container service.</summary>
        public ContainerService Containers { get; }

        /// <summary>Object write service.</summary>
        public ObjectWriteService Writes { get; }

        /// <summary>Object read service.</summary>
        public ObjectReadService Reads { get; }

        /// <summary>Object delete service.</summary>
        public ObjectDeleteService Deletes { get; }

        /// <summary>Search service.</summary>
        public SearchService Search { get; }

        /// <summary>Statistics service.</summary>
        public StatisticsService Statistics { get; }

        /// <summary>Rehydration service.</summary>
        public RehydrationService Rehydration { get; }

        /// <summary>Janitor service.</summary>
        public JanitorService Janitor { get; }

        /// <summary>The local lock registry.</summary>
        public LocalLockRegistry LocalLocks { get; }

        /// <summary>The per-container cache manager.</summary>
        public ContainerCacheManager Cache { get; }

        #endregion

        #region Private-Members

        private ServiceStack(string nodeId, IMetadataDatabaseDriver db, DiskExtentStorageDriver storage, LocalLockRegistry locks, ContainerCacheManager cache,
            ContainerService containers, ObjectWriteService writes, ObjectReadService reads, ObjectDeleteService deletes,
            SearchService search, StatisticsService statistics, RehydrationService rehydration, JanitorService janitor)
        {
            NodeId = nodeId;
            Db = db;
            Storage = storage;
            LocalLocks = locks;
            Cache = cache;
            Containers = containers;
            Writes = writes;
            Reads = reads;
            Deletes = deletes;
            Search = search;
            Statistics = statistics;
            Rehydration = rehydration;
            Janitor = janitor;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a service stack over a database and storage root.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="storageRoot">Storage root directory.</param>
        /// <param name="mode">Delete coordination mode.</param>
        /// <param name="nodeId">Optional node identifier; generated when null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A wired service stack.</returns>
        public static async Task<ServiceStack> CreateAsync(IMetadataDatabaseDriver db, string storageRoot, DeleteCoordinationModeEnum mode, string? nodeId = null, CancellationToken token = default)
        {
            string resolvedNode = nodeId ?? IdGenerator.GenerateNodeId();

            PepperXSettings settings = new PepperXSettings();
            settings.Storage.Disk.RootDirectory = storageRoot;
            settings.Cluster.NodeId = resolvedNode;
            settings.Cluster.DeleteCoordinationMode = mode;
            settings.Cluster.DeleteDrainPollMs = 20;

            DiskExtentStorageDriver storage = new DiskExtentStorageDriver(settings.Storage.Disk);
            await storage.InitializeAsync(token).ConfigureAwait(false);

            LocalLockRegistry locks = new LocalLockRegistry();
            ContainerCacheManager cache = new ContainerCacheManager();
            ObjectDeleteService deletes = new ObjectDeleteService(db, storage, settings, locks, cache);
            ObjectReadService reads = new ObjectReadService(db, storage, settings, locks, cache, resolvedNode);
            ObjectWriteService writes = new ObjectWriteService(db, storage, settings, reads, deletes, cache);
            ContainerService containers = new ContainerService(db, storage, deletes, cache);
            SearchService search = new SearchService(db);
            StatisticsService statistics = new StatisticsService(db, storage, settings);
            RehydrationService rehydration = new RehydrationService(db, storage);
            JanitorService janitor = new JanitorService(db, storage, deletes, settings);

            return new ServiceStack(resolvedNode, db, storage, locks, cache, containers, writes, reads, deletes, search, statistics, rehydration, janitor);
        }

        #endregion
    }
}
