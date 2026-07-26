namespace PepperX.Core.Caching
{
    using System;
    using PepperX.Core.Enums;
    using PepperX.Core.Models;
    // The local PepperX.Core.Caching namespace shadows the package's top-level Caching namespace, so the
    // package types are reached through this global alias.
    using Pkg = global::Caching;

    /// <summary>
    /// A single container's in-memory cache. Wraps a policy-specific <see cref="Pkg.CacheBase{T1, T2}"/>
    /// (FIFO or LRU) of key to <see cref="CachedObject"/>, bounded by both an object count and an optional
    /// memory ceiling, and records the settings signature it was built from so the manager can detect a
    /// reconfiguration and rebuild.
    /// </summary>
    /// <remarks>
    /// Thread-safety: the underlying cache is thread-safe for the get/add/remove/clear operations used
    /// here, so concurrent readers and writers on one instance are safe. Building, rebuilding, and
    /// disposing an instance are serialized by <see cref="ContainerCacheManager"/>, never by this type.
    /// </remarks>
    public sealed class ContainerCache : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The signature of the settings this cache was built from. When a caller's current settings
        /// produce a different signature, the manager disposes and rebuilds the cache.
        /// </summary>
        public string Signature
        {
            get
            {
                return _Signature;
            }
        }

        /// <summary>
        /// Objects whose estimated size exceeds this are never admitted; 0 means no ceiling.
        /// </summary>
        public long MaxCacheableObjectBytes
        {
            get
            {
                return _MaxCacheableObjectBytes;
            }
        }

        #endregion

        #region Private-Members

        private readonly Pkg.CacheBase<string, CachedObject> _Cache;
        private readonly string _Signature;
        private readonly long _MaxCacheableObjectBytes;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build a cache from the supplied settings, which must have <see cref="ContainerCacheSettings.Enabled"/>
        /// set. Capacity comes from <see cref="ContainerCacheSettings.MaxObjects"/>, the eviction batch from
        /// <see cref="ContainerCacheSettings.EvictCount"/>, and the memory bound from
        /// <see cref="ContainerCacheSettings.MaxMemoryBytes"/>; entry size is estimated by
        /// <see cref="CachedObject.SizeBytes"/>.
        /// </summary>
        /// <param name="settings">Cache settings (already clamped).</param>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        public ContainerCache(ContainerCacheSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            _Signature = settings.BuildSignature();
            _MaxCacheableObjectBytes = settings.MaxCacheableObjectBytes;

            Pkg.CacheBase<string, CachedObject> cache = settings.Policy == CacheEvictionPolicyEnum.FIFO
                ? new Pkg.FIFOCache<string, CachedObject>(settings.MaxObjects, settings.EvictCount, StringComparer.Ordinal)
                : new Pkg.LRUCache<string, CachedObject>(settings.MaxObjects, settings.EvictCount, StringComparer.Ordinal);

            cache.MaxMemoryBytes = settings.MaxMemoryBytes;
            cache.SizeEstimator = EstimateSize;
            _Cache = cache;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Try to read an entry.
        /// </summary>
        /// <param name="key">Object key.</param>
        /// <param name="value">The cached entry when found; otherwise null.</param>
        /// <returns>True on a hit.</returns>
        public bool TryGet(string key, out CachedObject? value)
        {
            value = null;
            if (String.IsNullOrEmpty(key) || _Disposed) return false;

            try
            {
                return _Cache.TryGet(key, out value!);
            }
            catch (ObjectDisposedException)
            {
                // The manager disposed this cache concurrently (container removed/reconfigured). Degrade to a
                // miss so the caller cleanly bypasses to storage rather than seeing a disposed-cache error.
                value = null;
                return false;
            }
        }

        /// <summary>
        /// Add or replace an entry. An entry whose estimated size exceeds
        /// <see cref="MaxCacheableObjectBytes"/> (when that is non-zero) is silently not admitted, so an
        /// over-ceiling object never displaces smaller cacheable objects.
        /// </summary>
        /// <param name="entry">The entry to cache.</param>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
        public void AddReplace(CachedObject entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (_Disposed) return;
            if (_MaxCacheableObjectBytes > 0 && entry.SizeBytes > _MaxCacheableObjectBytes) return;

            try
            {
                _Cache.AddReplace(entry.Key, entry);
            }
            catch (ObjectDisposedException)
            {
                // Disposed concurrently; the durable write already succeeded, so silently drop the insert.
            }
        }

        /// <summary>
        /// Remove an entry if present.
        /// </summary>
        /// <param name="key">Object key.</param>
        public void Remove(string key)
        {
            if (String.IsNullOrEmpty(key) || _Disposed) return;

            try
            {
                _Cache.Remove(key);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Remove every entry.
        /// </summary>
        public void Clear()
        {
            if (_Disposed) return;

            try
            {
                _Cache.Clear();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Current runtime statistics (hits, misses, evictions, resident count and memory). Returns an empty
        /// snapshot if the cache was disposed concurrently.
        /// </summary>
        /// <returns>A snapshot of statistics.</returns>
        public Pkg.CacheStatistics Statistics()
        {
            if (_Disposed) return new Pkg.CacheStatistics();

            try
            {
                return _Cache.GetStatistics();
            }
            catch (ObjectDisposedException)
            {
                return new Pkg.CacheStatistics();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private static long EstimateSize(CachedObject entry)
        {
            return entry == null ? 0 : entry.SizeBytes;
        }

        private void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _Cache.Dispose();
            }
            _Disposed = true;
        }

        #endregion
    }
}
