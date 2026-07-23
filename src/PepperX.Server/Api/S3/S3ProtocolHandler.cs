namespace PepperX.Server.Api.S3
{
    using System;
    using System.Collections.Generic;
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
        /// <param name="db">Metadata database driver.</param>
        /// <param name="settings">S3 settings.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public S3ProtocolHandler(ContainerService containers, ObjectWriteService writes, ObjectReadService reads, ObjectDeleteService deletes, SearchService search, IMetadataDatabaseDriver db, S3Settings settings, LoggingModule? logging)
        {
            _Containers = containers ?? throw new ArgumentNullException(nameof(containers));
            _Writes = writes ?? throw new ArgumentNullException(nameof(writes));
            _Reads = reads ?? throw new ArgumentNullException(nameof(reads));
            _Deletes = deletes ?? throw new ArgumentNullException(nameof(deletes));
            _Search = search ?? throw new ArgumentNullException(nameof(search));
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
            _Server.Service.ListBuckets = ListBucketsAsync;

            _Server.Bucket.Exists = BucketExistsAsync;
            _Server.Bucket.Write = BucketWriteAsync;
            _Server.Bucket.Delete = BucketDeleteAsync;
            _Server.Bucket.Read = BucketReadAsync;
            _Server.Bucket.ReadTagging = BucketReadTaggingAsync;
            _Server.Bucket.WriteTagging = BucketWriteTaggingAsync;
            _Server.Bucket.DeleteTagging = BucketDeleteTaggingAsync;
            _Server.Bucket.ReadLocation = BucketReadLocationAsync;

            _Server.Object.Write = ObjectWriteAsync;
            _Server.Object.Read = ObjectReadAsync;
            _Server.Object.ReadRange = ObjectReadAsync;
            _Server.Object.Exists = ObjectExistsAsync;
            _Server.Object.Delete = ObjectDeleteAsync;
            _Server.Object.DeleteMultiple = ObjectDeleteMultipleAsync;
            _Server.Object.ReadTagging = ObjectReadTaggingAsync;
            _Server.Object.WriteTagging = ObjectWriteTaggingAsync;
            _Server.Object.DeleteTagging = ObjectDeleteTaggingAsync;

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
                    result.Contents.Add(new S3ServerLibrary.S3Objects.ObjectMetadata(meta.Key, meta.CreatedUtc, "\"" + meta.Sha256 + "\"", meta.SizeBytes, _Owner));
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
                    string etag = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))).ToLowerInvariant();
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

        private async Task<S3ServerLibrary.S3Objects.ObjectMetadata> ObjectExistsAsync(S3Context ctx)
        {
            try
            {
                PepperX.Core.Models.ObjectMetadata? meta = await _Reads.ReadMetadataAsync(ctx.Request.Bucket, ctx.Request.Key, ctx.Http.Token).ConfigureAwait(false);
                if (meta == null) throw new PepperX.Core.Exceptions.ObjectNotFoundException(ctx.Request.Bucket, ctx.Request.Key);
                return new S3ServerLibrary.S3Objects.ObjectMetadata(meta.Key, meta.CreatedUtc, "\"" + meta.Sha256 + "\"", meta.SizeBytes, _Owner);
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

        #region Private-Methods-Helpers

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
