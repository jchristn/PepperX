namespace PepperX.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Models;

    /// <summary>
    /// Data access for extents. Implementations maintain container counters transactionally: creation
    /// increments them, tombstoning decrements them, replacement adjusts the byte delta, and purging (pure
    /// cleanup) does not change them.
    /// </summary>
    public interface IExtentMethods
    {
        /// <summary>
        /// Create an extent together with its labels and tags, and increment the container counters, in a
        /// single transaction. Fails if an active extent already exists for the same container and key.
        /// </summary>
        /// <param name="extent">Extent to create, with labels and tags populated.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created extent.</returns>
        Task<Extent> CreateAsync(Extent extent, CancellationToken token = default);

        /// <summary>
        /// Read the active extent for a container and key, with labels and tags hydrated.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The active extent, or null if none.</returns>
        Task<Extent?> ReadActiveAsync(string containerId, string key, CancellationToken token = default);

        /// <summary>
        /// Read an extent by identifier, with labels and tags hydrated, regardless of state.
        /// </summary>
        /// <param name="id">Extent identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The extent, or null if not found.</returns>
        Task<Extent?> ReadByIdAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Determine whether an active extent exists for a container and key.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if an active extent exists.</returns>
        Task<bool> ExistsActiveAsync(string containerId, string key, CancellationToken token = default);

        /// <summary>
        /// Transition the active extent for a container and key to the Deleting state and decrement container
        /// counters, in a single transaction.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tombstoned extent, or null if there was no active extent.</returns>
        Task<Extent?> MarkDeletingAsync(string containerId, string key, CancellationToken token = default);

        /// <summary>
        /// Create a new active extent, tombstone any existing active extent for the same container and key,
        /// and adjust the container byte counter, in a single transaction (atomic replace).
        /// </summary>
        /// <param name="newExtent">New extent, with labels and tags populated.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The identifier of the tombstoned prior extent, or null if there was none.</returns>
        Task<string?> ReplaceAsync(Extent newExtent, CancellationToken token = default);

        /// <summary>
        /// Physically remove an extent row and its labels, tags, and leases. Container counters are not
        /// changed (they were already adjusted at tombstone time).
        /// </summary>
        /// <param name="id">Extent identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task PurgeAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate active extents, optionally within one container, with label/tag/prefix/suffix/date
        /// filters, ordering, and pagination.
        /// </summary>
        /// <param name="containerId">Container identifier to scope to, or null for cross-container.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of extents with labels and tags hydrated.</returns>
        Task<EnumerationResult<Extent>> EnumerateAsync(string? containerId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// List extents currently in the Deleting state (for the janitor to finish).
        /// </summary>
        /// <param name="limit">Maximum rows to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Extents in the Deleting state.</returns>
        Task<IReadOnlyList<Extent>> ListDeletingAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Count active extents, optionally within one container.
        /// </summary>
        /// <param name="containerId">Container identifier, or null for all containers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count of active extents.</returns>
        Task<long> CountActiveAsync(string? containerId, CancellationToken token = default);
    }
}
