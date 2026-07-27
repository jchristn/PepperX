namespace PepperX.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Responses;

    /// <summary>
    /// Data access for containers.
    /// </summary>
    public interface IContainerMethods
    {
        /// <summary>
        /// Create a container.
        /// </summary>
        /// <param name="container">Container to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created container.</returns>
        Task<Container> CreateAsync(Container container, CancellationToken token = default);

        /// <summary>
        /// Read a container by identifier.
        /// </summary>
        /// <param name="id">Container identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The container, or null if not found.</returns>
        Task<Container?> ReadByIdAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a container by name.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The container, or null if not found.</returns>
        Task<Container?> ReadByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Determine whether a container exists by name.
        /// </summary>
        /// <param name="name">Container name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if it exists.</returns>
        Task<bool> ExistsAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Enumerate containers with filtering and pagination.
        /// </summary>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of containers.</returns>
        Task<EnumerationResult<Container>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Replace a container's tags.
        /// </summary>
        /// <param name="id">Container identifier.</param>
        /// <param name="tags">New tag set.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated container, or null if not found.</returns>
        Task<Container?> UpdateTagsAsync(string id, Dictionary<string, string> tags, CancellationToken token = default);

        /// <summary>
        /// Read the container that claims a given RESP database index, if any.
        /// </summary>
        /// <param name="respDatabaseIndex">RESP database index.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The container mapped to the index, or null if none.</returns>
        Task<Container?> ReadByRespDatabaseIndexAsync(int respDatabaseIndex, CancellationToken token = default);

        /// <summary>
        /// Assign (or clear, with null) a container's RESP database index.
        /// </summary>
        /// <param name="id">Container identifier.</param>
        /// <param name="respDatabaseIndex">The index to claim, or null to clear.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated container, or null if not found.</returns>
        /// <exception cref="PepperXException">The index is already assigned to another container (409).</exception>
        Task<Container?> UpdateRespDatabaseIndexAsync(string id, int? respDatabaseIndex, CancellationToken token = default);

        /// <summary>
        /// Set (or clear, with null) a container's per-container multipart-upload expiry, in days. Null
        /// clears the override so the container inherits the system-wide default. The caller is responsible
        /// for clamping; the persisted row reflects the supplied value as-is.
        /// </summary>
        /// <param name="id">Container identifier.</param>
        /// <param name="days">The expiry in days, or null to clear the override.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated container, or null if not found.</returns>
        Task<Container?> UpdateMultipartExpiryAsync(string id, int? days, CancellationToken token = default);

        /// <summary>
        /// Replace a container's cache settings.
        /// </summary>
        /// <param name="id">Container identifier.</param>
        /// <param name="settings">New cache settings. The caller is responsible for clamping and
        /// validation; the persisted row reflects the supplied values as-is.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated container, or null if not found.</returns>
        Task<Container?> UpdateCacheSettingsAsync(string id, ContainerCacheSettings settings, CancellationToken token = default);

        /// <summary>
        /// Delete a container by identifier. The caller is responsible for ensuring it is empty or that its
        /// contents have already been removed.
        /// </summary>
        /// <param name="id">Container identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a container was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Atomically adjust a container's object-count and byte counters.
        /// </summary>
        /// <param name="id">Container identifier.</param>
        /// <param name="objectDelta">Change to the object count.</param>
        /// <param name="byteDelta">Change to the byte total.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task AdjustCountersAsync(string id, long objectDelta, long byteDelta, CancellationToken token = default);

        /// <summary>
        /// Read per-container statistics for every container.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Per-container statistics.</returns>
        Task<IReadOnlyList<ContainerStatistics>> ReadAllStatisticsAsync(CancellationToken token = default);
    }
}
