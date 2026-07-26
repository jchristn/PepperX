namespace PepperX.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Caching;
    using PepperX.Core.Database;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Enums;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage;
    using SyslogLogging;

    /// <summary>
    /// Deletes objects while guaranteeing that a delete waits for in-flight reads before destroying a
    /// payload. In Cluster mode this is enforced by draining database read leases; in Local mode by an
    /// in-process writer lock. In both modes the database tombstone blocks new reads the moment it commits.
    /// </summary>
    public sealed class ObjectDeleteService
    {
        #region Private-Members

        private readonly string _Header = "[ObjectDeleteService] ";
        private readonly IMetadataDatabaseDriver _Db;
        private readonly IExtentStorageDriver _Storage;
        private readonly ClusterSettings _Cluster;
        private readonly LocalLockRegistry _LocalLocks;
        private readonly ContainerCacheManager _Cache;
        private readonly LoggingModule? _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the delete service.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="storage">Extent storage driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="localLocks">Local lock registry (used only in Local coordination mode).</param>
        /// <param name="cache">Per-container cache manager.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public ObjectDeleteService(IMetadataDatabaseDriver db, IExtentStorageDriver storage, PepperXSettings settings, LocalLockRegistry localLocks, ContainerCacheManager cache, LoggingModule? logging = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _Cluster = settings.Cluster;
            _LocalLocks = localLocks ?? throw new ArgumentNullException(nameof(localLocks));
            _Cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Delete an object by container name and key. Blocks until in-flight reads finish before destroying
        /// the payload.
        /// </summary>
        /// <param name="containerName">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if an object was deleted; false if it did not exist.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        public async Task<bool> DeleteAsync(string containerName, string key, CancellationToken token = default)
        {
            Container? container = await _Db.Containers.ReadByNameAsync(containerName, token).ConfigureAwait(false);
            if (container == null) throw new ContainerNotFoundException(containerName);

            return await DeleteByContainerIdAsync(container.Id, key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete an object by container identifier and key.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if an object was deleted; false if it did not exist.</returns>
        public async Task<bool> DeleteByContainerIdAsync(string containerId, string key, CancellationToken token = default)
        {
            // Delete from cache first (D-goal): drop the entry before tombstoning so a same-node read cannot
            // serve a soon-to-be-destroyed payload. Other nodes self-heal via the D1 coherence check.
            _Cache.Get(containerId)?.Remove(key);

            if (_Cluster.DeleteCoordinationMode == DeleteCoordinationModeEnum.Local)
            {
                string lockKey = LockKey(containerId, key);
                _LocalLocks.EnterWrite(lockKey);
                try
                {
                    Extent? tomb = await _Db.Extents.MarkDeletingAsync(containerId, key, token).ConfigureAwait(false);
                    if (tomb == null) return false;
                    await DestroyAndPurgeAsync(tomb, token).ConfigureAwait(false);
                    return true;
                }
                finally
                {
                    _LocalLocks.ExitWrite(lockKey);
                }
            }

            Extent? tombstoned = await _Db.Extents.MarkDeletingAsync(containerId, key, token).ConfigureAwait(false);
            if (tombstoned == null) return false;

            bool drained = await DrainAsync(tombstoned.Id, token).ConfigureAwait(false);
            if (drained) await DestroyAndPurgeAsync(tombstoned, token).ConfigureAwait(false);
            else _Logging?.Warn(_Header + "extent " + tombstoned.Id + " did not drain within the timeout; the janitor will finish it");
            return true;
        }

        /// <summary>
        /// Finish destroying an already-tombstoned extent (used after an atomic replace and by the janitor).
        /// </summary>
        /// <param name="extent">The tombstoned extent.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="extent"/> is null.</exception>
        public async Task FinishTombstonedAsync(Extent extent, CancellationToken token = default)
        {
            if (extent == null) throw new ArgumentNullException(nameof(extent));

            if (_Cluster.DeleteCoordinationMode == DeleteCoordinationModeEnum.Local)
            {
                string lockKey = LockKey(extent.ContainerId, extent.Key);
                _LocalLocks.EnterWrite(lockKey);
                try
                {
                    await DestroyAndPurgeAsync(extent, token).ConfigureAwait(false);
                }
                finally
                {
                    _LocalLocks.ExitWrite(lockKey);
                }
                return;
            }

            bool drained = await DrainAsync(extent.Id, token).ConfigureAwait(false);
            if (drained) await DestroyAndPurgeAsync(extent, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete every object in a container.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of objects deleted.</returns>
        public async Task<int> BulkDeleteContainerAsync(string containerId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));

            // Clear the whole container cache up front; per-key deletes below also evict individually.
            _Cache.Get(containerId)?.Clear();

            int deleted = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                EnumerationQuery query = new EnumerationQuery { MaxResults = 100 };
                EnumerationResult<Extent> page = await _Db.Extents.EnumerateAsync(containerId, query, token).ConfigureAwait(false);
                if (page.Objects.Count == 0) break;

                foreach (Extent extent in page.Objects)
                {
                    if (await DeleteByContainerIdAsync(containerId, extent.Key, token).ConfigureAwait(false)) deleted++;
                }
            }

            return deleted;
        }

        #endregion

        #region Private-Methods

        private async Task<bool> DrainAsync(string extentId, CancellationToken token)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            TimeSpan timeout = TimeSpan.FromSeconds(_Cluster.DeleteDrainTimeoutSeconds);

            while (true)
            {
                token.ThrowIfCancellationRequested();
                long active = await _Db.ReadLeases.CountActiveForExtentAsync(extentId, token).ConfigureAwait(false);
                if (active == 0) return true;
                if (stopwatch.Elapsed >= timeout) return false;
                await Task.Delay(_Cluster.DeleteDrainPollMs, token).ConfigureAwait(false);
            }
        }

        private async Task DestroyAndPurgeAsync(Extent extent, CancellationToken token)
        {
            await _Storage.DeleteAsync(extent.StorageLocation, token).ConfigureAwait(false);
            await _Db.Extents.PurgeAsync(extent.Id, token).ConfigureAwait(false);
        }

        private static string LockKey(string containerId, string key)
        {
            return containerId + "\n" + key;
        }

        #endregion
    }
}
