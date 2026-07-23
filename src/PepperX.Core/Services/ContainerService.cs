namespace PepperX.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Enumeration;
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

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the container service.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="storage">Extent storage driver.</param>
        /// <param name="deleteService">Object delete service (used for forced container deletion).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ContainerService(IMetadataDatabaseDriver db, IExtentStorageDriver storage, ObjectDeleteService deleteService)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _DeleteService = deleteService ?? throw new ArgumentNullException(nameof(deleteService));
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

            await _Db.Containers.CreateAsync(container, token).ConfigureAwait(false);
            await _Storage.WriteContainerManifestAsync(ToManifest(container), token).ConfigureAwait(false);

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

            await _Db.Containers.DeleteAsync(container.Id, token).ConfigureAwait(false);
            await _Storage.DeleteContainerAsync(container.Id, token).ConfigureAwait(false);
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
                CreatedUtc = container.CreatedUtc
            };
        }

        #endregion
    }
}
