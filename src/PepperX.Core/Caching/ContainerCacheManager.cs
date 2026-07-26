namespace PepperX.Core.Caching
{
    using System;
    using System.Collections.Concurrent;
    using PepperX.Core.Models;
    using SyslogLogging;
    // The local PepperX.Core.Caching namespace shadows the package's top-level Caching namespace, so the
    // package types are reached through this global alias.
    using Pkg = global::Caching;

    /// <summary>
    /// Owns every container's live <see cref="ContainerCache"/>, keyed by container id. Builds a cache
    /// lazily on first use, rebuilds it when a container's settings change shape, and disposes it when
    /// caching is disabled or the container is removed. A node holds at most one cache per container.
    /// </summary>
    /// <remarks>
    /// Thread-safety: the backing map is a <see cref="ConcurrentDictionary{TKey, TValue}"/> and every
    /// build/rebuild/dispose transition for a given container id is serialized under a per-container lock,
    /// so concurrent callers for the same container never race to construct or dispose an instance. Reads
    /// and writes against a returned <see cref="ContainerCache"/> are themselves thread-safe.
    /// </remarks>
    public sealed class ContainerCacheManager : IDisposable
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, ContainerCache> _Caches = new ConcurrentDictionary<string, ContainerCache>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, object> _Locks = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        private readonly LoggingModule? _Logging;
        private readonly string _Header = "[ContainerCacheManager] ";
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the manager.
        /// </summary>
        /// <param name="logging">Optional logging module for build/rebuild/dispose diagnostics. May be null.</param>
        public ContainerCacheManager(LoggingModule? logging = null)
        {
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Return the live cache for a container, building it on first use and rebuilding it when the
        /// supplied settings differ in shape from the built instance. Returns null when
        /// <see cref="ContainerCacheSettings.Enabled"/> is false, disposing any existing instance so a
        /// disable takes effect immediately.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="settings">The container's current cache settings.</param>
        /// <returns>The cache, or null when caching is disabled.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public ContainerCache? Get(string containerId, ContainerCacheSettings settings)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (_Disposed) return null;

            if (!settings.Enabled)
            {
                Remove(containerId);
                return null;
            }

            string signature = settings.BuildSignature();

            if (_Caches.TryGetValue(containerId, out ContainerCache? existing) && String.Equals(existing.Signature, signature, StringComparison.Ordinal))
            {
                return existing;
            }

            lock (LockFor(containerId))
            {
                if (_Caches.TryGetValue(containerId, out ContainerCache? current))
                {
                    if (String.Equals(current.Signature, signature, StringComparison.Ordinal)) return current;
                    current.Dispose();
                    _Logging?.Debug(_Header + "rebuilding cache for container " + containerId + " (settings changed)");
                }
                else
                {
                    _Logging?.Debug(_Header + "building cache for container " + containerId);
                }

                ContainerCache built = new ContainerCache(settings);
                _Caches[containerId] = built;
                return built;
            }
        }

        /// <summary>
        /// Return the existing cache for a container, or null. Never builds — used by paths (such as
        /// delete) that must not create a cache merely to touch it.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <returns>The existing cache, or null.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="containerId"/> is null.</exception>
        public ContainerCache? Get(string containerId)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));
            return _Caches.TryGetValue(containerId, out ContainerCache? existing) ? existing : null;
        }

        /// <summary>
        /// Apply settings explicitly: build/rebuild when enabled, or dispose and drop when disabled. Called
        /// when a container's cache settings are changed through the API.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="settings">New settings.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public void Configure(string containerId, ContainerCacheSettings settings)
        {
            Get(containerId, settings);
        }

        /// <summary>
        /// Dispose and drop a container's cache if present. Safe to call when no cache exists.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <exception cref="ArgumentNullException"><paramref name="containerId"/> is null.</exception>
        public void Remove(string containerId)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));

            lock (LockFor(containerId))
            {
                if (_Caches.TryRemove(containerId, out ContainerCache? removed))
                {
                    removed.Dispose();
                    _Logging?.Debug(_Header + "removed cache for container " + containerId);
                }
            }
        }

        /// <summary>
        /// Current runtime statistics for a container's cache, or null when no cache exists on this node.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <returns>Statistics, or null.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="containerId"/> is null.</exception>
        public Pkg.CacheStatistics? Statistics(string containerId)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));
            return _Caches.TryGetValue(containerId, out ContainerCache? existing) ? existing.Statistics() : null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            foreach (ContainerCache cache in _Caches.Values)
            {
                cache.Dispose();
            }
            _Caches.Clear();
            _Locks.Clear();
        }

        #endregion

        #region Private-Methods

        private object LockFor(string containerId)
        {
            return _Locks.GetOrAdd(containerId, static _ => new object());
        }

        #endregion
    }
}
