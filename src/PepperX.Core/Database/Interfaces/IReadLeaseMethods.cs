namespace PepperX.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Data access for read leases, the mechanism by which a delete waits for in-flight reads across the
    /// cluster.
    /// </summary>
    public interface IReadLeaseMethods
    {
        /// <summary>
        /// Atomically resolve the active extent for a container and key and, if found, insert a read lease
        /// protecting it. Returns null when there is no active extent (for example, it is being deleted).
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="key">Object key.</param>
        /// <param name="nodeId">Identifier of the acquiring node.</param>
        /// <param name="ttlSeconds">Lease time-to-live in seconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The extent and lease identifier, or null.</returns>
        Task<LeaseAcquisition?> AcquireForActiveExtentAsync(string containerId, string key, string nodeId, int ttlSeconds, CancellationToken token = default);

        /// <summary>
        /// Extend a lease's expiry.
        /// </summary>
        /// <param name="leaseId">Lease identifier.</param>
        /// <param name="ttlSeconds">New time-to-live in seconds from now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the lease still existed and was renewed.</returns>
        Task<bool> RenewAsync(string leaseId, int ttlSeconds, CancellationToken token = default);

        /// <summary>
        /// Release (delete) a lease.
        /// </summary>
        /// <param name="leaseId">Lease identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task ReleaseAsync(string leaseId, CancellationToken token = default);

        /// <summary>
        /// Count unexpired leases on an extent.
        /// </summary>
        /// <param name="extentId">Extent identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count of unexpired leases.</returns>
        Task<long> CountActiveForExtentAsync(string extentId, CancellationToken token = default);

        /// <summary>
        /// Delete all expired leases.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of leases removed.</returns>
        Task<int> PurgeExpiredAsync(CancellationToken token = default);

        /// <summary>
        /// Delete all leases held by a node (used when a node is declared dead).
        /// </summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of leases removed.</returns>
        Task<int> PurgeForNodeAsync(string nodeId, CancellationToken token = default);
    }
}
