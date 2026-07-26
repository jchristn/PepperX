namespace PepperX.Core.Requests
{
    using PepperX.Core.Enums;
    using PepperX.Core.Models;

    /// <summary>
    /// A request to change a container's cache configuration. Fields carry the same clamped, null-safe
    /// semantics as <see cref="ContainerCacheSettings"/>, so a request and the persisted model can never
    /// disagree on bounds.
    /// </summary>
    public class UpdateCacheSettingsRequest
    {
        #region Public-Members

        /// <summary>
        /// Whether caching is enabled. Default false.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Eviction policy. Default <see cref="CacheEvictionPolicyEnum.LRU"/>.
        /// </summary>
        public CacheEvictionPolicyEnum Policy { get; set; } = CacheEvictionPolicyEnum.LRU;

        /// <summary>
        /// Maximum number of objects. Clamped on conversion. Default 1000.
        /// </summary>
        public int MaxObjects { get; set; } = 1000;

        /// <summary>
        /// Maximum memory in bytes; 0 means no cap. Clamped on conversion. Default 0.
        /// </summary>
        public long MaxMemoryBytes { get; set; } = 0;

        /// <summary>
        /// Entries to evict on contention. Clamped on conversion. Default 10.
        /// </summary>
        public int EvictCount { get; set; } = 10;

        /// <summary>
        /// Per-object admission ceiling in bytes; 0 means no ceiling. Clamped on conversion. Default
        /// 1 MiB (1048576).
        /// </summary>
        public long MaxCacheableObjectBytes { get; set; } = 1048576;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a default update request.
        /// </summary>
        public UpdateCacheSettingsRequest()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate the raw request values before they are clamped, so the API can reject a clearly-invalid
        /// request with a 400 rather than silently normalizing it. Enabled-false requests are always valid
        /// (the other fields are ignored while disabled).
        /// </summary>
        /// <param name="error">Set to a human-readable reason when the result is false.</param>
        /// <returns>True when the request is acceptable.</returns>
        public bool Validate(out string? error)
        {
            if (!Enabled)
            {
                error = null;
                return true;
            }

            if (MaxObjects < 1)
            {
                error = "MaxObjects must be at least 1 when caching is enabled.";
                return false;
            }

            if (EvictCount < 1)
            {
                error = "EvictCount must be at least 1 when caching is enabled.";
                return false;
            }

            if (EvictCount > MaxObjects)
            {
                error = "EvictCount cannot exceed MaxObjects.";
                return false;
            }

            if (MaxMemoryBytes < 0)
            {
                error = "MaxMemoryBytes cannot be negative.";
                return false;
            }

            if (MaxCacheableObjectBytes < 0)
            {
                error = "MaxCacheableObjectBytes cannot be negative.";
                return false;
            }

            if (MaxMemoryBytes > 0 && MaxCacheableObjectBytes > 0 && MaxMemoryBytes < MaxCacheableObjectBytes)
            {
                error = "MaxMemoryBytes must be at least MaxCacheableObjectBytes so at least one object can be admitted.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Convert to a <see cref="ContainerCacheSettings"/>, which re-clamps every value.
        /// </summary>
        /// <returns>Clamped settings.</returns>
        public ContainerCacheSettings ToSettings()
        {
            ContainerCacheSettings settings = new ContainerCacheSettings
            {
                Enabled = Enabled,
                Policy = Policy,
                MaxObjects = MaxObjects,
                MaxMemoryBytes = MaxMemoryBytes,
                EvictCount = EvictCount,
                MaxCacheableObjectBytes = MaxCacheableObjectBytes
            };
            return settings;
        }

        #endregion
    }
}
