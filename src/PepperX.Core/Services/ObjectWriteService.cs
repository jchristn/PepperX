namespace PepperX.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Caching;
    using PepperX.Core.Database;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Serialization;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage;
    using PepperX.Core.Storage.Format;
    using SyslogLogging;

    /// <summary>
    /// Writes objects. A create with no-overwrite fails when the key exists; otherwise a write is an atomic
    /// replace that tombstones any prior extent (destroyed asynchronously). Metadata updates are performed as
    /// an extent rewrite because extents are immutable.
    /// </summary>
    public sealed class ObjectWriteService
    {
        #region Private-Members

        private readonly PepperXSerializer _Serializer = new PepperXSerializer();
        private readonly IMetadataDatabaseDriver _Db;
        private readonly IExtentStorageDriver _Storage;
        private readonly StorageSettings _StorageSettings;
        private readonly ObjectReadService _ReadService;
        private readonly ObjectDeleteService _DeleteService;
        private readonly ContainerCacheManager _Cache;
        private readonly LoggingModule? _Logging;
        private readonly string _Header = "[ObjectWriteService] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the write service.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="storage">Extent storage driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="readService">Read service (used to stream the payload during a metadata rewrite).</param>
        /// <param name="deleteService">Delete service (used to destroy a replaced extent).</param>
        /// <param name="cache">Per-container cache manager.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public ObjectWriteService(IMetadataDatabaseDriver db, IExtentStorageDriver storage, PepperXSettings settings, ObjectReadService readService, ObjectDeleteService deleteService, ContainerCacheManager cache, LoggingModule? logging = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _StorageSettings = settings.Storage;
            _ReadService = readService ?? throw new ArgumentNullException(nameof(readService));
            _DeleteService = deleteService ?? throw new ArgumentNullException(nameof(deleteService));
            _Cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write an object, creating it or (unless <paramref name="noOverwrite"/> is set) atomically replacing
        /// an existing one.
        /// </summary>
        /// <param name="containerName">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="payload">Payload stream, read to end.</param>
        /// <param name="contentType">Content type, or null.</param>
        /// <param name="labels">Labels, or null.</param>
        /// <param name="tags">Tags, or null.</param>
        /// <param name="metadataObject">Freeform metadata object, or null.</param>
        /// <param name="noOverwrite">When true, fail if the key already exists.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="etagOverride">Optional S3 ETag to persist on the object (used by multipart completion
        /// to store the <c>digest-N</c> ETag, which differs from the content MD5). Null for ordinary writes,
        /// whose S3 ETag is derived from the content MD5.</param>
        /// <returns>The write result.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        /// <exception cref="ObjectAlreadyExistsException">The key exists and no-overwrite was requested.</exception>
        /// <exception cref="ObjectTooLargeException">The payload or a metadata field exceeds a limit.</exception>
        public async Task<ObjectWriteResponse> WriteAsync(
            string containerName,
            string key,
            Stream payload,
            string? contentType,
            List<string>? labels,
            Dictionary<string, string>? tags,
            object? metadataObject,
            bool noOverwrite,
            CancellationToken token = default,
            string? etagOverride = null)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            Container container = await RequireContainerAsync(containerName, token).ConfigureAwait(false);
            ValidateKey(key);
            List<string> normalizedLabels = NormalizeLabels(labels);
            Dictionary<string, string> normalizedTags = tags ?? new Dictionary<string, string>();
            ValidateMetadata(normalizedLabels, normalizedTags, metadataObject);

            ExtentHeader header = BuildHeader(container, key, contentType, normalizedLabels, normalizedTags, metadataObject, etagOverride);

            // Write-through capture (D-goal): tee the payload into memory as it streams to storage, bounded
            // by the container's per-object ceiling, so a within-ceiling write can populate the cache in one
            // pass. A 0 ceiling (no per-object limit) skips capture to bound write-time memory; such objects
            // are cached lazily on first read instead.
            long ceiling = container.Cache.MaxCacheableObjectBytes;
            bool tryCapture = container.Cache.Enabled && ceiling > 0;
            BoundedCaptureStream? capture = tryCapture ? new BoundedCaptureStream(payload, ceiling) : null;

            Extent extent;
            bool replaced;
            byte[]? capturedPayload = null;
            try
            {
                ExtentWriteResult result = await _Storage.WriteAsync(header, capture ?? payload, token).ConfigureAwait(false);

                if (result.SizeBytes > _StorageSettings.MaxObjectBytes)
                {
                    await _Storage.DeleteAsync(result.Location, token).ConfigureAwait(false);
                    throw new ObjectTooLargeException("Payload exceeds the maximum object size of " + _StorageSettings.MaxObjectBytes + " bytes.");
                }

                extent = BuildExtent(container, key, contentType, result, metadataObject != null, normalizedLabels, normalizedTags, header);
                extent.Etag = etagOverride;

                try
                {
                    if (noOverwrite)
                    {
                        await _Db.Extents.CreateAsync(extent, token).ConfigureAwait(false);
                        replaced = false;
                    }
                    else
                    {
                        string? oldId = await ReplaceWithRetryAsync(extent, token).ConfigureAwait(false);
                        replaced = oldId != null;
                        if (oldId != null) FinishOldExtent(oldId);
                    }
                }
                catch (Exception)
                {
                    await SafeDeleteAsync(result.Location).ConfigureAwait(false);
                    throw;
                }

                if (capture != null && !capture.TryGetCapturedPayload(out capturedPayload)) capturedPayload = null;
            }
            finally
            {
                capture?.Dispose();
            }

            PopulateCacheOnWrite(container, key, extent, capturedPayload, metadataObject);
            return BuildResponse(extent, replaced);
        }

        /// <summary>
        /// Update an object's metadata by rewriting the extent with the existing payload and new metadata.
        /// </summary>
        /// <param name="containerName">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="request">Metadata update request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The write result.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        /// <exception cref="ObjectNotFoundException">The object does not exist.</exception>
        public async Task<ObjectWriteResponse> UpdateMetadataAsync(string containerName, string key, UpdateMetadataRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            Container container = await RequireContainerAsync(containerName, token).ConfigureAwait(false);
            Extent? existing = await _Db.Extents.ReadActiveAsync(container.Id, key, token).ConfigureAwait(false);
            if (existing == null) throw new ObjectNotFoundException(containerName, key);

            object? existingObject = null;
            if (existing.HasMetadataObject)
            {
                ExtentHeader oldHeader = await _Storage.ReadHeaderAsync(existing.StorageLocation, token).ConfigureAwait(false);
                existingObject = oldHeader.Object;
            }

            List<string> labels = NormalizeLabels(request.Labels ?? existing.Labels);
            Dictionary<string, string> tags = request.Tags ?? existing.Tags;
            object? metadataObject = request.ClearObject ? null : (request.Object ?? existingObject);
            ValidateMetadata(labels, tags, metadataObject);

            await using (ObjectReadHandle? handle = await _ReadService.ReadAsync(containerName, key, null, null, token).ConfigureAwait(false))
            {
                if (handle == null) throw new ObjectNotFoundException(containerName, key);

                ExtentHeader header = BuildHeader(container, key, existing.ContentType, labels, tags, metadataObject, existing.Etag);

                long ceiling = container.Cache.MaxCacheableObjectBytes;
                bool tryCapture = container.Cache.Enabled && ceiling > 0;
                BoundedCaptureStream? capture = tryCapture ? new BoundedCaptureStream(handle.Payload, ceiling) : null;

                Extent newExtent;
                byte[]? capturedPayload = null;
                try
                {
                    ExtentWriteResult result = await _Storage.WriteAsync(header, (Stream?)capture ?? handle.Payload, token).ConfigureAwait(false);
                    newExtent = BuildExtent(container, key, existing.ContentType, result, metadataObject != null, labels, tags, header);
                    newExtent.Etag = existing.Etag;

                    await handle.DisposeAsync().ConfigureAwait(false);

                    string? oldId;
                    try
                    {
                        oldId = await ReplaceWithRetryAsync(newExtent, token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        await SafeDeleteAsync(result.Location).ConfigureAwait(false);
                        throw;
                    }

                    if (oldId != null) FinishOldExtent(oldId);
                    if (capture != null && !capture.TryGetCapturedPayload(out capturedPayload)) capturedPayload = null;
                }
                finally
                {
                    capture?.Dispose();
                }

                PopulateCacheOnWrite(container, key, newExtent, capturedPayload, metadataObject);
                return BuildResponse(newExtent, true);
            }
        }

        #endregion

        #region Private-Methods

        private void PopulateCacheOnWrite(Container container, string key, Extent extent, byte[]? capturedPayload, object? metadataObject)
        {
            if (!container.Cache.Enabled) return;

            ContainerCache? cache = _Cache.Get(container.Id, container.Cache);
            if (cache == null) return;

            // No usable capture (over-ceiling, or capture skipped for a 0 ceiling): drop any prior entry so a
            // stale extent id never lingers. The D1 coherence check would catch it on read regardless, but
            // dropping keeps the cache tidy.
            if (capturedPayload == null)
            {
                cache.Remove(key);
                return;
            }

            ObjectMetadata metadata = new ObjectMetadata
            {
                Key = extent.Key,
                ExtentId = extent.Id,
                ContainerId = extent.ContainerId,
                ContainerName = container.Name,
                SizeBytes = extent.SizeBytes,
                Sha256 = extent.Sha256,
                Md5 = extent.Md5,
                Etag = extent.Etag,
                ContentType = extent.ContentType,
                Labels = new List<string>(extent.Labels),
                Tags = new Dictionary<string, string>(extent.Tags),
                Object = metadataObject,
                HasMetadataObject = extent.HasMetadataObject,
                CreatedUtc = extent.CreatedUtc
            };

            try
            {
                cache.AddReplace(new CachedObject(key, extent.Id, metadata, capturedPayload));
            }
            catch (Exception ex)
            {
                // Terminal storage already holds the durable write; a cache-insert failure (effectively only
                // OOM) must not fail the acknowledged write.
                _Logging?.Debug(_Header + "cache insert failed for " + container.Id + "/" + key + ": " + ex.Message);
            }
        }

        private async Task<string?> ReplaceWithRetryAsync(Extent extent, CancellationToken token)
        {
            int attempts = _StorageSettings.ReplaceRetryCount + 1;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    return await _Db.Extents.ReplaceAsync(extent, token).ConfigureAwait(false);
                }
                catch (ConcurrentModificationException) when (i < attempts - 1)
                {
                    await Task.Delay(5 * (i + 1), token).ConfigureAwait(false);
                }
            }

            return await _Db.Extents.ReplaceAsync(extent, token).ConfigureAwait(false);
        }

        private void FinishOldExtent(string oldExtentId)
        {
            _ = Task.Run(async () =>
            {
                Extent? old = await _Db.Extents.ReadByIdAsync(oldExtentId, CancellationToken.None).ConfigureAwait(false);
                if (old != null) await _DeleteService.FinishTombstonedAsync(old, CancellationToken.None).ConfigureAwait(false);
            });
        }

        private async Task SafeDeleteAsync(string location)
        {
            try
            {
                await _Storage.DeleteAsync(location, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The janitor removes orphan files.
            }
        }

        private void ValidateKey(string key)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentException("Object key must not be empty.", nameof(key));
            if (Encoding.UTF8.GetByteCount(key) > _StorageSettings.MaxKeyBytes)
                throw new ObjectTooLargeException("Object key exceeds the maximum of " + _StorageSettings.MaxKeyBytes + " bytes.");
        }

        private void ValidateMetadata(List<string> labels, Dictionary<string, string> tags, object? metadataObject)
        {
            if (labels.Count > _StorageSettings.MaxLabels)
                throw new ObjectTooLargeException("Too many labels; the maximum is " + _StorageSettings.MaxLabels + ".");
            foreach (string label in labels)
            {
                if (label.Length > _StorageSettings.MaxLabelLength)
                    throw new ObjectTooLargeException("A label exceeds the maximum length of " + _StorageSettings.MaxLabelLength + ".");
            }
            if (tags.Count > _StorageSettings.MaxTags)
                throw new ObjectTooLargeException("Too many tags; the maximum is " + _StorageSettings.MaxTags + ".");

            if (metadataObject != null)
            {
                string json = _Serializer.SerializeJson(metadataObject) ?? "null";
                if (Encoding.UTF8.GetByteCount(json) > _StorageSettings.MaxMetadataObjectBytes)
                    throw new ObjectTooLargeException("Metadata object exceeds the maximum of " + _StorageSettings.MaxMetadataObjectBytes + " bytes.");
            }
        }

        private static List<string> NormalizeLabels(List<string>? labels)
        {
            List<string> result = new List<string>();
            if (labels == null) return result;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string label in labels)
            {
                if (label == null) continue;
                string trimmed = label.Trim();
                if (trimmed.Length == 0) continue;
                if (seen.Add(trimmed)) result.Add(trimmed);
            }

            return result;
        }

        private static ExtentHeader BuildHeader(Container container, string key, string? contentType, List<string> labels, Dictionary<string, string> tags, object? metadataObject, string? etag)
        {
            return new ExtentHeader
            {
                ExtentId = PepperX.Core.Helpers.IdGenerator.GenerateExtentId(),
                ContainerId = container.Id,
                ContainerName = container.Name,
                Key = key,
                ContentType = contentType,
                Etag = etag,
                Labels = labels,
                Tags = tags,
                Object = metadataObject,
                CreatedUtc = DateTime.UtcNow
            };
        }

        private static Extent BuildExtent(Container container, string key, string? contentType, ExtentWriteResult result, bool hasObject, List<string> labels, Dictionary<string, string> tags, ExtentHeader header)
        {
            return new Extent
            {
                Id = header.ExtentId,
                ContainerId = container.Id,
                Key = key,
                State = PepperX.Core.Enums.ExtentStateEnum.Active,
                SizeBytes = result.SizeBytes,
                Sha256 = result.Sha256,
                Md5 = result.Md5,
                ContentType = contentType,
                StorageDriver = PepperX.Core.Enums.StorageDriverTypeEnum.Disk,
                StorageLocation = result.Location,
                HasMetadataObject = hasObject,
                Labels = labels,
                Tags = tags,
                CreatedUtc = header.CreatedUtc
            };
        }

        private static ObjectWriteResponse BuildResponse(Extent extent, bool replaced)
        {
            return new ObjectWriteResponse
            {
                ExtentId = extent.Id,
                Key = extent.Key,
                ContainerId = extent.ContainerId,
                SizeBytes = extent.SizeBytes,
                Sha256 = extent.Sha256,
                Md5 = extent.Md5,
                ContentType = extent.ContentType,
                Replaced = replaced
            };
        }

        private async Task<Container> RequireContainerAsync(string name, CancellationToken token)
        {
            Container? container = await _Db.Containers.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (container == null) throw new ContainerNotFoundException(name);
            return container;
        }

        #endregion
    }
}
