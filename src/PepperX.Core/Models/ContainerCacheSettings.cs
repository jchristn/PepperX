namespace PepperX.Core.Models
{
    using System;
    using System.Globalization;
    using PepperX.Core.Enums;

    /// <summary>
    /// Per-container cache configuration, persisted in the metadata database. Every setter is null-safe
    /// and range-clamped so that a value arriving from an API body, a hand-edited database row, or a
    /// legacy row written before a column existed is normalized to a usable range rather than trusted.
    /// Setters clamp; they do not throw. Use <see cref="Validate"/> for the stricter check the API layer
    /// applies before accepting a change.
    /// </summary>
    public class ContainerCacheSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether caching is enabled for the container. Default false.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Eviction policy. Default <see cref="CacheEvictionPolicyEnum.LRU"/>.
        /// </summary>
        public CacheEvictionPolicyEnum Policy { get; set; } = CacheEvictionPolicyEnum.LRU;

        /// <summary>
        /// Maximum number of objects to hold. Clamped to the range 1 to <see cref="MaxObjectsCeiling"/>.
        /// Default 1000. <see cref="EvictCount"/> is re-clamped to not exceed this value.
        /// </summary>
        public int MaxObjects
        {
            get
            {
                return _MaxObjects;
            }
            set
            {
                _MaxObjects = Math.Clamp(value, 1, MaxObjectsCeiling);
                if (_EvictCount > _MaxObjects) _EvictCount = _MaxObjects;
            }
        }

        /// <summary>
        /// Maximum memory the cache may consume, in bytes. Clamped to the range 0 to
        /// <see cref="long.MaxValue"/>; 0 means no memory cap. Default 0.
        /// </summary>
        public long MaxMemoryBytes
        {
            get
            {
                return _MaxMemoryBytes;
            }
            set
            {
                _MaxMemoryBytes = value < 0 ? 0 : value;
            }
        }

        /// <summary>
        /// Number of entries to evict when the cache is full and admits a new entry. Clamped to the range
        /// 1 to <see cref="MaxObjects"/>. Default 10.
        /// </summary>
        public int EvictCount
        {
            get
            {
                return _EvictCount;
            }
            set
            {
                _EvictCount = Math.Clamp(value, 1, _MaxObjects);
            }
        }

        /// <summary>
        /// Objects larger than this are never admitted to the cache and always stream from terminal
        /// storage. Clamped to the range 0 to <see cref="long.MaxValue"/>; 0 means no ceiling. Default
        /// 1 MiB (1048576).
        /// </summary>
        public long MaxCacheableObjectBytes
        {
            get
            {
                return _MaxCacheableObjectBytes;
            }
            set
            {
                _MaxCacheableObjectBytes = value < 0 ? 0 : value;
            }
        }

        /// <summary>
        /// Upper bound applied to <see cref="MaxObjects"/>, protecting a node from a pathological
        /// configuration. Public and tunable; clamped to at least 1. Default 10,000,000.
        /// </summary>
        public int MaxObjectsCeiling
        {
            get
            {
                return _MaxObjectsCeiling;
            }
            set
            {
                _MaxObjectsCeiling = value < 1 ? 1 : value;
            }
        }

        #endregion

        #region Private-Members

        private int _MaxObjects = 1000;
        private long _MaxMemoryBytes = 0;
        private int _EvictCount = 10;
        private long _MaxCacheableObjectBytes = 1048576;
        private int _MaxObjectsCeiling = 10_000_000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate default (disabled) cache settings.
        /// </summary>
        public ContainerCacheSettings()
        {
        }

        /// <summary>
        /// The default applied when a container is created without explicit cache settings: caching
        /// enabled with an LRU policy, 1000 objects, evicting 10 on contention, a 256 MiB memory cap, and
        /// a 1 MiB per-object ceiling. Distinct from the disabled model/column default, which governs
        /// legacy rows.
        /// </summary>
        /// <returns>Creation-time default settings.</returns>
        public static ContainerCacheSettings CreationDefault()
        {
            return new ContainerCacheSettings
            {
                Enabled = true,
                Policy = CacheEvictionPolicyEnum.LRU,
                MaxObjects = 1000,
                EvictCount = 10,
                MaxMemoryBytes = 268435456,
                MaxCacheableObjectBytes = 1048576
            };
        }

        /// <summary>
        /// Parse an eviction policy name, falling back to <see cref="CacheEvictionPolicyEnum.LRU"/> for a
        /// null or unrecognized value. Never throws.
        /// </summary>
        /// <param name="value">Policy name (case-insensitive).</param>
        /// <returns>The parsed policy, or LRU.</returns>
        public static CacheEvictionPolicyEnum ParsePolicy(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return CacheEvictionPolicyEnum.LRU;
            return Enum.TryParse(value, ignoreCase: true, out CacheEvictionPolicyEnum parsed)
                ? parsed
                : CacheEvictionPolicyEnum.LRU;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A stable signature of the fields that determine the shape of the underlying cache. The cache
        /// manager rebuilds a container's cache when this changes.
        /// </summary>
        /// <returns>Signature string.</returns>
        public string BuildSignature()
        {
            return String.Create(
                CultureInfo.InvariantCulture,
                $"{Enabled}|{Policy}|{MaxObjects}|{MaxMemoryBytes}|{EvictCount}|{MaxCacheableObjectBytes}");
        }

        /// <summary>
        /// Stricter validation than the clamping setters, used by the API layer to reject a clearly
        /// invalid change with a 400 rather than silently normalizing it.
        /// </summary>
        /// <param name="error">Set to a human-readable reason when the result is false.</param>
        /// <returns>True when the settings are internally consistent.</returns>
        public bool Validate(out string? error)
        {
            if (EvictCount > MaxObjects)
            {
                error = "EvictCount cannot exceed MaxObjects.";
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
        /// Create a deep copy.
        /// </summary>
        /// <returns>A copy carrying the same values.</returns>
        public ContainerCacheSettings Clone()
        {
            return new ContainerCacheSettings
            {
                Enabled = Enabled,
                Policy = Policy,
                MaxObjectsCeiling = MaxObjectsCeiling,
                MaxObjects = MaxObjects,
                MaxMemoryBytes = MaxMemoryBytes,
                EvictCount = EvictCount,
                MaxCacheableObjectBytes = MaxCacheableObjectBytes
            };
        }

        #endregion
    }
}
