namespace PepperX.Server.Api.S3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using PepperX.Core.Database;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using PepperX.Core.Settings;
    using PepperX.Core.Telemetry;
    using S3ServerLibrary;
    using S3ServerLibrary.S3Objects;
    using SyslogLogging;
    using WatsonWebserver.Core;

    /// <summary>
    /// Hosts the S3-compatible protocol surface (buckets, objects, and tags) over the shared PepperX service
    /// layer. Buckets map to containers and object keys map to extent keys. Unsupported S3 operations are left
    /// unimplemented and return the library's default error.
    /// </summary>
    public sealed class S3ProtocolHandler
    {
        #region Private-Members

        private readonly ContainerService _Containers;
        private readonly ObjectWriteService _Writes;
        private readonly ObjectReadService _Reads;
        private readonly ObjectDeleteService _Deletes;
        private readonly SearchService _Search;
        private readonly MultipartUploadService _Multipart;
        private readonly IMetadataDatabaseDriver _Db;
        private readonly S3Settings _Settings;
        private readonly LoggingModule? _Logging;
        private readonly Owner _Owner = new Owner("pepperx", "pepperx");

        private S3Server? _Server;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the S3 protocol handler.
        /// </summary>
        /// <param name="containers">Container service.</param>
        /// <param name="writes">Write service.</param>
        /// <param name="reads">Read service.</param>
        /// <param name="deletes">Delete service.</param>
        /// <param name="search">Search service.</param>
        /// <param name="multipart">Multipart upload service.</param>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="settings">S3 settings.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public S3ProtocolHandler(ContainerService containers, ObjectWriteService writes, ObjectReadService reads, ObjectDeleteService deletes, SearchService search, MultipartUploadService multipart, IMetadataDatabaseDriver db, S3Settings settings, LoggingModule? logging)
        {
            _Containers = containers ?? throw new ArgumentNullException(nameof(containers));
            _Writes = writes ?? throw new ArgumentNullException(nameof(writes));
            _Reads = reads ?? throw new ArgumentNullException(nameof(reads));
            _Deletes = deletes ?? throw new ArgumentNullException(nameof(deletes));
            _Search = search ?? throw new ArgumentNullException(nameof(search));
            _Multipart = multipart ?? throw new ArgumentNullException(nameof(multipart));
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the S3 listener.
        /// </summary>
        public void Start()
        {
            S3ServerSettings settings = new S3ServerSettings();
            settings.Webserver = new WebserverSettings(_Settings.Hostname, _Settings.Port, _Settings.Ssl);

            _Server = new S3Server(settings);

            _Server.Service.IsAnonymousRequestAllowed = _ => Task.FromResult(_Settings.AllowAnonymous);
            _Server.Service.GetSecretKey = _ => _Settings.StaticSecretKey;
            _Server.Service.ListBuckets = InstrumentResult("ListBuckets", ListBucketsAsync);

            _Server.Bucket.Exists = InstrumentResult("BucketExists", BucketExistsAsync);
            _Server.Bucket.Write = InstrumentVoid("BucketWrite", BucketWriteAsync);
            _Server.Bucket.Delete = InstrumentVoid("BucketDelete", BucketDeleteAsync);
            _Server.Bucket.Read = InstrumentResult("BucketRead", BucketReadAsync);
            _Server.Bucket.ReadTagging = InstrumentResult("BucketReadTagging", BucketReadTaggingAsync);
            _Server.Bucket.WriteTagging = InstrumentVoid<Tagging>("BucketWriteTagging", BucketWriteTaggingAsync);
            _Server.Bucket.DeleteTagging = InstrumentVoid("BucketDeleteTagging", BucketDeleteTaggingAsync);
            _Server.Bucket.ReadLocation = InstrumentResult("BucketReadLocation", BucketReadLocationAsync);

            _Server.Object.Write = InstrumentVoid("ObjectWrite", ObjectWriteAsync);
            _Server.Object.Read = InstrumentResult("ObjectRead", ObjectReadAsync);
            _Server.Object.ReadRange = InstrumentResult("ObjectReadRange", ObjectReadRangeAsync);
            _Server.Object.Exists = InstrumentResult("ObjectExists", ObjectExistsAsync);
            _Server.Object.Delete = InstrumentVoid("ObjectDelete", ObjectDeleteAsync);
            _Server.Object.DeleteMultiple = InstrumentResult<DeleteMultiple, DeleteResult>("ObjectDeleteMultiple", ObjectDeleteMultipleAsync);
            _Server.Object.ReadTagging = InstrumentResult("ObjectReadTagging", ObjectReadTaggingAsync);
            _Server.Object.WriteTagging = InstrumentVoid<Tagging>("ObjectWriteTagging", ObjectWriteTaggingAsync);
            _Server.Object.DeleteTagging = InstrumentVoid("ObjectDeleteTagging", ObjectDeleteTaggingAsync);

            if (_Settings.MultipartEnabled)
            {
                _Server.Object.CreateMultipartUpload = InstrumentResult("CreateMultipartUpload", CreateMultipartUploadAsync);
                _Server.Object.UploadPart = InstrumentVoid("UploadPart", UploadPartAsync);
                _Server.Object.CompleteMultipartUpload = InstrumentResult<CompleteMultipartUpload, CompleteMultipartUploadResult>("CompleteMultipartUpload", CompleteMultipartUploadAsync);
                _Server.Object.AbortMultipartUpload = InstrumentVoid("AbortMultipartUpload", AbortMultipartUploadAsync);
                _Server.Object.ReadParts = InstrumentResult("ReadParts", ReadPartsAsync);
                _Server.Bucket.ReadMultipartUploads = InstrumentResult("ReadMultipartUploads", ReadMultipartUploadsAsync);
            }

            _Server.Start();
        }

        /// <summary>
        /// Stop the S3 listener.
        /// </summary>
        public void Stop()
        {
            try { _Server?.Stop(); } catch (Exception) { }
            _Server?.Dispose();
            _Server = null;
        }

        #endregion

        #region Private-Methods-Telemetry

        private Func<S3Context, Task<T>> InstrumentResult<T>(string operation, Func<S3Context, Task<T>> handler)
        {
            return async ctx =>
            {
                long startTs = Stopwatch.GetTimestamp();
                Activity? activity = PepperXTelemetry.StartActivity("s3 " + operation, ActivityKind.Server);
                activity?.SetTag("pepperx.protocol", "s3");
                activity?.SetTag("pepperx.operation", operation);
                bool ok = true;
                try
                {
                    return await handler(ctx).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ok = false;
                    PepperXTelemetry.RecordException(activity, ex);
                    throw;
                }
                finally
                {
                    activity?.Dispose();
                    PepperXTelemetry.RecordS3(operation, Stopwatch.GetElapsedTime(startTs).TotalSeconds, ok);
                }
            };
        }

        private Func<S3Context, Task> InstrumentVoid(string operation, Func<S3Context, Task> handler)
        {
            return async ctx =>
            {
                long startTs = Stopwatch.GetTimestamp();
                Activity? activity = PepperXTelemetry.StartActivity("s3 " + operation, ActivityKind.Server);
                activity?.SetTag("pepperx.protocol", "s3");
                activity?.SetTag("pepperx.operation", operation);
                bool ok = true;
                try
                {
                    await handler(ctx).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ok = false;
                    PepperXTelemetry.RecordException(activity, ex);
                    throw;
                }
                finally
                {
                    activity?.Dispose();
                    PepperXTelemetry.RecordS3(operation, Stopwatch.GetElapsedTime(startTs).TotalSeconds, ok);
                }
            };
        }

        private Func<S3Context, TArg, Task<T>> InstrumentResult<TArg, T>(string operation, Func<S3Context, TArg, Task<T>> handler)
        {
            return async (ctx, arg) =>
            {
                long startTs = Stopwatch.GetTimestamp();
                Activity? activity = PepperXTelemetry.StartActivity("s3 " + operation, ActivityKind.Server);
                activity?.SetTag("pepperx.protocol", "s3");
                activity?.SetTag("pepperx.operation", operation);
                bool ok = true;
                try
                {
                    return await handler(ctx, arg).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ok = false;
                    PepperXTelemetry.RecordException(activity, ex);
                    throw;
                }
                finally
                {
                    activity?.Dispose();
                    PepperXTelemetry.RecordS3(operation, Stopwatch.GetElapsedTime(startTs).TotalSeconds, ok);
                }
            };
        }

        private Func<S3Context, TArg, Task> InstrumentVoid<TArg>(string operation, Func<S3Context, TArg, Task> handler)
        {
            return async (ctx, arg) =>
            {
                long startTs = Stopwatch.GetTimestamp();
                Activity? activity = PepperXTelemetry.StartActivity("s3 " + operation, ActivityKind.Server);
                activity?.SetTag("pepperx.protocol", "s3");
                activity?.SetTag("pepperx.operation", operation);
                bool ok = true;
                try
                {
                    await handler(ctx, arg).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ok = false;
                    PepperXTelemetry.RecordException(activity, ex);
                    throw;
                }
                finally
                {
                    activity?.Dispose();
                    PepperXTelemetry.RecordS3(operation, Stopwatch.GetElapsedTime(startTs).TotalSeconds, ok);
                }
            };
        }

        #endregion

        #region Private-Methods-Service

        private async Task<ListAllMyBucketsResult> ListBucketsAsync(S3Context ctx)
        {
            try
            {
                EnumerationResult<Container> page = await _Db.Containers.EnumerateAsync(new EnumerationQuery { MaxResults = 1000 }, ctx.Http.Token).ConfigureAwait(false);
                List<Bucket> list = new List<Bucket>();
                foreach (Container container in page.Objects) list.Add(new Bucket(container.Name, container.CreatedUtc));
                return new ListAllMyBucketsResult(_Owner, new Buckets(list));
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex);
            }
        }

        #endregion

        #region Private-Methods-Bucket

        private async Task<bool> BucketExistsAsync(S3Context ctx)
        {
            return await _Containers.ExistsAsync(ctx.Request.Bucket, ctx.Http.Token).ConfigureAwait(false);
        }

        private async Task BucketWriteAsync(S3Context ctx)
        {
            try
            {
                await _Containers.CreateAsync(new ContainerCreateRequest { Name = ctx.Request.Bucket }, ctx.Http.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex);
            }
        }

        private async Task BucketDeleteAsync(S3Context ctx)
        {
            try
            {
                await _Containers.DeleteAsync(ctx.Request.Bucket, false, ctx.Http.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex);
            }
        }

        private async Task<ListBucketResult> BucketReadAsync(S3Context ctx)
        {
            try
            {
                EnumerationQuery query = new EnumerationQuery
                {
                    MaxResults = ctx.Request.MaxKeys > 0 ? Math.Min(ctx.Request.MaxKeys, 1000) : 1000,
                    Prefix = String.IsNullOrEmpty(ctx.Request.Prefix) ? null : ctx.Request.Prefix,
                    Ordering = EnumerationOrderEnum.KeyAscending
                };

                EnumerationResult<PepperX.Core.Models.ObjectMetadata> page = await _Search.EnumerateContainerAsync(ctx.Request.Bucket, query, ctx.Http.Token).ConfigureAwait(false);

                ListBucketResult result = new ListBucketResult
                {
                    Name = ctx.Request.Bucket,
                    Prefix = ctx.Request.Prefix,
                    MaxKeys = query.MaxResults,
                    IsTruncated = !page.EndOfResults
                };

                foreach (PepperX.Core.Models.ObjectMetadata meta in page.Objects)
                {
                    result.Contents.Add(new S3ServerLibrary.S3Objects.ObjectMetadata(meta.Key, meta.CreatedUtc, "\"" + ResolveEtag(meta.Etag, meta.Md5, meta.Sha256) + "\"", meta.SizeBytes, _Owner));
                }

                return result;
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex);
            }
        }

        private async Task<Tagging> BucketReadTaggingAsync(S3Context ctx)
        {
            try
            {
                ContainerResponse? container = await _Containers.ReadAsync(ctx.Request.Bucket, ctx.Http.Token).ConfigureAwait(false);
                if (container == null) throw new PepperX.Core.Exceptions.ContainerNotFoundException(ctx.Request.Bucket);
                return ToTagging(container.Tags);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex);
            }
        }

        private async Task BucketWriteTaggingAsync(S3Context ctx, Tagging tagging)
        {
            try
            {
                await _Containers.UpdateTagsAsync(ctx.Request.Bucket, FromTagging(tagging), ctx.Http.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex);
            }
        }

        private async Task BucketDeleteTaggingAsync(S3Context ctx)
        {
            try
            {
                await _Containers.UpdateTagsAsync(ctx.Request.Bucket, new Dictionary<string, string>(), ctx.Http.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex);
            }
        }

        private Task<LocationConstraint> BucketReadLocationAsync(S3Context ctx)
        {
            return Task.FromResult(new LocationConstraint(_Settings.Region));
        }

        #endregion

        #region Private-Methods-Object

        private async Task ObjectWriteAsync(S3Context ctx)
        {
            try
            {
                Dictionary<string, string>? tags = ParseAmzTagging(ctx);
                Stream payload = ctx.Request.Data ?? Stream.Null;

                string? contentSha = ctx.Http.Request.Headers["x-amz-content-sha256"];
                if (!String.IsNullOrEmpty(contentSha) && contentSha.StartsWith("STREAMING", StringComparison.OrdinalIgnoreCase))
                {
                    payload = new AwsChunkedStream(payload);
                }

                await _Writes.WriteAsync(ctx.Request.Bucket, ctx.Request.Key, payload, ctx.Request.ContentType, null, tags, null, false, ctx.Http.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task<S3Object> ObjectReadAsync(S3Context ctx)
        {
            try
            {
                await using (ObjectReadHandle? handle = await _Reads.ReadAsync(ctx.Request.Bucket, ctx.Request.Key, null, null, ctx.Http.Token).ConfigureAwait(false))
                {
                    if (handle == null) throw new PepperX.Core.Exceptions.ObjectNotFoundException(ctx.Request.Bucket, ctx.Request.Key);

                    MemoryStream buffer = new MemoryStream();
                    await handle.Payload.CopyToAsync(buffer, ctx.Http.Token).ConfigureAwait(false);

                    // Prefer the stored S3 ETag (multipart) or content MD5; only compute MD5 for a legacy
                    // object that predates MD5 recording.
                    string etag = handle.Extent.Etag ?? handle.Extent.Md5
                        ?? Convert.ToHexString(System.Security.Cryptography.MD5.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))).ToLowerInvariant();
                    buffer.Position = 0;

                    return new S3Object
                    {
                        Key = ctx.Request.Key,
                        ContentType = handle.Extent.ContentType ?? "application/octet-stream",
                        ETag = "\"" + etag + "\"",
                        Size = buffer.Length,
                        Data = buffer
                    };
                }
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task<S3Object> ObjectReadRangeAsync(S3Context ctx)
        {
            // No parsed range start means this is not actually a range request; serve the full object.
            if (ctx.Request.RangeStart == null) return await ObjectReadAsync(ctx).ConfigureAwait(false);

            try
            {
                PepperX.Core.Models.ObjectMetadata? meta = await _Reads.ReadMetadataAsync(ctx.Request.Bucket, ctx.Request.Key, ctx.Http.Token).ConfigureAwait(false);
                if (meta == null) throw new PepperX.Core.Exceptions.ObjectNotFoundException(ctx.Request.Bucket, ctx.Request.Key);

                long total = meta.SizeBytes;
                long start = ctx.Request.RangeStart.Value;
                long end = ctx.Request.RangeEnd ?? (total - 1);
                if (end > total - 1) end = total - 1;
                if (start < 0 || start >= total || start > end) throw new S3Exception(new Error(ErrorCode.InvalidRange, ctx.Request.Key));

                long count = end - start + 1;

                await using (ObjectReadHandle? handle = await _Reads.ReadAsync(ctx.Request.Bucket, ctx.Request.Key, start, count, ctx.Http.Token).ConfigureAwait(false))
                {
                    if (handle == null) throw new PepperX.Core.Exceptions.ObjectNotFoundException(ctx.Request.Bucket, ctx.Request.Key);

                    MemoryStream buffer = new MemoryStream();
                    await handle.Payload.CopyToAsync(buffer, ctx.Http.Token).ConfigureAwait(false);
                    buffer.Position = 0;

                    // The ETag identifies the whole object, so use the stored object ETag/MD5, never a hash of
                    // the partial range. TotalSize lets S3Server emit Content-Range: bytes start-end/total.
                    string etag = ResolveEtag(handle.Extent.Etag, handle.Extent.Md5, handle.Extent.Sha256);

                    return new S3Object
                    {
                        Key = ctx.Request.Key,
                        ContentType = handle.Extent.ContentType ?? "application/octet-stream",
                        ETag = "\"" + etag + "\"",
                        Size = buffer.Length,
                        TotalSize = total,
                        Data = buffer
                    };
                }
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task<S3ServerLibrary.S3Objects.ObjectMetadata> ObjectExistsAsync(S3Context ctx)
        {
            try
            {
                PepperX.Core.Models.ObjectMetadata? meta = await _Reads.ReadMetadataAsync(ctx.Request.Bucket, ctx.Request.Key, ctx.Http.Token).ConfigureAwait(false);
                if (meta == null) throw new PepperX.Core.Exceptions.ObjectNotFoundException(ctx.Request.Bucket, ctx.Request.Key);
                return new S3ServerLibrary.S3Objects.ObjectMetadata(meta.Key, meta.CreatedUtc, "\"" + ResolveEtag(meta.Etag, meta.Md5, meta.Sha256) + "\"", meta.SizeBytes, _Owner);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task ObjectDeleteAsync(S3Context ctx)
        {
            try
            {
                bool deleted = await _Deletes.DeleteAsync(ctx.Request.Bucket, ctx.Request.Key, ctx.Http.Token).ConfigureAwait(false);
                if (!deleted) throw new PepperX.Core.Exceptions.ObjectNotFoundException(ctx.Request.Bucket, ctx.Request.Key);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task<DeleteResult> ObjectDeleteMultipleAsync(S3Context ctx, DeleteMultiple request)
        {
            DeleteResult result = new DeleteResult();
            if (request?.Objects == null) return result;

            foreach (S3ServerLibrary.S3Objects.Object obj in request.Objects)
            {
                try
                {
                    await _Deletes.DeleteAsync(ctx.Request.Bucket, obj.Key, ctx.Http.Token).ConfigureAwait(false);
                    result.DeletedObjects.Add(new Deleted(obj.Key, null, null));
                }
                catch (Exception)
                {
                    result.Errors.Add(new Error(ErrorCode.InternalError, obj.Key));
                }
            }

            return result;
        }

        private async Task<Tagging> ObjectReadTaggingAsync(S3Context ctx)
        {
            try
            {
                PepperX.Core.Models.ObjectMetadata? meta = await _Reads.ReadMetadataAsync(ctx.Request.Bucket, ctx.Request.Key, ctx.Http.Token).ConfigureAwait(false);
                if (meta == null) throw new PepperX.Core.Exceptions.ObjectNotFoundException(ctx.Request.Bucket, ctx.Request.Key);
                return ToTagging(meta.Tags);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task ObjectWriteTaggingAsync(S3Context ctx, Tagging tagging)
        {
            try
            {
                await _Writes.UpdateMetadataAsync(ctx.Request.Bucket, ctx.Request.Key, new UpdateMetadataRequest { Tags = FromTagging(tagging) }, ctx.Http.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task ObjectDeleteTaggingAsync(S3Context ctx)
        {
            try
            {
                await _Writes.UpdateMetadataAsync(ctx.Request.Bucket, ctx.Request.Key, new UpdateMetadataRequest { Tags = new Dictionary<string, string>() }, ctx.Http.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        #endregion

        #region Private-Methods-Multipart

        private async Task<InitiateMultipartUploadResult> CreateMultipartUploadAsync(S3Context ctx)
        {
            try
            {
                Dictionary<string, string>? tags = ParseAmzTagging(ctx);
                PepperX.Core.Models.MultipartUpload upload = await _Multipart.InitiateAsync(ctx.Request.Bucket, ctx.Request.Key, ctx.Request.ContentType, tags, ctx.Http.Token).ConfigureAwait(false);
                return new InitiateMultipartUploadResult(ctx.Request.Bucket, ctx.Request.Key, upload.Id);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task UploadPartAsync(S3Context ctx)
        {
            try
            {
                string? copySource = ctx.Http.Request.Headers["x-amz-copy-source"];
                if (!String.IsNullOrEmpty(copySource))
                {
                    await UploadPartCopyAsync(ctx, copySource).ConfigureAwait(false);
                    return;
                }

                Stream payload = ctx.Request.Data ?? Stream.Null;
                string? contentSha = ctx.Http.Request.Headers["x-amz-content-sha256"];
                if (!String.IsNullOrEmpty(contentSha) && contentSha.StartsWith("STREAMING", StringComparison.OrdinalIgnoreCase))
                {
                    payload = new AwsChunkedStream(payload);
                }

                PepperX.Core.Models.MultipartPart part = await _Multipart.UploadPartAsync(ctx.Request.Bucket, ctx.Request.UploadId, ctx.Request.PartNumber, payload, ctx.Http.Token).ConfigureAwait(false);
                ctx.Response.Headers.Add("ETag", "\"" + part.Md5 + "\"");
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task UploadPartCopyAsync(S3Context ctx, string copySource)
        {
            string source = copySource.Trim();
            if (source.StartsWith("/", StringComparison.Ordinal)) source = source.Substring(1);

            int versionMarker = source.IndexOf('?');
            if (versionMarker >= 0) source = source.Substring(0, versionMarker);

            int slash = source.IndexOf('/');
            if (slash <= 0 || slash >= source.Length - 1) throw new PepperX.Core.Exceptions.PepperXException(PepperX.Core.Enums.ApiErrorEnum.BadRequest, 400, "Invalid x-amz-copy-source header.");

            string sourceBucket = HttpUtility.UrlDecode(source.Substring(0, slash));
            string sourceKey = HttpUtility.UrlDecode(source.Substring(slash + 1));

            long? rangeStart = null;
            long? rangeCount = null;
            string? rangeHeader = ctx.Http.Request.Headers["x-amz-copy-source-range"];
            if (!String.IsNullOrEmpty(rangeHeader)) ParseCopyRange(rangeHeader, out rangeStart, out rangeCount);

            PepperX.Core.Models.MultipartPart part = await _Multipart.UploadPartCopyAsync(ctx.Request.Bucket, ctx.Request.UploadId, ctx.Request.PartNumber, sourceBucket, sourceKey, rangeStart, rangeCount, ctx.Http.Token).ConfigureAwait(false);

            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<CopyPartResult>" +
                "<LastModified>" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "</LastModified>" +
                "<ETag>&quot;" + part.Md5 + "&quot;</ETag>" +
                "</CopyPartResult>";

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/xml";
            await ctx.Response.Send(xml).ConfigureAwait(false);
        }

        private async Task<CompleteMultipartUploadResult> CompleteMultipartUploadAsync(S3Context ctx, CompleteMultipartUpload upload)
        {
            try
            {
                CompleteMultipartUploadRequest request = new CompleteMultipartUploadRequest();
                if (upload?.Parts != null)
                {
                    foreach (Part part in upload.Parts) request.Parts.Add(new CompletedPart(part.PartNumber, part.ETag ?? String.Empty));
                }

                CompleteMultipartUploadResponse result = await _Multipart.CompleteAsync(ctx.Request.Bucket, ctx.Request.UploadId, request, ctx.Http.Token).ConfigureAwait(false);

                return new CompleteMultipartUploadResult
                {
                    Bucket = result.ContainerName,
                    Key = result.Key,
                    Location = "/" + result.ContainerName + "/" + result.Key,
                    ETag = "\"" + result.ETag + "\""
                };
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task AbortMultipartUploadAsync(S3Context ctx)
        {
            try
            {
                await _Multipart.AbortAsync(ctx.Request.Bucket, ctx.Request.UploadId, ctx.Http.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task<ListPartsResult> ReadPartsAsync(S3Context ctx)
        {
            try
            {
                int maxParts = ctx.Request.MaxParts > 0 ? ctx.Request.MaxParts : 1000;
                int marker = ctx.Request.PartNumberMarker > 0 ? ctx.Request.PartNumberMarker : 0;

                PepperX.Core.Responses.MultipartPartListResult page = await _Multipart.ListPartsAsync(ctx.Request.Bucket, ctx.Request.UploadId, marker, maxParts, ctx.Http.Token).ConfigureAwait(false);

                ListPartsResult result = new ListPartsResult
                {
                    Bucket = ctx.Request.Bucket,
                    Key = ctx.Request.Key,
                    UploadId = ctx.Request.UploadId,
                    MaxParts = maxParts,
                    PartNumberMarker = marker,
                    IsTruncated = page.IsTruncated,
                    NextPartNumberMarker = page.NextPartNumberMarker ?? 0,
                    Owner = _Owner,
                    Initiator = _Owner
                };

                foreach (PepperX.Core.Models.MultipartPart part in page.Parts)
                {
                    result.Parts.Add(new Part
                    {
                        PartNumber = part.PartNumber,
                        ETag = "\"" + part.Md5 + "\"",
                        Size = part.SizeBytes > Int32.MaxValue ? Int32.MaxValue : (int)part.SizeBytes,
                        LastModified = part.CreatedUtc
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Key);
            }
        }

        private async Task<ListMultipartUploadsResult> ReadMultipartUploadsAsync(S3Context ctx)
        {
            try
            {
                string? keyMarker = QueryParam(ctx, "key-marker");
                string? uploadIdMarker = QueryParam(ctx, "upload-id-marker");
                string? maxUploadsRaw = QueryParam(ctx, "max-uploads");
                int maxUploads = 1000;
                if (!String.IsNullOrEmpty(maxUploadsRaw) && Int32.TryParse(maxUploadsRaw, out int parsed)) maxUploads = Math.Clamp(parsed, 1, 1000);

                PepperX.Core.Responses.MultipartUploadListResult page = await _Multipart.ListUploadsAsync(ctx.Request.Bucket, keyMarker, uploadIdMarker, maxUploads, ctx.Http.Token).ConfigureAwait(false);

                ListMultipartUploadsResult result = new ListMultipartUploadsResult
                {
                    Bucket = ctx.Request.Bucket,
                    KeyMarker = keyMarker,
                    UploadIdMarker = uploadIdMarker,
                    MaxUploads = maxUploads,
                    IsTruncated = page.IsTruncated,
                    NextKeyMarker = page.NextKeyMarker,
                    NextUploadIdMarker = page.NextUploadIdMarker
                };

                foreach (PepperX.Core.Models.MultipartUpload upload in page.Uploads)
                {
                    result.Uploads.Add(new Upload
                    {
                        UploadId = upload.Id,
                        Key = upload.Key,
                        Initiated = upload.InitiatedUtc,
                        Owner = _Owner,
                        Initiator = _Owner
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                throw S3ErrorMapper.Map(ex, ctx.Request.Bucket);
            }
        }

        private static void ParseCopyRange(string header, out long? start, out long? count)
        {
            start = null;
            count = null;
            string value = header.Trim();
            if (value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) value = value.Substring("bytes=".Length);
            string[] parts = value.Split('-');
            if (parts.Length != 2) return;
            if (long.TryParse(parts[0], out long s) && long.TryParse(parts[1], out long e) && e >= s)
            {
                start = s;
                count = e - s + 1;
            }
        }

        private static string? QueryParam(S3Context ctx, string name)
        {
            string? raw = ctx.Http.Request.Url?.RawWithQuery;
            if (String.IsNullOrEmpty(raw)) return null;
            int q = raw.IndexOf('?');
            if (q < 0) return null;
            System.Collections.Specialized.NameValueCollection nvc = HttpUtility.ParseQueryString(raw.Substring(q + 1));
            string? value = nvc[name];
            return String.IsNullOrEmpty(value) ? null : value;
        }

        #endregion

        #region Private-Methods-Helpers

        private static string ResolveEtag(string? etag, string? md5, string sha256)
        {
            // Multipart-assembled objects carry an explicit S3 ETag (digest-N); single-part objects derive
            // it from the content MD5. Legacy objects written before MD5 was recorded fall back to SHA-256.
            if (!String.IsNullOrEmpty(etag)) return etag;
            if (!String.IsNullOrEmpty(md5)) return md5;
            return sha256;
        }

        private static Tagging ToTagging(Dictionary<string, string> tags)
        {
            List<Tag> list = new List<Tag>();
            foreach (KeyValuePair<string, string> tag in tags) list.Add(new Tag(tag.Key, tag.Value));
            return new Tagging(new TagSet(list));
        }

        private static Dictionary<string, string> FromTagging(Tagging tagging)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (tagging?.Tags?.Tags == null) return result;
            foreach (Tag tag in tagging.Tags.Tags)
            {
                if (!String.IsNullOrEmpty(tag.Key)) result[tag.Key] = tag.Value ?? String.Empty;
            }
            return result;
        }

        private static Dictionary<string, string>? ParseAmzTagging(S3Context ctx)
        {
            string? header = ctx.Http.Request.Headers["x-amz-tagging"];
            if (String.IsNullOrEmpty(header)) return null;

            Dictionary<string, string> tags = new Dictionary<string, string>();
            foreach (string pair in header.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                tags[HttpUtility.UrlDecode(pair.Substring(0, eq))] = HttpUtility.UrlDecode(pair.Substring(eq + 1));
            }
            return tags.Count > 0 ? tags : null;
        }

        #endregion
    }
}
