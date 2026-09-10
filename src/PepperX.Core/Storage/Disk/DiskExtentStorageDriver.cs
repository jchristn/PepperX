namespace PepperX.Core.Storage.Disk
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Enums;
    using PepperX.Core.Helpers;
    using PepperX.Core.Serialization;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage.Format;
    using PepperX.Core.Telemetry;

    /// <summary>
    /// Local filesystem extent storage driver. Writes are made durable via a temp-file-then-atomic-move
    /// sequence so a partially written extent is never visible. Extents are laid out under
    /// <c>{root}/{containerId}/{fanout}/{extentId}.pxe</c>, where the fanout keeps directories small. For
    /// multi-node deployments the root must be a filesystem shared by every node.
    /// This type is thread-safe: reads use independent shared handles, and writes use per-extent temp files.
    /// </summary>
    public sealed class DiskExtentStorageDriver : IExtentStorageDriver
    {
        #region Public-Members

        /// <summary>
        /// The driver type.
        /// </summary>
        public StorageDriverTypeEnum Type => StorageDriverTypeEnum.Disk;

        /// <summary>
        /// A human-readable driver name.
        /// </summary>
        public string Name => "Disk";

        #endregion

        #region Private-Members

        private static readonly PepperXSerializer _Serializer = new PepperXSerializer();
        private const string _ExtentExtension = ".pxe";
        private const string _PartExtension = ".part";
        private const string _ManifestFile = "container.json";
        private const string _TempDirName = ".tmp";
        private const string _MultipartDirName = ".multipart";
        private const int _CopyBufferBytes = 81920;
        private const int _DeleteRetryCount = 20;
        private const int _DeleteRetryDelayMs = 25;

        private readonly string _Root;
        private readonly string _TempDir;
        private readonly string _MultipartDir;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the disk driver.
        /// </summary>
        /// <param name="settings">Disk storage settings.</param>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        public DiskExtentStorageDriver(DiskStorageSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Root = Path.GetFullPath(settings.RootDirectory);
            _TempDir = Path.Combine(_Root, _TempDirName);
            _MultipartDir = Path.Combine(_Root, _MultipartDirName);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create the root and temp directories.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task InitializeAsync(CancellationToken token = default)
        {
            Directory.CreateDirectory(_Root);
            Directory.CreateDirectory(_TempDir);
            Directory.CreateDirectory(_MultipartDir);

            // Wire the storage-capacity gauge to a synchronous, non-throwing snapshot of the volume hosting
            // the storage root. The metrics collector calls this periodically on its own thread.
            string root = _Root;
            PepperXTelemetry.SetStorageCapacityProvider(() =>
            {
                try
                {
                    string? pathRoot = Path.GetPathRoot(root);
                    if (String.IsNullOrEmpty(pathRoot)) return null;
                    DriveInfo drive = new DriveInfo(pathRoot);
                    return new StorageCapacitySnapshot(drive.TotalSize, drive.AvailableFreeSpace);
                }
                catch
                {
                    return null;
                }
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Persist an extent durably.
        /// </summary>
        /// <param name="header">Header to persist; size and checksum are populated by the driver.</param>
        /// <param name="payload">Payload source stream.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The write result.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public async Task<ExtentWriteResult> WriteAsync(ExtentHeader header, Stream payload, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("storage.write", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                if (header == null) throw new ArgumentNullException(nameof(header));
                if (payload == null) throw new ArgumentNullException(nameof(payload));

                string location = BuildLocation(header.ContainerId, header.ExtentId);
                string finalPath = Resolve(location);
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                Directory.CreateDirectory(_TempDir);

                string payloadTemp = Path.Combine(_TempDir, header.ExtentId + ".payload.tmp");
                string extentTemp = Path.Combine(_TempDir, header.ExtentId + _ExtentExtension + ".tmp");

                try
                {
                    HashResult hash;
                    using (FileStream payloadOut = new FileStream(payloadTemp, FileMode.Create, FileAccess.Write, FileShare.None, _CopyBufferBytes, FileOptions.Asynchronous))
                    {
                        hash = await HashHelper.CopyAndHashAsync(payload, payloadOut, token).ConfigureAwait(false);
                    }

                    header.SizeBytes = hash.SizeBytes;
                    header.Sha256 = hash.Sha256;
                    header.Md5 = hash.Md5;

                    using (FileStream payloadIn = new FileStream(payloadTemp, FileMode.Open, FileAccess.Read, FileShare.Read, _CopyBufferBytes, FileOptions.Asynchronous))
                    using (FileStream extentOut = new FileStream(extentTemp, FileMode.Create, FileAccess.Write, FileShare.None, _CopyBufferBytes, FileOptions.Asynchronous))
                    {
                        await ExtentFormatWriter.WriteAsync(extentOut, header, payloadIn, token).ConfigureAwait(false);
                        await extentOut.FlushAsync(token).ConfigureAwait(false);
                        extentOut.Flush(true);
                    }

                    File.Move(extentTemp, finalPath, true);
                    PepperXTelemetry.AddStorageBytesWritten(hash.SizeBytes);
                    return new ExtentWriteResult(hash.SizeBytes, hash.Sha256, hash.Md5, location);
                }
                finally
                {
                    TryDelete(payloadTemp);
                    TryDelete(extentTemp);
                }
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordStorage("write", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Read only the header of a stored extent.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The parsed header.</returns>
        public async Task<ExtentHeader> ReadHeaderAsync(string location, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("storage.read_header", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                return await ExtentFormatReader.ReadHeaderOnlyAsync(Resolve(location), token).ConfigureAwait(false);
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordStorage("read_header", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Open the full payload of a stored extent for streaming.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="verifyChecksum">Whether to verify the checksum before returning.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A payload stream; the caller disposes it.</returns>
        public async Task<ExtentPayloadStream> OpenReadAsync(string location, bool verifyChecksum, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("storage.read", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                return await ExtentFormatReader.OpenPayloadAsync(Resolve(location), verifyChecksum, token).ConfigureAwait(false);
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordStorage("read", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Open a byte range of a stored extent's payload for streaming.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="offset">Zero-based payload offset.</param>
        /// <param name="count">Number of bytes to expose.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A windowed payload stream; the caller disposes it.</returns>
        public async Task<ExtentPayloadStream> OpenReadRangeAsync(string location, long offset, long count, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("storage.read_range", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                ExtentPayloadStream __stream = await ExtentFormatReader.OpenRangeAsync(Resolve(location), offset, count, token).ConfigureAwait(false);
                PepperXTelemetry.AddStorageBytesRead(count);
                return __stream;
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordStorage("read_range", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Delete a stored extent.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a file was deleted.</returns>
        public async Task<bool> DeleteAsync(string location, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("storage.delete", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                string path = Resolve(location);
                if (!File.Exists(path)) return false;

                // On Windows a file whose handle is still closing — a just-finished read, or an antivirus or
                // indexer scan of a freshly written extent — can briefly reject deletion with a sharing
                // violation. A short bounded retry absorbs that transient; on POSIX the first attempt succeeds.
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.Delete(path);
                        return true;
                    }
                    catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < _DeleteRetryCount)
                    {
                        await Task.Delay(_DeleteRetryDelayMs, token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordStorage("delete", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Determine whether a stored extent exists.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the file exists.</returns>
        public Task<bool> ExistsAsync(string location, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("storage.exists", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                return Task.FromResult(File.Exists(Resolve(location)));
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordStorage("exists", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Enumerate the driver-relative locations of all stored extents.
        /// </summary>
        /// <returns>Extent locations.</returns>
        public IEnumerable<string> EnumerateExtentLocations()
        {
            if (!Directory.Exists(_Root)) yield break;

            foreach (string path in Directory.EnumerateFiles(_Root, "*" + _ExtentExtension, SearchOption.AllDirectories))
            {
                if (IsUnderTemp(path)) continue;
                yield return ToLocation(path);
            }
        }

        /// <summary>
        /// Asynchronously enumerate the driver-relative locations of all stored extents.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Extent locations.</returns>
        public async IAsyncEnumerable<string> EnumerateExtentLocationsAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            foreach (string location in EnumerateExtentLocations())
            {
                token.ThrowIfCancellationRequested();
                yield return location;
                await Task.Yield();
            }
        }

        /// <summary>
        /// Write or overwrite a container manifest atomically.
        /// </summary>
        /// <param name="manifest">Manifest to persist.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is null.</exception>
        public async Task WriteContainerManifestAsync(ContainerManifest manifest, CancellationToken token = default)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            string dir = Path.Combine(_Root, manifest.Id);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(_TempDir);

            string finalPath = Path.Combine(dir, _ManifestFile);
            string temp = Path.Combine(_TempDir, manifest.Id + ".manifest.tmp");
            string json = _Serializer.SerializeJson(manifest, true) ?? "{}";

            await File.WriteAllTextAsync(temp, json, token).ConfigureAwait(false);
            File.Move(temp, finalPath, true);
        }

        /// <summary>
        /// Read a container manifest by container identifier.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The manifest, or null if it does not exist.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="containerId"/> is null or empty.</exception>
        public async Task<ContainerManifest?> ReadContainerManifestAsync(string containerId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));

            string path = Path.Combine(_Root, containerId, _ManifestFile);
            if (!File.Exists(path)) return null;

            string json = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
            return _Serializer.DeserializeJson<ContainerManifest>(json);
        }

        /// <summary>
        /// Read all container manifests.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All manifests found in storage.</returns>
        public async Task<IReadOnlyList<ContainerManifest>> ReadAllContainerManifestsAsync(CancellationToken token = default)
        {
            List<ContainerManifest> results = new List<ContainerManifest>();
            if (!Directory.Exists(_Root)) return results;

            foreach (string dir in Directory.EnumerateDirectories(_Root))
            {
                token.ThrowIfCancellationRequested();
                if (String.Equals(Path.GetFileName(dir), _TempDirName, StringComparison.Ordinal)) continue;
                if (String.Equals(Path.GetFileName(dir), _MultipartDirName, StringComparison.Ordinal)) continue;

                string path = Path.Combine(dir, _ManifestFile);
                if (!File.Exists(path)) continue;

                string json = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
                ContainerManifest? manifest = _Serializer.DeserializeJson<ContainerManifest>(json);
                if (manifest != null) results.Add(manifest);
            }

            return results;
        }

        /// <summary>
        /// Delete a container's directory, including its manifest and any remaining extents.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="containerId"/> is null or empty.</exception>
        public Task DeleteContainerAsync(string containerId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));

            string dir = Path.Combine(_Root, containerId);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Report storage capacity of the volume hosting the root directory.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Capacity report; zeros when the volume cannot be inspected.</returns>
        public Task<StorageCapacity> GetCapacityAsync(CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("storage.get_capacity", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                try
                {
                    string? root = Path.GetPathRoot(_Root);
                    if (String.IsNullOrEmpty(root)) return Task.FromResult(new StorageCapacity(0, 0));
                    DriveInfo drive = new DriveInfo(root);
                    return Task.FromResult(new StorageCapacity(drive.TotalSize, drive.AvailableFreeSpace));
                }
                catch (Exception)
                {
                    return Task.FromResult(new StorageCapacity(0, 0));
                }
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordStorage("get_capacity", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Remove temporary work files older than the given age.
        /// </summary>
        /// <param name="olderThan">Age threshold.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of temporary files removed.</returns>
        public Task<int> CleanupTempFilesAsync(TimeSpan olderThan, CancellationToken token = default)
        {
            int removed = 0;
            if (!Directory.Exists(_TempDir)) return Task.FromResult(0);

            DateTime cutoff = DateTime.UtcNow - olderThan;
            foreach (string file in Directory.EnumerateFiles(_TempDir))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                        removed++;
                    }
                }
                catch (IOException)
                {
                    // A concurrent write may still hold the file; skip it.
                }
            }

            return Task.FromResult(removed);
        }

        /// <summary>
        /// Stage a multipart upload part durably, computing size, MD5, and SHA-256 in one pass.
        /// </summary>
        /// <param name="uploadId">Owning upload identifier.</param>
        /// <param name="partNumber">Part number.</param>
        /// <param name="payload">Part payload stream.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stage result.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public async Task<MultipartStageResult> WritePartAsync(string uploadId, int partNumber, Stream payload, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("storage.write_part", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                if (String.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));
                if (payload == null) throw new ArgumentNullException(nameof(payload));

                string location = BuildPartLocation(uploadId, partNumber);
                string finalPath = Resolve(location);
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                Directory.CreateDirectory(_TempDir);

                // A unique temp name per attempt so concurrent re-uploads of the same part number do not
                // collide on the temp file (the final path is deterministic and the last mover wins).
                string partTemp = Path.Combine(_TempDir, uploadId + "." + partNumber + "." + Guid.NewGuid().ToString("N") + _PartExtension + ".tmp");

                try
                {
                    HashResult hash;
                    using (FileStream partOut = new FileStream(partTemp, FileMode.Create, FileAccess.Write, FileShare.None, _CopyBufferBytes, FileOptions.Asynchronous))
                    {
                        hash = await HashHelper.CopyAndHashAsync(payload, partOut, token).ConfigureAwait(false);
                        await partOut.FlushAsync(token).ConfigureAwait(false);
                        partOut.Flush(true);
                    }

                    // Concurrent re-uploads of the same part number target the same final path; on Windows a
                    // simultaneous move/open can raise a transient sharing or access violation. A bounded retry
                    // absorbs it — last-writer-wins is the expected semantic for a re-uploaded part.
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            File.Move(partTemp, finalPath, true);
                            break;
                        }
                        catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < _DeleteRetryCount)
                        {
                            await Task.Delay(_DeleteRetryDelayMs, token).ConfigureAwait(false);
                        }
                    }

                    PepperXTelemetry.AddStorageBytesWritten(hash.SizeBytes);
                    return new MultipartStageResult(hash.SizeBytes, hash.Md5, hash.Sha256, location);
                }
                finally
                {
                    TryDelete(partTemp);
                }
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordStorage("write_part", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Open a staged part for reading.
        /// </summary>
        /// <param name="location">Driver-relative staged-part location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A readable stream over the staged part.</returns>
        public Task<Stream> OpenPartAsync(string location, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("storage.open_part", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                string path = Resolve(location);
                Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, _CopyBufferBytes, FileOptions.Asynchronous);
                return Task.FromResult(stream);
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordStorage("open_part", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Delete a single staged part.
        /// </summary>
        /// <param name="location">Driver-relative staged-part location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a file was deleted.</returns>
        public Task<bool> DeletePartAsync(string location, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("storage.delete_part", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                string path = Resolve(location);
                if (!File.Exists(path)) return Task.FromResult(false);
                try
                {
                    File.Delete(path);
                    return Task.FromResult(true);
                }
                catch (IOException)
                {
                    return Task.FromResult(false);
                }
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordStorage("delete_part", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Delete all staged parts for an upload (its staging directory).
        /// </summary>
        /// <param name="uploadId">Upload identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="uploadId"/> is null or empty.</exception>
        public Task DeletePartsAsync(string uploadId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));

            string dir = Path.Combine(_MultipartDir, uploadId);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Remove staging directories whose upload id is not in <paramref name="knownUploadIds"/> and whose
        /// last write is older than <paramref name="olderThan"/>.
        /// </summary>
        /// <param name="olderThan">Age threshold.</param>
        /// <param name="knownUploadIds">Upload identifiers to preserve.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of staging directories removed.</returns>
        public Task<int> CleanupOrphanedPartsAsync(TimeSpan olderThan, System.Collections.Generic.IReadOnlyCollection<string> knownUploadIds, CancellationToken token = default)
        {
            int removed = 0;
            if (!Directory.Exists(_MultipartDir)) return Task.FromResult(0);

            System.Collections.Generic.HashSet<string> known = new System.Collections.Generic.HashSet<string>(
                knownUploadIds ?? System.Array.Empty<string>(), StringComparer.Ordinal);
            DateTime cutoff = DateTime.UtcNow - olderThan;

            foreach (string dir in Directory.EnumerateDirectories(_MultipartDir))
            {
                token.ThrowIfCancellationRequested();
                string uploadId = Path.GetFileName(dir);
                if (known.Contains(uploadId)) continue;
                try
                {
                    if (Directory.GetLastWriteTimeUtc(dir) >= cutoff) continue;
                    Directory.Delete(dir, true);
                    removed++;
                }
                catch (IOException)
                {
                    // A concurrent stage may still hold the directory; skip it.
                }
            }

            return Task.FromResult(removed);
        }

        #endregion

        #region Private-Methods

        private static string BuildLocation(string containerId, string extentId)
        {
            string fanout = extentId.Length >= Constants.ExtentIdPrefix.Length + 2
                ? extentId.Substring(Constants.ExtentIdPrefix.Length, 2)
                : "00";
            return containerId + "/" + fanout + "/" + extentId + _ExtentExtension;
        }

        private static string BuildPartLocation(string uploadId, int partNumber)
        {
            return _MultipartDirName + "/" + uploadId + "/" + partNumber + _PartExtension;
        }

        private string Resolve(string location)
        {
            if (String.IsNullOrEmpty(location)) throw new ArgumentNullException(nameof(location));
            string relative = location.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(_Root, relative);
        }

        private string ToLocation(string absolutePath)
        {
            string relative = Path.GetRelativePath(_Root, absolutePath);
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }

        private bool IsUnderTemp(string absolutePath)
        {
            string relative = Path.GetRelativePath(_Root, absolutePath);
            return relative.StartsWith(_TempDirName + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith(_TempDirName + "/", StringComparison.Ordinal);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup; the janitor will retry.
            }
        }

        #endregion
    }
}
