namespace PepperX.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage;
    using SyslogLogging;

    /// <summary>
    /// Orchestrates S3 multipart uploads: initiate, upload part (including copy-from-object), complete
    /// (assemble staged parts into one immutable extent), abort, and list. Protocol-neutral: the S3 handler
    /// maps S3Server request/response types to and from this service, which never references the S3 library.
    /// Staged parts live on the shared storage root so any node can serve any operation for any upload.
    /// </summary>
    public sealed class MultipartUploadService
    {
        #region Private-Members

        private readonly IMetadataDatabaseDriver _Db;
        private readonly IExtentStorageDriver _Storage;
        private readonly ObjectWriteService _Writes;
        private readonly ObjectReadService _Reads;
        private readonly S3Settings _Settings;
        private readonly LoggingModule? _Logging;
        private readonly string _Header = "[MultipartUploadService] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the multipart upload service.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="storage">Extent storage driver (also stages parts).</param>
        /// <param name="writes">Object write service (assembles the completed object).</param>
        /// <param name="reads">Object read service (used by UploadPartCopy).</param>
        /// <param name="settings">S3 settings (limits and expiry).</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public MultipartUploadService(IMetadataDatabaseDriver db, IExtentStorageDriver storage, ObjectWriteService writes, ObjectReadService reads, S3Settings settings, LoggingModule? logging = null)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _Writes = writes ?? throw new ArgumentNullException(nameof(writes));
            _Reads = reads ?? throw new ArgumentNullException(nameof(reads));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initiate a multipart upload and return the upload record (whose id is the S3 UploadId).
        /// </summary>
        /// <param name="containerName">Container (bucket) name.</param>
        /// <param name="key">Target object key.</param>
        /// <param name="contentType">Content type, or null.</param>
        /// <param name="tags">Tags to apply to the completed object, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created upload.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        public async Task<MultipartUpload> InitiateAsync(string containerName, string key, string? contentType, Dictionary<string, string>? tags, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentException("Object key must not be empty.", nameof(key));

            Container container = await RequireContainerAsync(containerName, token).ConfigureAwait(false);

            DateTime now = DateTime.UtcNow;
            MultipartUpload upload = new MultipartUpload
            {
                ContainerId = container.Id,
                Key = key,
                ContentType = contentType,
                Tags = tags ?? new Dictionary<string, string>(),
                InitiatedUtc = now,
                ExpiresUtc = now.AddDays(_Settings.MultipartUploadExpiryDays)
            };

            await _Db.MultipartUploads.CreateUploadAsync(upload, token).ConfigureAwait(false);
            _Logging?.Debug(_Header + "initiated upload " + upload.Id + " for " + containerName + "/" + key);
            return upload;
        }

        /// <summary>
        /// Stage an uploaded part from a request body and return the staged part (its MD5 is the part ETag).
        /// </summary>
        /// <param name="containerName">Container (bucket) name.</param>
        /// <param name="uploadId">Upload id.</param>
        /// <param name="partNumber">Part number (1 to the configured maximum).</param>
        /// <param name="payload">Part payload stream.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The staged part.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        /// <exception cref="NoSuchUploadException">The upload does not exist or belongs to another container.</exception>
        public async Task<MultipartPart> UploadPartAsync(string containerName, string uploadId, int partNumber, Stream payload, CancellationToken token = default)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            MultipartUpload upload = await RequireUploadAsync(containerName, uploadId, token).ConfigureAwait(false);
            ValidatePartNumber(partNumber);

            MultipartStageResult stage = await _Storage.WritePartAsync(uploadId, partNumber, payload, token).ConfigureAwait(false);
            return await UpsertStagedPartAsync(upload.Id, partNumber, stage, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Stage a part by copying from an existing object (S3 UploadPartCopy), optionally a byte range.
        /// </summary>
        /// <param name="containerName">Container (bucket) name of the target upload.</param>
        /// <param name="uploadId">Upload id.</param>
        /// <param name="partNumber">Part number.</param>
        /// <param name="sourceContainer">Source container name.</param>
        /// <param name="sourceKey">Source object key.</param>
        /// <param name="rangeStart">Optional inclusive range start; null copies the whole object.</param>
        /// <param name="rangeCount">Optional range length; null copies to the end.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The staged part.</returns>
        /// <exception cref="NoSuchUploadException">The upload does not exist.</exception>
        /// <exception cref="ObjectNotFoundException">The source object does not exist.</exception>
        public async Task<MultipartPart> UploadPartCopyAsync(string containerName, string uploadId, int partNumber, string sourceContainer, string sourceKey, long? rangeStart, long? rangeCount, CancellationToken token = default)
        {
            MultipartUpload upload = await RequireUploadAsync(containerName, uploadId, token).ConfigureAwait(false);
            ValidatePartNumber(partNumber);

            await using (ObjectReadHandle? handle = await _Reads.ReadAsync(sourceContainer, sourceKey, rangeStart, rangeCount, token).ConfigureAwait(false))
            {
                if (handle == null) throw new ObjectNotFoundException(sourceContainer, sourceKey);
                MultipartStageResult stage = await _Storage.WritePartAsync(uploadId, partNumber, handle.Payload, token).ConfigureAwait(false);
                return await UpsertStagedPartAsync(upload.Id, partNumber, stage, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Complete a multipart upload: validate the listed parts, assemble them (in ascending order) into a
        /// single immutable extent, persist the multipart ETag, and reclaim the staged parts.
        /// </summary>
        /// <param name="containerName">Container (bucket) name.</param>
        /// <param name="uploadId">Upload id.</param>
        /// <param name="request">The client's ordered list of parts and their ETags.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The completion result including the multipart ETag.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        /// <exception cref="NoSuchUploadException">The upload does not exist or was completed concurrently.</exception>
        /// <exception cref="InvalidPartOrderException">The parts are not strictly ascending.</exception>
        /// <exception cref="InvalidPartException">A listed part is missing or its ETag does not match.</exception>
        /// <exception cref="EntityTooSmallException">A non-final part is below the minimum part size.</exception>
        public async Task<CompleteMultipartUploadResponse> CompleteAsync(string containerName, string uploadId, CompleteMultipartUploadRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MultipartUpload upload = await RequireUploadAsync(containerName, uploadId, token).ConfigureAwait(false);

            IReadOnlyList<MultipartPart> staged = await _Db.MultipartUploads.ListAllPartsAsync(uploadId, token).ConfigureAwait(false);
            List<MultipartPart> ordered = ValidateAndOrder(request, staged);

            // Claim the upload so a concurrent completion of the same id loses the race and gets NoSuchUpload.
            // Deleting the row cascades the part rows, but the staged blobs on disk survive until we remove
            // them below (their locations are captured in 'ordered'); a crash after the claim leaves orphaned
            // staged blobs that the janitor reclaims.
            bool claimed = await _Db.MultipartUploads.DeleteUploadAsync(uploadId, token).ConfigureAwait(false);
            if (!claimed) throw new NoSuchUploadException(uploadId);

            string etag = ComputeMultipartEtag(ordered);

            List<Func<CancellationToken, Task<Stream>>> openers = new List<Func<CancellationToken, Task<Stream>>>();
            long totalLength = 0;
            foreach (MultipartPart part in ordered)
            {
                string location = part.StorageLocation;
                openers.Add(ct => _Storage.OpenPartAsync(location, ct));
                totalLength += part.SizeBytes;
            }

            ObjectWriteResponse write;
            using (ConcatReadStream concat = new ConcatReadStream(openers, totalLength))
            {
                write = await _Writes.WriteAsync(
                    containerName,
                    upload.Key,
                    concat,
                    upload.ContentType,
                    null,
                    upload.Tags,
                    null,
                    false,
                    token,
                    etag).ConfigureAwait(false);
            }

            await _Storage.DeletePartsAsync(uploadId, token).ConfigureAwait(false);
            _Logging?.Debug(_Header + "completed upload " + uploadId + " -> " + containerName + "/" + upload.Key + " etag=" + etag);

            return new CompleteMultipartUploadResponse
            {
                ContainerName = containerName,
                Key = upload.Key,
                ETag = etag,
                ExtentId = write.ExtentId,
                SizeBytes = write.SizeBytes
            };
        }

        /// <summary>
        /// Abort a multipart upload: delete its part rows and staged blobs. Idempotent — aborting an unknown
        /// upload succeeds without error.
        /// </summary>
        /// <param name="containerName">Container (bucket) name.</param>
        /// <param name="uploadId">Upload id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        public async Task AbortAsync(string containerName, string uploadId, CancellationToken token = default)
        {
            await RequireContainerAsync(containerName, token).ConfigureAwait(false);
            await _Db.MultipartUploads.DeleteUploadAsync(uploadId, token).ConfigureAwait(false);
            await _Storage.DeletePartsAsync(uploadId, token).ConfigureAwait(false);
            _Logging?.Debug(_Header + "aborted upload " + uploadId);
        }

        /// <summary>
        /// List staged parts for an upload (paginated).
        /// </summary>
        /// <param name="containerName">Container (bucket) name.</param>
        /// <param name="uploadId">Upload id.</param>
        /// <param name="partNumberMarker">Exclusive part-number marker; 0 or less starts at the beginning.</param>
        /// <param name="maxParts">Maximum parts to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of parts.</returns>
        /// <exception cref="NoSuchUploadException">The upload does not exist.</exception>
        public async Task<MultipartPartListResult> ListPartsAsync(string containerName, string uploadId, int partNumberMarker, int maxParts, CancellationToken token = default)
        {
            await RequireUploadAsync(containerName, uploadId, token).ConfigureAwait(false);
            return await _Db.MultipartUploads.ListPartsAsync(uploadId, partNumberMarker, maxParts, token).ConfigureAwait(false);
        }

        /// <summary>
        /// List in-progress uploads for a container (paginated).
        /// </summary>
        /// <param name="containerName">Container (bucket) name.</param>
        /// <param name="keyMarker">Exclusive key marker, or null.</param>
        /// <param name="uploadIdMarker">Exclusive upload-id marker paired with the key marker, or null.</param>
        /// <param name="maxUploads">Maximum uploads to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of uploads.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        public async Task<MultipartUploadListResult> ListUploadsAsync(string containerName, string? keyMarker, string? uploadIdMarker, int maxUploads, CancellationToken token = default)
        {
            Container container = await RequireContainerAsync(containerName, token).ConfigureAwait(false);
            return await _Db.MultipartUploads.ListUploadsAsync(container.Id, keyMarker, uploadIdMarker, maxUploads, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<MultipartPart> UpsertStagedPartAsync(string uploadId, int partNumber, MultipartStageResult stage, CancellationToken token)
        {
            MultipartPart part = new MultipartPart
            {
                UploadId = uploadId,
                PartNumber = partNumber,
                SizeBytes = stage.SizeBytes,
                Md5 = stage.Md5,
                Sha256 = stage.Sha256,
                StorageLocation = stage.Location
            };

            string? superseded = await _Db.MultipartUploads.UpsertPartAsync(part, token).ConfigureAwait(false);
            if (superseded != null)
            {
                try
                {
                    await _Storage.DeletePartAsync(superseded, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging?.Debug(_Header + "failed to delete superseded staged part " + superseded + ": " + ex.Message);
                }
            }

            return part;
        }

        private List<MultipartPart> ValidateAndOrder(CompleteMultipartUploadRequest request, IReadOnlyList<MultipartPart> staged)
        {
            if (request.Parts.Count == 0) throw new InvalidPartOrderException();

            Dictionary<int, MultipartPart> byNumber = new Dictionary<int, MultipartPart>();
            foreach (MultipartPart p in staged) byNumber[p.PartNumber] = p;

            List<MultipartPart> ordered = new List<MultipartPart>();
            int previous = 0;
            for (int i = 0; i < request.Parts.Count; i++)
            {
                CompletedPart requested = request.Parts[i];
                if (requested.PartNumber <= previous) throw new InvalidPartOrderException();
                previous = requested.PartNumber;

                if (!byNumber.TryGetValue(requested.PartNumber, out MultipartPart? staged1) || staged1 == null)
                    throw new InvalidPartException(requested.PartNumber);

                string requestedEtag = requested.ETag.Trim().Trim('"').ToLowerInvariant();
                if (!String.Equals(requestedEtag, staged1.Md5, StringComparison.Ordinal))
                    throw new InvalidPartException(requested.PartNumber);

                bool isLast = i == request.Parts.Count - 1;
                if (!isLast && _Settings.MultipartMinPartBytes > 0 && staged1.SizeBytes < _Settings.MultipartMinPartBytes)
                    throw new EntityTooSmallException(requested.PartNumber, _Settings.MultipartMinPartBytes);

                ordered.Add(staged1);
            }

            return ordered;
        }

        private static string ComputeMultipartEtag(List<MultipartPart> ordered)
        {
            byte[] concat = new byte[ordered.Count * 16];
            for (int i = 0; i < ordered.Count; i++)
            {
                byte[] digest = Convert.FromHexString(ordered[i].Md5);
                Array.Copy(digest, 0, concat, i * 16, 16);
            }

            byte[] hash = MD5.HashData(concat);
            return Convert.ToHexString(hash).ToLowerInvariant() + "-" + ordered.Count;
        }

        private void ValidatePartNumber(int partNumber)
        {
            if (partNumber < 1 || partNumber > _Settings.MultipartMaxParts)
                throw new PepperXException(PepperX.Core.Enums.ApiErrorEnum.BadRequest, 400, "Part number must be between 1 and " + _Settings.MultipartMaxParts + ".");
        }

        private async Task<Container> RequireContainerAsync(string name, CancellationToken token)
        {
            Container? container = await _Db.Containers.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (container == null) throw new ContainerNotFoundException(name);
            return container;
        }

        private async Task<MultipartUpload> RequireUploadAsync(string containerName, string uploadId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(uploadId)) throw new NoSuchUploadException(uploadId ?? String.Empty);
            Container container = await RequireContainerAsync(containerName, token).ConfigureAwait(false);
            MultipartUpload? upload = await _Db.MultipartUploads.ReadUploadAsync(uploadId, token).ConfigureAwait(false);
            if (upload == null || !String.Equals(upload.ContainerId, container.Id, StringComparison.Ordinal)) throw new NoSuchUploadException(uploadId);
            return upload;
        }

        #endregion
    }
}
