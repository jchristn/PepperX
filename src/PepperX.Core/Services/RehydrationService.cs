namespace PepperX.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Enums;
    using PepperX.Core.Models;
    using PepperX.Core.Responses;
    using PepperX.Core.Storage;
    using PepperX.Core.Storage.Format;
    using PepperX.Core.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Reconciles the metadata database with raw extent storage. Verify reports drift only; Repair and
    /// Rebuild add database rows for extents present in storage, remove rows whose backing file is gone, and
    /// recompute container counters. On an empty database, Rebuild is a full reconstruction from storage.
    /// </summary>
    public sealed class RehydrationService
    {
        #region Private-Members

        private readonly string _Header = "[RehydrationService] ";
        private readonly IMetadataDatabaseDriver _Db;
        private readonly IExtentStorageDriver _Storage;
        private readonly LoggingModule? _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the rehydration service.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="storage">Extent storage driver.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public RehydrationService(IMetadataDatabaseDriver db, IExtentStorageDriver storage, LoggingModule? logging = null)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run a rehydration in the requested mode.
        /// </summary>
        /// <param name="mode">Reconciliation mode.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A report describing what was found and changed.</returns>
        public async Task<RehydrationReport> RehydrateAsync(RehydrationModeEnum mode, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("rehydration.run", ActivityKind.Internal);
            __act?.SetTag("pepperx.mode", mode.ToString());
            bool __ok = true;
            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                RehydrationReport report = new RehydrationReport { Mode = mode };
                bool mutate = mode != RehydrationModeEnum.Verify;

                Dictionary<string, long> targetCount = new Dictionary<string, long>();
                Dictionary<string, long> targetBytes = new Dictionary<string, long>();

                IReadOnlyList<ContainerManifest> manifests = await _Storage.ReadAllContainerManifestsAsync(token).ConfigureAwait(false);
                report.ContainersDiscovered = manifests.Count;
                foreach (ContainerManifest manifest in manifests)
                {
                    await EnsureContainerAsync(manifest.Id, manifest.Name, manifest.Tags, manifest.Cache, manifest.RespDatabaseIndex, manifest.MultipartUploadExpiryDays, mutate, report, token).ConfigureAwait(false);
                    if (!targetCount.ContainsKey(manifest.Id)) { targetCount[manifest.Id] = 0; targetBytes[manifest.Id] = 0; }
                }

                await foreach (string location in _Storage.EnumerateExtentLocationsAsync(token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    ExtentHeader header;
                    try
                    {
                        header = await _Storage.ReadHeaderAsync(location, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        report.Drift.Add("Unreadable extent at " + location + ": " + ex.Message);
                        continue;
                    }

                    report.ExtentsDiscovered++;
                    await EnsureContainerAsync(header.ContainerId, header.ContainerName, null, null, null, null, mutate, report, token).ConfigureAwait(false);

                    if (!targetCount.ContainsKey(header.ContainerId)) { targetCount[header.ContainerId] = 0; targetBytes[header.ContainerId] = 0; }
                    targetCount[header.ContainerId] += 1;
                    targetBytes[header.ContainerId] += header.SizeBytes;

                    Extent? existing = await _Db.Extents.ReadByIdAsync(header.ExtentId, token).ConfigureAwait(false);
                    if (existing == null)
                    {
                        if (mutate)
                        {
                            await _Db.Extents.CreateAsync(BuildExtent(header, location), token).ConfigureAwait(false);
                            report.RowsAdded++;
                        }
                        else
                        {
                            report.Drift.Add("Extent " + header.ExtentId + " (" + header.Key + ") present in storage but missing from database.");
                        }
                    }
                }

                await RemoveOrphansAsync(mutate, report, token).ConfigureAwait(false);
                await ReconcileCountersAsync(targetCount, targetBytes, mutate, report, token).ConfigureAwait(false);

                stopwatch.Stop();
                report.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
                report.Success = true;
                _Logging?.Info(_Header + "rehydration (" + mode + ") complete: +" + report.RowsAdded + " -" + report.RowsRemoved + " in " + report.DurationMs.ToString("F0") + "ms");
                return report;
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordRehydration(mode.ToString(), Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        #endregion

        #region Private-Methods

        private async Task EnsureContainerAsync(string containerId, string containerName, Dictionary<string, string>? tags, ContainerCacheSettings? cache, int? respIndex, int? multipartExpiryDays, bool mutate, RehydrationReport report, CancellationToken token)
        {
            Container? existing = await _Db.Containers.ReadByIdAsync(containerId, token).ConfigureAwait(false);
            if (existing != null)
            {
                if (mutate && tags != null) await _Db.Containers.UpdateTagsAsync(containerId, tags, token).ConfigureAwait(false);
                if (mutate && cache != null) await _Db.Containers.UpdateCacheSettingsAsync(containerId, cache, token).ConfigureAwait(false);
                if (mutate && respIndex.HasValue) await RestoreRespIndexAsync(containerId, containerName, respIndex.Value, report, token).ConfigureAwait(false);
                if (mutate && multipartExpiryDays.HasValue) await _Db.Containers.UpdateMultipartExpiryAsync(containerId, multipartExpiryDays, token).ConfigureAwait(false);
                return;
            }

            if (!mutate)
            {
                report.Drift.Add("Container " + containerName + " (" + containerId + ") present in storage but missing from database.");
                return;
            }

            if (!Container.IsValidName(containerName))
            {
                report.Drift.Add("Container " + containerId + " has an invalid name in storage: '" + containerName + "'.");
                return;
            }

            Container container = new Container { Id = containerId, Name = containerName };
            if (tags != null) container.Tags = tags;
            if (cache != null) container.Cache = cache;
            if (respIndex.HasValue) container.RespDatabaseIndex = respIndex.Value;
            if (multipartExpiryDays.HasValue) container.MultipartUploadExpiryDays = multipartExpiryDays;
            try
            {
                await _Db.Containers.CreateAsync(container, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A RESP-index collision must not sink the whole container: retry without the index and note
                // the drift, so the container is still recreated (just not addressable over RESP by index).
                if (respIndex.HasValue)
                {
                    report.Drift.Add("RESP database index " + respIndex.Value + " for container " + containerName + " could not be restored (" + ex.Message + "); recreating without it.");
                    container.RespDatabaseIndex = null;
                    try
                    {
                        await _Db.Containers.CreateAsync(container, token).ConfigureAwait(false);
                    }
                    catch (Exception inner)
                    {
                        report.Drift.Add("Could not recreate container " + containerName + ": " + inner.Message);
                    }
                }
                else
                {
                    report.Drift.Add("Could not recreate container " + containerName + ": " + ex.Message);
                }
            }
        }

        private async Task RestoreRespIndexAsync(string containerId, string containerName, int respIndex, RehydrationReport report, CancellationToken token)
        {
            try
            {
                await _Db.Containers.UpdateRespDatabaseIndexAsync(containerId, respIndex, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                report.Drift.Add("RESP database index " + respIndex + " for container " + containerName + " could not be restored: " + ex.Message);
            }
        }

        private async Task RemoveOrphansAsync(bool mutate, RehydrationReport report, CancellationToken token)
        {
            string? continuation = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                EnumerationQuery query = new EnumerationQuery { MaxResults = 200, Ordering = EnumerationOrderEnum.CreatedAscending, ContinuationToken = continuation };
                EnumerationResult<Extent> page = await _Db.Extents.EnumerateAsync(null, query, token).ConfigureAwait(false);

                foreach (Extent extent in page.Objects)
                {
                    bool exists = await _Storage.ExistsAsync(extent.StorageLocation, token).ConfigureAwait(false);
                    if (exists) continue;

                    if (mutate)
                    {
                        await _Db.Extents.MarkDeletingAsync(extent.ContainerId, extent.Key, token).ConfigureAwait(false);
                        await _Db.Extents.PurgeAsync(extent.Id, token).ConfigureAwait(false);
                        report.RowsRemoved++;
                    }
                    else
                    {
                        report.Drift.Add("Extent " + extent.Id + " (" + extent.Key + ") is in the database but has no backing file.");
                    }
                }

                if (page.EndOfResults) break;
                continuation = page.ContinuationToken;
                if (continuation == null) break;
            }
        }

        private async Task ReconcileCountersAsync(Dictionary<string, long> targetCount, Dictionary<string, long> targetBytes, bool mutate, RehydrationReport report, CancellationToken token)
        {
            foreach (KeyValuePair<string, long> entry in targetCount)
            {
                Container? container = await _Db.Containers.ReadByIdAsync(entry.Key, token).ConfigureAwait(false);
                if (container == null) continue;

                long countDelta = entry.Value - container.ObjectCount;
                long byteDelta = targetBytes[entry.Key] - container.TotalBytes;

                if (countDelta == 0 && byteDelta == 0) continue;

                if (mutate) await _Db.Containers.AdjustCountersAsync(entry.Key, countDelta, byteDelta, token).ConfigureAwait(false);
                else report.Drift.Add("Container " + container.Name + " counters drift: count " + countDelta + ", bytes " + byteDelta + ".");
            }
        }

        private static Extent BuildExtent(ExtentHeader header, string location)
        {
            return new Extent
            {
                Id = header.ExtentId,
                ContainerId = header.ContainerId,
                Key = header.Key,
                State = ExtentStateEnum.Active,
                SizeBytes = header.SizeBytes,
                Sha256 = header.Sha256,
                Md5 = header.Md5,
                Etag = header.Etag,
                ContentType = header.ContentType,
                StorageDriver = StorageDriverTypeEnum.Disk,
                StorageLocation = location,
                HasMetadataObject = header.Object != null,
                Labels = new List<string>(header.Labels),
                Tags = new Dictionary<string, string>(header.Tags),
                CreatedUtc = header.CreatedUtc
            };
        }

        #endregion
    }
}
