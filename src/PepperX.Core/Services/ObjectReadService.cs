namespace PepperX.Core.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Caching;
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

        private const int _ReadRaceRetryCount = 5;
        private const int _ReadRaceRetryDelayMs = 10;
        private readonly string _Header = "[ObjectReadService] ";
        private readonly IMetadataDatabaseDriver _Db;
        private readonly IExtentStorageDriver _Storage;
        private readonly StorageSettings _StorageSettings;
        private readonly ClusterSettings _Cluster;
        private readonly LocalLockRegistry _LocalLocks;
        private readonly ContainerCacheManager _Cache;
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
        /// <param name="cache">Per-container cache manager.</param>
        /// <param name="nodeId">This node's identifier.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public ObjectReadService(IMetadataDatabaseDriver db, IExtentStorageDriver storage, PepperXSettings settings, LocalLockRegistry localLocks, ContainerCacheManager cache, string nodeId, LoggingModule? logging = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _StorageSettings = settings.Storage;
            _Cluster = settings.Cluster;
            _LocalLocks = localLocks ?? throw new ArgumentNullException(nameof(localLocks));
            _Cache = cache ?? throw new ArgumentNullException(nameof(cache));
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

            if (container.Cache.Enabled)
            {
                return await ReadWithCacheAsync(container, key, offset, count, token).ConfigureAwait(false);
            }

            return await ReadUncachedAsync(container.Id, key, offset, count, token).ConfigureAwait(false);
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

            // Cache path (D1/D4): a validated hit serves metadata (including the freeform object) from
            // memory without touching storage. A metadata-only miss does not hydrate the payload cache.
            if (container.Cache.Enabled)
            {
                ContainerCache? cache = _Cache.Get(container.Id, container.Cache);
                if (extent == null)
                {
                    cache?.Remove(key);
                    return null;
                }
                if (cache != null && cache.TryGet(key, out CachedObject? entry) && entry != null
                    && String.Equals(entry.ExtentId, extent.Id, StringComparison.Ordinal))
                {
                    return CloneMetadata(entry.Metadata);
                }
                cache?.Remove(key);
            }

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

        private async Task<ObjectReadHandle?> ReadUncachedAsync(string containerId, string key, long? offset, long? count, CancellationToken token)
        {
            // Under heavy replace churn a read can lease the active extent and then find its file already
            // destroyed in the narrow window between a concurrent replace's drain check and its delete. The
            // object still exists as a newer extent, so re-resolve and retry a bounded number of times;
            // last-writer-wins means returning the current version is the correct outcome. A genuinely
            // deleted object resolves to no active extent and returns null without throwing.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (_Cluster.DeleteCoordinationMode == DeleteCoordinationModeEnum.Local)
                    {
                        return await ReadLocalAsync(containerId, key, offset, count, token).ConfigureAwait(false);
                    }

                    return await ReadClusterAsync(containerId, key, offset, count, token).ConfigureAwait(false);
                }
                catch (Exception ex) when ((ex is IOException || ex is ExtentCorruptException) && attempt < _ReadRaceRetryCount)
                {
                    await Task.Delay(_ReadRaceRetryDelayMs, token).ConfigureAwait(false);
                }
            }
        }

        private async Task<ObjectReadHandle?> ReadWithCacheAsync(Container container, string key, long? offset, long? count, CancellationToken token)
        {
            ContainerCache? cache = _Cache.Get(container.Id, container.Cache);
            if (cache == null) return await ReadUncachedAsync(container.Id, key, offset, count, token).ConfigureAwait(false);

            Extent? active = await _Db.Extents.ReadActiveAsync(container.Id, key, token).ConfigureAwait(false);
            if (active == null)
            {
                cache.Remove(key);
                return null;
            }

            // HIT: cached entry still refers to the active extent (D1). Serve from memory, no lease (D2).
            if (cache.TryGet(key, out CachedObject? entry) && entry != null
                && String.Equals(entry.ExtentId, active.Id, StringComparison.Ordinal))
            {
                byte[] slice = SlicePayload(entry.Payload, offset, count);
                ExtentPayloadStream hitStream = ExtentPayloadStream.FromMemory(slice, BuildHeader(active, container.Name, entry.Metadata));
                return new ObjectReadHandle(active, hitStream, static () => ValueTask.CompletedTask);
            }

            // MISS or stale: drop any stale entry, then take the normal lease-guarded path.
            cache.Remove(key);
            ObjectReadHandle? handle = await ReadUncachedAsync(container.Id, key, offset, count, token).ConfigureAwait(false);
            if (handle == null) return null;

            // Hydrate and serve from the extent the bytes actually came from (handle.Extent, the leased and
            // opened extent), NOT the earlier ReadActiveAsync snapshot: a concurrent replace can move the
            // active extent between the two lookups, and caching one extent's bytes under another's id would
            // violate coherence. Only a full read of a within-ceiling object hydrates the cache; ranges and
            // over-ceiling objects stream straight through.
            Extent served = handle.Extent;
            long ceiling = container.Cache.MaxCacheableObjectBytes;
            bool cacheable = !offset.HasValue && (ceiling <= 0 || served.SizeBytes <= ceiling);
            if (!cacheable) return handle;

            ObjectMetadata metadata = ToMetadata(served, container.Name);
            metadata.Object = handle.Payload.Header.Object;

            byte[] bytes;
            await using (handle.ConfigureAwait(false))
            {
                bytes = await DrainAsync(handle.Payload, served.SizeBytes, token).ConfigureAwait(false);
            }

            try
            {
                cache.AddReplace(new CachedObject(key, served.Id, metadata, bytes));
            }
            catch (Exception ex)
            {
                // Terminal storage already served the durable bytes; a cache-insert failure (effectively
                // only OOM) must not fail the read.
                _Logging?.Debug(_Header + "cache insert failed for " + container.Id + "/" + key + ": " + ex.Message);
            }

            ExtentPayloadStream memStream = ExtentPayloadStream.FromMemory(bytes, BuildHeader(served, container.Name, metadata));
            return new ObjectReadHandle(served, memStream, static () => ValueTask.CompletedTask);
        }

        private static byte[] SlicePayload(byte[] payload, long? offset, long? count)
        {
            if (!offset.HasValue) return payload;

            long start = Math.Clamp(offset.Value, 0, payload.LongLength);
            long length = count ?? (payload.LongLength - start);
            if (length < 0) length = 0;
            if (start + length > payload.LongLength) length = payload.LongLength - start;

            byte[] slice = new byte[length];
            Array.Copy(payload, start, slice, 0, length);
            return slice;
        }

        private static async Task<byte[]> DrainAsync(Stream source, long expectedLength, CancellationToken token)
        {
            using (MemoryStream ms = new MemoryStream(expectedLength > 0 && expectedLength <= int.MaxValue ? (int)expectedLength : 0))
            {
                await source.CopyToAsync(ms, token).ConfigureAwait(false);
                return ms.ToArray();
            }
        }

        private static ExtentHeader BuildHeader(Extent extent, string containerName, ObjectMetadata metadata)
        {
            return new ExtentHeader
            {
                ExtentId = extent.Id,
                ContainerId = extent.ContainerId,
                ContainerName = containerName,
                Key = extent.Key,
                ContentType = extent.ContentType,
                SizeBytes = extent.SizeBytes,
                Sha256 = extent.Sha256,
                Object = metadata.Object,
                CreatedUtc = extent.CreatedUtc
            };
        }

        private static ObjectMetadata CloneMetadata(ObjectMetadata source)
        {
            return new ObjectMetadata
            {
                Key = source.Key,
                ExtentId = source.ExtentId,
                ContainerId = source.ContainerId,
                ContainerName = source.ContainerName,
                SizeBytes = source.SizeBytes,
                Sha256 = source.Sha256,
                Md5 = source.Md5,
                Etag = source.Etag,
                ContentType = source.ContentType,
                Labels = new System.Collections.Generic.List<string>(source.Labels),
                Tags = new System.Collections.Generic.Dictionary<string, string>(source.Tags),
                Object = source.Object,
                HasMetadataObject = source.HasMetadataObject,
                CreatedUtc = source.CreatedUtc
            };
        }

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
                Md5 = extent.Md5,
                Etag = extent.Etag,
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
