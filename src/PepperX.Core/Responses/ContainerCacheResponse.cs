namespace PepperX.Core.Responses
{
    using System;
    using PepperX.Core.Enums;
    using PepperX.Core.Models;
    // Fully qualified at the use site: the local PepperX.Core.Caching namespace would otherwise shadow
    // the package's top-level Caching namespace.
    using CacheStatistics = global::Caching.CacheStatistics;

    /// <summary>
    /// A container's cache configuration together with its live runtime statistics. Statistics are zero
    /// when caching is disabled or the cache has not yet been built on this node.
    /// </summary>
    public class ContainerCacheResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether caching is enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Eviction policy.
        /// </summary>
        public CacheEvictionPolicyEnum Policy { get; set; } = CacheEvictionPolicyEnum.LRU;

        /// <summary>
        /// Maximum number of objects.
        /// </summary>
        public int MaxObjects { get; set; } = 0;

        /// <summary>
        /// Maximum memory in bytes; 0 means no cap.
        /// </summary>
        public long MaxMemoryBytes { get; set; } = 0;

        /// <summary>
        /// Entries evicted on contention.
        /// </summary>
        public int EvictCount { get; set; } = 0;

        /// <summary>
        /// Per-object admission ceiling in bytes; 0 means no ceiling.
        /// </summary>
        public long MaxCacheableObjectBytes { get; set; } = 0;

        /// <summary>
        /// Cache hits observed on this node.
        /// </summary>
        public long HitCount { get; set; } = 0;

        /// <summary>
        /// Cache misses observed on this node.
        /// </summary>
        public long MissCount { get; set; } = 0;

        /// <summary>
        /// Hit rate in the range 0 to 1.
        /// </summary>
        public double HitRate { get; set; } = 0;

        /// <summary>
        /// Objects currently resident on this node.
        /// </summary>
        public int CurrentCount { get; set; } = 0;

        /// <summary>
        /// Memory currently consumed on this node, in bytes.
        /// </summary>
        public long CurrentMemoryBytes { get; set; } = 0;

        /// <summary>
        /// Objects evicted on this node since the cache was built.
        /// </summary>
        public long EvictionCount { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty response.
        /// </summary>
        public ContainerCacheResponse()
        {
        }

        /// <summary>
        /// Build from settings and optional live statistics. When statistics are null (disabled or not yet
        /// built), the runtime counters are zero.
        /// </summary>
        /// <param name="settings">Cache settings.</param>
        /// <param name="statistics">Live statistics, or null.</param>
        /// <returns>A populated response.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        public static ContainerCacheResponse FromSettingsAndStatistics(ContainerCacheSettings settings, CacheStatistics? statistics)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            ContainerCacheResponse response = new ContainerCacheResponse
            {
                Enabled = settings.Enabled,
                Policy = settings.Policy,
                MaxObjects = settings.MaxObjects,
                MaxMemoryBytes = settings.MaxMemoryBytes,
                EvictCount = settings.EvictCount,
                MaxCacheableObjectBytes = settings.MaxCacheableObjectBytes
            };

            if (statistics != null)
            {
                response.HitCount = statistics.HitCount;
                response.MissCount = statistics.MissCount;
                response.HitRate = statistics.HitRate;
                response.CurrentCount = statistics.CurrentCount;
                response.CurrentMemoryBytes = statistics.CurrentMemoryBytes;
                response.EvictionCount = statistics.EvictionCount;
            }

            return response;
        }

        #endregion
    }
}
