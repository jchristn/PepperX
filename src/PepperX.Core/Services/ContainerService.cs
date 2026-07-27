namespace PepperX.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Caching;
    using PepperX.Core.Database;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Enums;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Storage;

    /// <summary>
    /// Coordinates container operations across the metadata database and extent storage, including the
    /// container manifest that keeps container tags recoverable during rehydration.
    /// </summary>
    public sealed class ContainerService
    {
        #region Private-Members

        private readonly IMetadataDatabaseDriver _Db;
        private readonly IExtentStorageDriver _Storage;
        private readonly ObjectDeleteService _DeleteService;
        private readonly ContainerCacheManager _Cache;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the container service.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="storage">Extent storage driver.</param>
        /// <param name="deleteService">Object delete service (used for forced container deletion).</param>
        /// <param name="cache">Per-container cache manager.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ContainerService(IMetadataDatabaseDriver db, IExtentStorageDriver storage, ObjectDeleteService deleteService, ContainerCacheManager cache)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _DeleteService = deleteService ?? throw new ArgumentNullException(nameof(deleteService));
            _Cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a container.
        /// </summary>
        /// <param name="request">Create request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created container.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
        public async Task<ContainerResponse> CreateAsync(ContainerCreateRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            Container container = new Container { Name = request.Name };
            if (request.Tags != null) container.Tags = request.Tags;

            // Cache defaults (D9): when the caller supplies no cache settings, a new container gets caching
            // enabled (LRU) with reasonable sizes; an explicit block (including one that disables caching)
            // is honored as-is after re-clamping.
            container.Cache = request.Cache != null
                ? request.Cache.ToSettings()
                : ContainerCacheSettings.CreationDefault();

            if (request.RespDatabaseIndex.HasValue)
            {
                if (request.RespDatabaseIndex.Value < 0)
                {
                    throw new PepperXException(ApiErrorEnum.BadRequest, 400, "RespDatabaseIndex cannot be negative.");
                }
                container.RespDatabaseIndex = request.RespDatabaseIndex.Value;
            }

            // Optional per-container multipart-upload expiry; the model setter clamps to 1..365 and coerces
            // anything below 1 to null (inherit the system-wide default).
            container.MultipartUploadExpiryDays = request.MultipartUploadExpiryDays;

            await _Db.Containers.CreateAsync(container, token).ConfigureAwait(false);
            await _Storage.WriteContainerManifestAsync(ToManifest(container), token).ConfigureAwait(false);
            _Cache.Configure(container.Id, container.Cache);

            return ContainerResponse.FromModel(container);
        }

        /// <summary>
        /// Read a container by name.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The container, or null if not found.</returns>
        public async Task<ContainerResponse?> ReadAsync(string name, CancellationToken token = default)
        {
            Container? container = await _Db.Containers.ReadByNameAsync(name, token).ConfigureAwait(false);
            return container == null ? null : ContainerResponse.FromModel(container);
        }

        /// <summary>
        /// Determine whether a container exists.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        public Task<bool> ExistsAsync(string name, CancellationToken token = default)
        {
            return _Db.Containers.ExistsAsync(name, token);
        }

        /// <summary>
        /// Enumerate containers.
        /// </summary>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of containers.</returns>
        public async Task<EnumerationResult<ContainerResponse>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<Container> page = await _Db.Containers.EnumerateAsync(query, token).ConfigureAwait(false);
            List<ContainerResponse> mapped = new List<ContainerResponse>();
            foreach (Container container in page.Objects) mapped.Add(ContainerResponse.FromModel(container));

            return new EnumerationResult<ContainerResponse>
            {
                Success = page.Success,
                StartUtc = page.StartUtc,
                EndUtc = page.EndUtc,
                MaxResults = page.MaxResults,
                ContinuationToken = page.ContinuationToken,
                EndOfResults = page.EndOfResults,
                TotalRecords = page.TotalRecords,
                RecordsRemaining = page.RecordsRemaining,
                Objects = mapped
            };
        }

        /// <summary>
        /// Replace a container's tags.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="tags">New tag set.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated container.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        public async Task<ContainerResponse> UpdateTagsAsync(string name, Dictionary<string, string> tags, CancellationToken token = default)
        {
            Container container = await RequireAsync(name, token).ConfigureAwait(false);
            Container? updated = await _Db.Containers.UpdateTagsAsync(container.Id, tags ?? new Dictionary<string, string>(), token).ConfigureAwait(false);
            if (updated == null) throw new ContainerNotFoundException(name);

            await _Storage.WriteContainerManifestAsync(ToManifest(updated), token).ConfigureAwait(false);
            return ContainerResponse.FromModel(updated);
        }

        /// <summary>
        /// Assign or clear a container's RESP database index. Passing null clears it. The index must be
        /// unique across containers; a conflict is reported as 409. Reachability over RESP additionally
        /// requires the index to be within <c>Resp.DatabaseCount</c> on the serving node.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="respDatabaseIndex">The index to claim, or null to clear.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated container.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        /// <exception cref="PepperXException">The index is negative (400) or already claimed (409).</exception>
        public async Task<ContainerResponse> SetRespDatabaseIndexAsync(string name, int? respDatabaseIndex, CancellationToken token = default)
        {
            Container container = await RequireAsync(name, token).ConfigureAwait(false);

            if (respDatabaseIndex.HasValue && respDatabaseIndex.Value < 0)
            {
                throw new PepperXException(ApiErrorEnum.BadRequest, 400, "RespDatabaseIndex cannot be negative.");
            }

            Container? updated = await _Db.Containers.UpdateRespDatabaseIndexAsync(container.Id, respDatabaseIndex, token).ConfigureAwait(false);
            if (updated == null) throw new ContainerNotFoundException(name);

            await _Storage.WriteContainerManifestAsync(ToManifest(updated), token).ConfigureAwait(false);
            return ContainerResponse.FromModel(updated);
        }

        /// <summary>
        /// Set or clear a container's per-container multipart-upload expiry (in days). Passing null clears
        /// the override so the container inherits the system-wide <c>S3.MultipartUploadExpiryDays</c>. A
        /// provided value is clamped to 1..365. The new window applies to uploads initiated after the
        /// change; uploads already in progress keep the expiry stamped when they started.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="days">The expiry in days, or null to clear the override.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated container.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        public async Task<ContainerResponse> SetMultipartExpiryAsync(string name, int? days, CancellationToken token = default)
        {
            Container container = await RequireAsync(name, token).ConfigureAwait(false);

            // Normalize through the model setter's clamp (1..365; below 1 => null/inherit) before persisting.
            container.MultipartUploadExpiryDays = days;

            Container? updated = await _Db.Containers.UpdateMultipartExpiryAsync(container.Id, container.MultipartUploadExpiryDays, token).ConfigureAwait(false);
            if (updated == null) throw new ContainerNotFoundException(name);

            await _Storage.WriteContainerManifestAsync(ToManifest(updated), token).ConfigureAwait(false);
            return ContainerResponse.FromModel(updated);
        }

        /// <summary>
        /// Read a container's cache settings together with this node's live cache statistics.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The cache settings and statistics.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        public async Task<ContainerCacheResponse> ReadCacheAsync(string name, CancellationToken token = default)
        {
            Container container = await RequireAsync(name, token).ConfigureAwait(false);
            return ContainerCacheResponse.FromSettingsAndStatistics(container.Cache, _Cache.Statistics(container.Id));
        }

        /// <summary>
        /// Replace a container's cache settings, persist them, and apply them to the live cache. When the
        /// request is internally inconsistent (for example an eviction count greater than the object
        /// maximum), a 400 is raised rather than silently normalizing beyond the per-field clamps.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="request">The new cache settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The applied settings and live statistics.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        /// <exception cref="PepperXException">The request is invalid.</exception>
        public async Task<ContainerCacheResponse> UpdateCacheSettingsAsync(string name, UpdateCacheSettingsRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            Container container = await RequireAsync(name, token).ConfigureAwait(false);

            // Reject a clearly-invalid request pre-clamp with a 400; the settings' own setters then re-clamp
            // as a second line of defense for anything that slips through (e.g. DB-loaded legacy rows).
            if (!request.Validate(out string? error))
            {
                throw new PepperXException(ApiErrorEnum.BadRequest, 400, error ?? "Invalid cache settings.");
            }

            ContainerCacheSettings settings = request.ToSettings();
            Container? updated = await _Db.Containers.UpdateCacheSettingsAsync(container.Id, settings, token).ConfigureAwait(false);
            if (updated == null) throw new ContainerNotFoundException(name);

            await _Storage.WriteContainerManifestAsync(ToManifest(updated), token).ConfigureAwait(false);
            _Cache.Configure(updated.Id, updated.Cache);

            return ContainerCacheResponse.FromSettingsAndStatistics(updated.Cache, _Cache.Statistics(updated.Id));
        }

        /// <summary>
        /// Delete a container. When it still holds objects, deletion requires <paramref name="force"/>, which
        /// deletes the container's contents first.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="force">Whether to delete a non-empty container's contents.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        /// <exception cref="ContainerNotEmptyException">The container is not empty and force was not requested.</exception>
        public async Task DeleteAsync(string name, bool force, CancellationToken token = default)
        {
            Container container = await RequireAsync(name, token).ConfigureAwait(false);

            if (container.ObjectCount > 0)
            {
                if (!force) throw new ContainerNotEmptyException(name, container.ObjectCount);
                await _DeleteService.BulkDeleteContainerAsync(container.Id, token).ConfigureAwait(false);
            }

            // Purge in-progress multipart uploads (rows + staged parts) so their foreign key does not block
            // the container delete. The staged blobs are removed best-effort; the janitor reclaims any that
            // survive a failure here.
            IReadOnlyList<string> uploads = await _Db.MultipartUploads.DeleteByContainerAsync(container.Id, token).ConfigureAwait(false);
            foreach (string uploadId in uploads)
            {
                try
                {
                    await _Storage.DeletePartsAsync(uploadId, token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best-effort; CleanupOrphanedPartsAsync reclaims staged parts whose upload row is gone.
                }
            }

            await _Db.Containers.DeleteAsync(container.Id, token).ConfigureAwait(false);
            await _Storage.DeleteContainerAsync(container.Id, token).ConfigureAwait(false);
            _Cache.Remove(container.Id);
        }

        /// <summary>
        /// Resolve a container by name or throw when it does not exist.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The container.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        public async Task<Container> RequireAsync(string name, CancellationToken token = default)
        {
            Container? container = await _Db.Containers.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (container == null) throw new ContainerNotFoundException(name);
            return container;
        }

        #endregion

        #region Private-Methods

        private static ContainerManifest ToManifest(Container container)
        {
            return new ContainerManifest
            {
                Id = container.Id,
                Name = container.Name,
                Tags = new Dictionary<string, string>(container.Tags),
                CreatedUtc = container.CreatedUtc,
                RespDatabaseIndex = container.RespDatabaseIndex,
                MultipartUploadExpiryDays = container.MultipartUploadExpiryDays,
                Cache = container.Cache.Clone()
            };
        }

        #endregion
    }
}
