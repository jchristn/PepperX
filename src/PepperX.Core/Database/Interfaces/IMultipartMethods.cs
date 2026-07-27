namespace PepperX.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Models;
    using PepperX.Core.Responses;

    /// <summary>
    /// Data access for S3 multipart uploads and their staged parts. Uploads are transient records that
    /// become objects only on completion; the janitor reclaims expired ones. Part rows are cascade-deleted
    /// when their upload row is deleted.
    /// </summary>
    public interface IMultipartMethods
    {
        /// <summary>
        /// Create a multipart upload record.
        /// </summary>
        /// <param name="upload">Upload to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created upload.</returns>
        Task<MultipartUpload> CreateUploadAsync(MultipartUpload upload, CancellationToken token = default);

        /// <summary>
        /// Read a multipart upload by identifier.
        /// </summary>
        /// <param name="uploadId">Upload identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The upload, or null if not found.</returns>
        Task<MultipartUpload?> ReadUploadAsync(string uploadId, CancellationToken token = default);

        /// <summary>
        /// List in-progress uploads for a container, paginated by key then upload id.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="keyMarker">Exclusive key marker to resume after, or null to start at the beginning.</param>
        /// <param name="uploadIdMarker">Exclusive upload-id marker paired with <paramref name="keyMarker"/>, or null.</param>
        /// <param name="maxUploads">Maximum uploads to return; clamped to at least 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of uploads with pagination state.</returns>
        Task<MultipartUploadListResult> ListUploadsAsync(string containerId, string? keyMarker, string? uploadIdMarker, int maxUploads, CancellationToken token = default);

        /// <summary>
        /// Delete a multipart upload and (by cascade) its part rows.
        /// </summary>
        /// <param name="uploadId">Upload identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted.</returns>
        Task<bool> DeleteUploadAsync(string uploadId, CancellationToken token = default);

        /// <summary>
        /// Insert or replace a staged part (keyed on upload id and part number). Re-uploading a part
        /// number replaces the prior row (S3 last-write-wins).
        /// </summary>
        /// <param name="part">Part to upsert.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The prior storage location if the part number already existed, else null. The caller
        /// deletes the superseded staged blob.</returns>
        Task<string?> UpsertPartAsync(MultipartPart part, CancellationToken token = default);

        /// <summary>
        /// Read all staged parts for an upload, ascending by part number.
        /// </summary>
        /// <param name="uploadId">Upload identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All parts, ascending.</returns>
        Task<IReadOnlyList<MultipartPart>> ListAllPartsAsync(string uploadId, CancellationToken token = default);

        /// <summary>
        /// List staged parts for an upload, paginated ascending by part number.
        /// </summary>
        /// <param name="uploadId">Upload identifier.</param>
        /// <param name="partNumberMarker">Exclusive part-number marker to resume after; 0 or less starts at the beginning.</param>
        /// <param name="maxParts">Maximum parts to return; clamped to at least 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of parts with pagination state.</returns>
        Task<MultipartPartListResult> ListPartsAsync(string uploadId, int partNumberMarker, int maxParts, CancellationToken token = default);

        /// <summary>
        /// Delete uploads whose expiry has passed and return their identifiers so staged parts can be
        /// reclaimed from storage. Part rows are removed by cascade.
        /// </summary>
        /// <param name="olderThanUtc">Uploads with an expiry at or before this instant are purged.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The identifiers of the purged uploads.</returns>
        Task<IReadOnlyList<string>> PurgeExpiredAsync(DateTime olderThanUtc, CancellationToken token = default);

        /// <summary>
        /// Delete all in-progress uploads for a container (rows and, by cascade, their parts) and return
        /// their identifiers so staged parts can be reclaimed from storage. Called when a container is
        /// deleted so its rows do not orphan or block the container delete via the foreign key.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The identifiers of the deleted uploads.</returns>
        Task<IReadOnlyList<string>> DeleteByContainerAsync(string containerId, CancellationToken token = default);

        /// <summary>
        /// List the identifiers of all in-progress uploads across all containers (used by the janitor to
        /// determine which staging directories still have a live upload).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All current upload identifiers.</returns>
        Task<IReadOnlyList<string>> ListActiveUploadIdsAsync(CancellationToken token = default);
    }
}
