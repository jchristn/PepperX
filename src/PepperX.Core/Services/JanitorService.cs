namespace PepperX.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Models;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage;
    using PepperX.Core.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Periodic maintenance: finishes orphaned tombstoned extents, purges expired leases and leases held by
    /// dead nodes, removes stale temp files, and prunes request history.
    /// </summary>
    public sealed class JanitorService : IDisposable
    {
        #region Private-Members

        private readonly string _Header = "[JanitorService] ";
        private readonly IMetadataDatabaseDriver _Db;
        private readonly IExtentStorageDriver _Storage;
        private readonly ObjectDeleteService _DeleteService;
        private readonly ClusterSettings _Cluster;
        private readonly RequestHistorySettings _RequestHistory;
        private readonly S3Settings _S3;
        private readonly LoggingModule? _Logging;
        private Timer? _Timer;
        private int _Running;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the janitor.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="storage">Extent storage driver.</param>
        /// <param name="deleteService">Delete service used to finish tombstoned extents.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public JanitorService(IMetadataDatabaseDriver db, IExtentStorageDriver storage, ObjectDeleteService deleteService, PepperXSettings settings, LoggingModule? logging = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _DeleteService = deleteService ?? throw new ArgumentNullException(nameof(deleteService));
            _Cluster = settings.Cluster;
            _RequestHistory = settings.RequestHistory;
            _S3 = settings.S3;
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the periodic janitor timer.
        /// </summary>
        public void Start()
        {
            int intervalMs = _Cluster.JanitorIntervalSeconds * 1000;
            _Timer = new Timer(_ => Tick(), null, intervalMs, intervalMs);
        }

        /// <summary>
        /// Run a single maintenance pass immediately.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task RunOnceAsync(CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("janitor.run", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                await FinishTombstonedAsync(token).ConfigureAwait(false);

                int __leases = await _Db.ReadLeases.PurgeExpiredAsync(token).ConfigureAwait(false);
                PepperXTelemetry.AddJanitorItems("leases", __leases);

                await PurgeDeadNodesAsync(token).ConfigureAwait(false);

                int __tempFiles = await _Storage.CleanupTempFilesAsync(TimeSpan.FromHours(1), token).ConfigureAwait(false);
                PepperXTelemetry.AddJanitorItems("temp_files", __tempFiles);

                int __history = await _Db.RequestHistory.PruneAsync(DateTime.UtcNow.AddDays(-_RequestHistory.RetentionDays), token).ConfigureAwait(false);
                PepperXTelemetry.AddJanitorItems("request_history", __history);

                await PurgeExpiredMultipartUploadsAsync(token).ConfigureAwait(false);
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordJanitorRun(Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Stop the janitor timer.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Timer?.Dispose();
            _Timer = null;
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private void Tick()
        {
            if (Interlocked.CompareExchange(ref _Running, 1, 0) != 0) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await RunOnceAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging?.Warn(_Header + "maintenance pass failed: " + ex.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref _Running, 0);
                }
            });
        }

        private async Task FinishTombstonedAsync(CancellationToken token)
        {
            IReadOnlyList<Extent> deleting = await _Db.Extents.ListDeletingAsync(100, token).ConfigureAwait(false);
            foreach (Extent extent in deleting)
            {
                try
                {
                    await _DeleteService.FinishTombstonedAsync(extent, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging?.Debug(_Header + "could not finish extent " + extent.Id + ": " + ex.Message);
                }
            }
        }

        private async Task PurgeExpiredMultipartUploadsAsync(CancellationToken token)
        {
            try
            {
                // Purge expired upload rows and reclaim their staged blobs.
                IReadOnlyList<string> expired = await _Db.MultipartUploads.PurgeExpiredAsync(DateTime.UtcNow, token).ConfigureAwait(false);
                foreach (string uploadId in expired)
                {
                    try
                    {
                        await _Storage.DeletePartsAsync(uploadId, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging?.Debug(_Header + "could not delete staged parts for expired upload " + uploadId + ": " + ex.Message);
                    }
                }

                // Reclaim staging directories whose upload row is gone (crash recovery), preserving any that
                // still have a live upload and any younger than the expiry window.
                IReadOnlyList<string> known = await _Db.MultipartUploads.ListActiveUploadIdsAsync(token).ConfigureAwait(false);
                await _Storage.CleanupOrphanedPartsAsync(TimeSpan.FromDays(_S3.MultipartUploadExpiryDays), known, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging?.Warn(_Header + "multipart upload purge failed: " + ex.Message);
            }
        }

        private async Task PurgeDeadNodesAsync(CancellationToken token)
        {
            IReadOnlyList<NodeRecord> dead = await _Db.Nodes.ListDeadAsync(_Cluster.NodeDeadAfterSeconds, token).ConfigureAwait(false);
            foreach (NodeRecord node in dead)
            {
                await _Db.ReadLeases.PurgeForNodeAsync(node.Id, token).ConfigureAwait(false);
                await _Db.Nodes.DeleteAsync(node.Id, token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
