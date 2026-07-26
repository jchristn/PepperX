namespace PepperX.Core.Storage.Disk
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Enums;
    using PepperX.Core.Helpers;
    using PepperX.Core.Serialization;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage.Format;

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
        private const string _ManifestFile = "container.json";
        private const string _TempDirName = ".tmp";
        private const int _CopyBufferBytes = 81920;
        private const int _DeleteRetryCount = 20;
        private const int _DeleteRetryDelayMs = 25;

        private readonly string _Root;
        private readonly string _TempDir;

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

                using (FileStream payloadIn = new FileStream(payloadTemp, FileMode.Open, FileAccess.Read, FileShare.Read, _CopyBufferBytes, FileOptions.Asynchronous))
                using (FileStream extentOut = new FileStream(extentTemp, FileMode.Create, FileAccess.Write, FileShare.None, _CopyBufferBytes, FileOptions.Asynchronous))
                {
                    await ExtentFormatWriter.WriteAsync(extentOut, header, payloadIn, token).ConfigureAwait(false);
                    await extentOut.FlushAsync(token).ConfigureAwait(false);
                    extentOut.Flush(true);
                }

                File.Move(extentTemp, finalPath, true);
                return new ExtentWriteResult(hash.SizeBytes, hash.Sha256, location);
            }
            finally
            {
                TryDelete(payloadTemp);
                TryDelete(extentTemp);
            }
        }

        /// <summary>
        /// Read only the header of a stored extent.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The parsed header.</returns>
        public Task<ExtentHeader> ReadHeaderAsync(string location, CancellationToken token = default)
        {
            return ExtentFormatReader.ReadHeaderOnlyAsync(Resolve(location), token);
        }

        /// <summary>
        /// Open the full payload of a stored extent for streaming.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="verifyChecksum">Whether to verify the checksum before returning.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A payload stream; the caller disposes it.</returns>
        public Task<ExtentPayloadStream> OpenReadAsync(string location, bool verifyChecksum, CancellationToken token = default)
        {
            return ExtentFormatReader.OpenPayloadAsync(Resolve(location), verifyChecksum, token);
        }

        /// <summary>
        /// Open a byte range of a stored extent's payload for streaming.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="offset">Zero-based payload offset.</param>
        /// <param name="count">Number of bytes to expose.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A windowed payload stream; the caller disposes it.</returns>
        public Task<ExtentPayloadStream> OpenReadRangeAsync(string location, long offset, long count, CancellationToken token = default)
        {
            return ExtentFormatReader.OpenRangeAsync(Resolve(location), offset, count, token);
        }

        /// <summary>
        /// Delete a stored extent.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a file was deleted.</returns>
        public async Task<bool> DeleteAsync(string location, CancellationToken token = default)
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

        /// <summary>
        /// Determine whether a stored extent exists.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the file exists.</returns>
        public Task<bool> ExistsAsync(string location, CancellationToken token = default)
        {
            return Task.FromResult(File.Exists(Resolve(location)));
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

        #endregion

        #region Private-Methods

        private static string BuildLocation(string containerId, string extentId)
        {
            string fanout = extentId.Length >= Constants.ExtentIdPrefix.Length + 2
                ? extentId.Substring(Constants.ExtentIdPrefix.Length, 2)
                : "00";
            return containerId + "/" + fanout + "/" + extentId + _ExtentExtension;
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
