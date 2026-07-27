namespace PepperX.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Enums;
    using PepperX.Core.Storage.Format;

    /// <summary>
    /// Abstraction over extent payload storage. Implementations persist self-describing extent files and
    /// small per-container manifests. Extents are immutable once written; there is no update operation.
    /// Implementations must be safe for concurrent reads of distinct extents and for concurrent reads of the
    /// same extent.
    /// </summary>
    public interface IExtentStorageDriver
    {
        #region Public-Members

        /// <summary>
        /// The driver type.
        /// </summary>
        StorageDriverTypeEnum Type { get; }

        /// <summary>
        /// A human-readable driver name.
        /// </summary>
        string Name { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Prepare the storage backend (for example, create root directories).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task InitializeAsync(CancellationToken token = default);

        /// <summary>
        /// Persist an extent. The driver computes the payload size and checksum, sets them on
        /// <paramref name="header"/>, writes the file durably, and returns the result including the location.
        /// </summary>
        /// <param name="header">Header to persist; its size and checksum fields are populated by the driver.</param>
        /// <param name="payload">Payload source stream, read to end.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The write result.</returns>
        Task<ExtentWriteResult> WriteAsync(ExtentHeader header, System.IO.Stream payload, CancellationToken token = default);

        /// <summary>
        /// Read only the header of a stored extent.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The parsed header.</returns>
        Task<ExtentHeader> ReadHeaderAsync(string location, CancellationToken token = default);

        /// <summary>
        /// Open the full payload of a stored extent for streaming.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="verifyChecksum">Whether to verify the checksum before returning.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A payload stream; the caller disposes it.</returns>
        Task<ExtentPayloadStream> OpenReadAsync(string location, bool verifyChecksum, CancellationToken token = default);

        /// <summary>
        /// Open a byte range of a stored extent's payload for streaming.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="offset">Zero-based payload offset.</param>
        /// <param name="count">Number of bytes to expose; clamped to the remaining payload.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A payload stream windowed to the range; the caller disposes it.</returns>
        Task<ExtentPayloadStream> OpenReadRangeAsync(string location, long offset, long count, CancellationToken token = default);

        /// <summary>
        /// Delete a stored extent.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a file was deleted; false if it did not exist.</returns>
        Task<bool> DeleteAsync(string location, CancellationToken token = default);

        /// <summary>
        /// Determine whether a stored extent exists.
        /// </summary>
        /// <param name="location">Driver-relative location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the file exists.</returns>
        Task<bool> ExistsAsync(string location, CancellationToken token = default);

        /// <summary>
        /// Enumerate the driver-relative locations of all stored extents.
        /// </summary>
        /// <returns>Extent locations.</returns>
        IEnumerable<string> EnumerateExtentLocations();

        /// <summary>
        /// Asynchronously enumerate the driver-relative locations of all stored extents.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Extent locations.</returns>
        IAsyncEnumerable<string> EnumerateExtentLocationsAsync(CancellationToken token = default);

        /// <summary>
        /// Write or overwrite a container manifest.
        /// </summary>
        /// <param name="manifest">Manifest to persist.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task WriteContainerManifestAsync(ContainerManifest manifest, CancellationToken token = default);

        /// <summary>
        /// Read a container manifest by container identifier.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The manifest, or null if it does not exist.</returns>
        Task<ContainerManifest?> ReadContainerManifestAsync(string containerId, CancellationToken token = default);

        /// <summary>
        /// Read all container manifests.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All manifests found in storage.</returns>
        Task<IReadOnlyList<ContainerManifest>> ReadAllContainerManifestsAsync(CancellationToken token = default);

        /// <summary>
        /// Delete a container's directory, including its manifest and any remaining extents.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteContainerAsync(string containerId, CancellationToken token = default);

        /// <summary>
        /// Report storage capacity.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Capacity report.</returns>
        Task<StorageCapacity> GetCapacityAsync(CancellationToken token = default);

        /// <summary>
        /// Remove temporary work files older than the given age (crash-recovery cleanup).
        /// </summary>
        /// <param name="olderThan">Age threshold.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of temporary files removed.</returns>
        Task<int> CleanupTempFilesAsync(TimeSpan olderThan, CancellationToken token = default);

        /// <summary>
        /// Stage a multipart upload part durably on the shared storage root, computing its size, MD5, and
        /// SHA-256 in a single streamed pass. Re-staging the same part number overwrites the prior blob.
        /// The staging area is shared across nodes so any node can complete or abort the upload.
        /// </summary>
        /// <param name="uploadId">Owning upload identifier.</param>
        /// <param name="partNumber">Part number.</param>
        /// <param name="payload">Part payload stream, read to end.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stage result including size, both hashes, and the driver-relative location.</returns>
        Task<MultipartStageResult> WritePartAsync(string uploadId, int partNumber, System.IO.Stream payload, CancellationToken token = default);

        /// <summary>
        /// Open a staged part for reading.
        /// </summary>
        /// <param name="location">Driver-relative staged-part location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A readable stream over the staged part; the caller disposes it.</returns>
        Task<System.IO.Stream> OpenPartAsync(string location, CancellationToken token = default);

        /// <summary>
        /// Delete a single staged part.
        /// </summary>
        /// <param name="location">Driver-relative staged-part location.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a file was deleted; false if it did not exist.</returns>
        Task<bool> DeletePartAsync(string location, CancellationToken token = default);

        /// <summary>
        /// Delete all staged parts for an upload (its staging directory).
        /// </summary>
        /// <param name="uploadId">Upload identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeletePartsAsync(string uploadId, CancellationToken token = default);

        /// <summary>
        /// Remove staging directories whose upload identifier is not in <paramref name="knownUploadIds"/> and
        /// whose last write is older than <paramref name="olderThan"/> (crash-recovery cleanup for uploads
        /// whose rows are already gone).
        /// </summary>
        /// <param name="olderThan">Age threshold.</param>
        /// <param name="knownUploadIds">Upload identifiers that still have rows and must be preserved.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of staging directories removed.</returns>
        Task<int> CleanupOrphanedPartsAsync(TimeSpan olderThan, System.Collections.Generic.IReadOnlyCollection<string> knownUploadIds, CancellationToken token = default);

        #endregion
    }
}
