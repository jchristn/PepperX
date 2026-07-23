namespace PepperX.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;

    /// <summary>
    /// Data access for captured request history.
    /// </summary>
    public interface IRequestHistoryMethods
    {
        /// <summary>
        /// Insert a captured request record.
        /// </summary>
        /// <param name="entry">Entry to insert.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task CreateAsync(RequestHistoryEntry entry, CancellationToken token = default);

        /// <summary>
        /// Read a single entry by identifier, including bodies and headers.
        /// </summary>
        /// <param name="id">Entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry, or null if not found.</returns>
        Task<RequestHistoryEntry?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Page through entries matching a filter. Bodies are omitted from list results.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of entries.</returns>
        Task<RequestHistoryPage> EnumerateAsync(RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>
        /// Return bucketed counts and averages for chart rendering. Every bucket in the range is emitted,
        /// including empty ones.
        /// </summary>
        /// <param name="filter">Filter, including the summary bucket width and time range.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A time-bucketed summary.</returns>
        Task<RequestHistorySummary> SummarizeAsync(RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>
        /// Delete a single entry.
        /// </summary>
        /// <param name="id">Entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if an entry was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete all entries matching a filter.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of entries deleted.</returns>
        Task<int> DeleteManyAsync(RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>
        /// Prune entries older than a UTC cutoff.
        /// </summary>
        /// <param name="olderThanUtc">Cutoff timestamp.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of entries pruned.</returns>
        Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default);
    }
}
