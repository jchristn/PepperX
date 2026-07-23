namespace PepperX.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Enums;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage;
    using PepperX.Core.Storage.Format;
    using SyslogLogging;

    /// <summary>
    /// Reads object payloads and metadata. In Cluster mode a read holds a database lease (renewed while
    /// streaming) that prevents a concurrent delete from destroying the payload; in Local mode it holds an
    /// in-process read lock. New reads of a tombstoned object fail immediately because the active-extent
    /// lookup no longer matches.
    /// </summary>
    public sealed class ObjectReadService
    {
        #region Private-Members

        private readonly string _Header = "[ObjectReadService] ";
        private readonly IMetadataDatabaseDriver _Db;
        private readonly IExtentStorageDriver _Storage;
        private readonly StorageSettings _StorageSettings;
        private readonly ClusterSettings _Cluster;
        private readonly LocalLockRegistry _LocalLocks;
        private readonly string _NodeId;
        private readonly LoggingModule? _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the read service.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="storage">Extent storage driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="localLocks">Local lock registry (used only in Local coordination mode).</param>
        /// <param name="nodeId">This node's identifier.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public ObjectReadService(IMetadataDatabaseDriver db, IExtentStorageDriver storage, PepperXSettings settings, LocalLockRegistry localLocks, string nodeId, LoggingModule? logging = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _StorageSettings = settings.Storage;
            _Cluster = settings.Cluster;
            _LocalLocks = localLocks ?? throw new ArgumentNullException(nameof(localLocks));
            _NodeId = String.IsNullOrEmpty(nodeId) ? throw new ArgumentNullException(nameof(nodeId)) : nodeId;
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open an object for reading. Returns null when the object does not exist or is being deleted.
        /// </summary>
        /// <param name="containerName">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="offset">Optional payload byte offset for a range read.</param>
        /// <param name="count">Optional byte count for a range read.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A read handle, or null.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        public async Task<ObjectReadHandle?> ReadAsync(string containerName, string key, long? offset, long? count, CancellationToken token = default)
        {
            Container container = await RequireContainerAsync(containerName, token).ConfigureAwait(false);

            if (_Cluster.DeleteCoordinationMode == DeleteCoordinationModeEnum.Local)
            {
                return await ReadLocalAsync(container.Id, key, offset, count, token).ConfigureAwait(false);
            }

            return await ReadClusterAsync(container.Id, key, offset, count, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read an object's metadata, including its labels, tags, and freeform metadata object, without
        /// reading the payload.
        /// </summary>
        /// <param name="containerName">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The object metadata, or null if not found.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        public async Task<ObjectMetadata?> ReadMetadataAsync(string containerName, string key, CancellationToken token = default)
        {
            Container container = await RequireContainerAsync(containerName, token).ConfigureAwait(false);

            Extent? extent = await _Db.Extents.ReadActiveAsync(container.Id, key, token).ConfigureAwait(false);
            if (extent == null) return null;

            ObjectMetadata metadata = ToMetadata(extent, container.Name);

            if (extent.HasMetadataObject)
            {
                try
                {
                    ExtentHeader header = await _Storage.ReadHeaderAsync(extent.StorageLocation, token).ConfigureAwait(false);
                    metadata.Object = header.Object;
                }
                catch (ExtentCorruptException)
                {
                    return null;
                }
                catch (System.IO.FileNotFoundException)
                {
                    return null;
                }
            }

            return metadata;
        }

        /// <summary>
        /// Determine whether an active object exists.
        /// </summary>
        /// <param name="containerName">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        public async Task<bool> ExistsAsync(string containerName, string key, CancellationToken token = default)
        {
            Container? container = await _Db.Containers.ReadByNameAsync(containerName, token).ConfigureAwait(false);
            if (container == null) return false;
            return await _Db.Extents.ExistsActiveAsync(container.Id, key, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<ObjectReadHandle?> ReadClusterAsync(string containerId, string key, long? offset, long? count, CancellationToken token)
        {
            LeaseAcquisition? acquisition = await _Db.ReadLeases.AcquireForActiveExtentAsync(containerId, key, _NodeId, _Cluster.ReadLeaseTtlSeconds, token).ConfigureAwait(false);
            if (acquisition == null) return null;

            Extent extent = acquisition.Extent;
            string leaseId = acquisition.LeaseId;

            try
            {
                ExtentPayloadStream payload = await OpenPayloadAsync(extent, offset, count, token).ConfigureAwait(false);

                int renewMs = Math.Max(1, _Cluster.ReadLeaseTtlSeconds / 2) * 1000;
                Timer timer = new Timer(_ => RenewSafe(leaseId), null, renewMs, renewMs);

                return new ObjectReadHandle(extent, payload, async () =>
                {
                    await timer.DisposeAsync().ConfigureAwait(false);
                    await _Db.ReadLeases.ReleaseAsync(leaseId, CancellationToken.None).ConfigureAwait(false);
                });
            }
            catch (Exception)
            {
                await _Db.ReadLeases.ReleaseAsync(leaseId, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        private async Task<ObjectReadHandle?> ReadLocalAsync(string containerId, string key, long? offset, long? count, CancellationToken token)
        {
            string lockKey = containerId + "\n" + key;
            _LocalLocks.EnterRead(lockKey);

            try
            {
                Extent? extent = await _Db.Extents.ReadActiveAsync(containerId, key, token).ConfigureAwait(false);
                if (extent == null)
                {
                    _LocalLocks.ExitRead(lockKey);
                    return null;
                }

                ExtentPayloadStream payload = await OpenPayloadAsync(extent, offset, count, token).ConfigureAwait(false);
                return new ObjectReadHandle(extent, payload, () =>
                {
                    _LocalLocks.ExitRead(lockKey);
                    return ValueTask.CompletedTask;
                });
            }
            catch (Exception)
            {
                _LocalLocks.ExitRead(lockKey);
                throw;
            }
        }

        private Task<ExtentPayloadStream> OpenPayloadAsync(Extent extent, long? offset, long? count, CancellationToken token)
        {
            if (offset.HasValue)
            {
                long length = count ?? (extent.SizeBytes - offset.Value);
                return _Storage.OpenReadRangeAsync(extent.StorageLocation, offset.Value, length, token);
            }

            return _Storage.OpenReadAsync(extent.StorageLocation, _StorageSettings.VerifyChecksumOnRead, token);
        }

        private void RenewSafe(string leaseId)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _Db.ReadLeases.RenewAsync(leaseId, _Cluster.ReadLeaseTtlSeconds, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging?.Debug(_Header + "lease renewal failed for " + leaseId + ": " + ex.Message);
                }
            });
        }

        private async Task<Container> RequireContainerAsync(string name, CancellationToken token)
        {
            Container? container = await _Db.Containers.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (container == null) throw new ContainerNotFoundException(name);
            return container;
        }

        private static ObjectMetadata ToMetadata(Extent extent, string containerName)
        {
            return new ObjectMetadata
            {
                Key = extent.Key,
                ExtentId = extent.Id,
                ContainerId = extent.ContainerId,
                ContainerName = containerName,
                SizeBytes = extent.SizeBytes,
                Sha256 = extent.Sha256,
                ContentType = extent.ContentType,
                Labels = new System.Collections.Generic.List<string>(extent.Labels),
                Tags = new System.Collections.Generic.Dictionary<string, string>(extent.Tags),
                HasMetadataObject = extent.HasMetadataObject,
                CreatedUtc = extent.CreatedUtc
            };
        }

        #endregion
    }
}
