namespace PepperX.Core.Database
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database.Interfaces;
    using PepperX.Core.Enums;

    /// <summary>
    /// Provider-neutral metadata database driver. Exposes domain-specific method groups rather than a generic
    /// repository. PepperX ships with a PostgreSQL implementation.
    /// </summary>
    public interface IMetadataDatabaseDriver : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// The provider type.
        /// </summary>
        DatabaseTypeEnum DatabaseType { get; }

        /// <summary>
        /// Container data access.
        /// </summary>
        IContainerMethods Containers { get; }

        /// <summary>
        /// Extent data access.
        /// </summary>
        IExtentMethods Extents { get; }

        /// <summary>
        /// Read lease data access.
        /// </summary>
        IReadLeaseMethods ReadLeases { get; }

        /// <summary>
        /// Node data access.
        /// </summary>
        INodeMethods Nodes { get; }

        /// <summary>
        /// Request history data access.
        /// </summary>
        IRequestHistoryMethods RequestHistory { get; }

        /// <summary>
        /// S3 multipart upload data access.
        /// </summary>
        IMultipartMethods MultipartUploads { get; }

        /// <summary>
        /// Initialize the driver: open the connection pool and apply pending migrations.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task InitializeAsync(CancellationToken token = default);

        /// <summary>
        /// Report the approximate size of the database in bytes.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Database size in bytes.</returns>
        Task<long> GetDatabaseSizeBytesAsync(CancellationToken token = default);

        /// <summary>
        /// Close the driver and release its connection pool.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task CloseAsync(CancellationToken token = default);
    }
}
