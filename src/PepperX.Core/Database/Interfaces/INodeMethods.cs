namespace PepperX.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Models;

    /// <summary>
    /// Data access for cluster node registration and heartbeats.
    /// </summary>
    public interface INodeMethods
    {
        /// <summary>
        /// Insert or update a node's record and heartbeat.
        /// </summary>
        /// <param name="node">Node record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpsertHeartbeatAsync(NodeRecord node, CancellationToken token = default);

        /// <summary>
        /// List all registered nodes.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All node records.</returns>
        Task<IReadOnlyList<NodeRecord>> ListAsync(CancellationToken token = default);

        /// <summary>
        /// List nodes whose last heartbeat is older than the given threshold.
        /// </summary>
        /// <param name="deadAfterSeconds">Threshold in seconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Dead node records.</returns>
        Task<IReadOnlyList<NodeRecord>> ListDeadAsync(int deadAfterSeconds, CancellationToken token = default);

        /// <summary>
        /// Delete a node record.
        /// </summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteAsync(string nodeId, CancellationToken token = default);
    }
}
