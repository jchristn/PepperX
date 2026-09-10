namespace PepperX.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Telemetry;

    /// <summary>
    /// Enumerates and searches objects by key, labels, and tags, both within a container and across
    /// containers. List results omit the freeform metadata object for performance; the object is available
    /// via a metadata read.
    /// </summary>
    public sealed class SearchService
    {
        #region Private-Members

        private readonly IMetadataDatabaseDriver _Db;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the search service.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <exception cref="ArgumentNullException"><paramref name="db"/> is null.</exception>
        public SearchService(IMetadataDatabaseDriver db)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enumerate objects within a container.
        /// </summary>
        /// <param name="containerName">Container name.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of object metadata.</returns>
        /// <exception cref="ContainerNotFoundException">The container does not exist.</exception>
        public async Task<EnumerationResult<ObjectMetadata>> EnumerateContainerAsync(string containerName, EnumerationQuery query, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("search.container", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                if (query == null) throw new ArgumentNullException(nameof(query));

                Container? container = await _Db.Containers.ReadByNameAsync(containerName, token).ConfigureAwait(false);
                if (container == null) throw new ContainerNotFoundException(containerName);

                EnumerationResult<Extent> page = await _Db.Extents.EnumerateAsync(container.Id, query, token).ConfigureAwait(false);
                Dictionary<string, string> names = new Dictionary<string, string> { { container.Id, container.Name } };
                return await MapAsync(page, names, token).ConfigureAwait(false);
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordSearch("container", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        /// <summary>
        /// Search objects across all containers (optionally restricted by the query's container list).
        /// </summary>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of object metadata.</returns>
        public async Task<EnumerationResult<ObjectMetadata>> SearchAllAsync(EnumerationQuery query, CancellationToken token = default)
        {
            long __ts = Stopwatch.GetTimestamp();
            using Activity? __act = PepperXTelemetry.StartActivity("search.all", ActivityKind.Internal);
            bool __ok = true;
            try
            {
                if (query == null) throw new ArgumentNullException(nameof(query));

                EnumerationResult<Extent> page = await _Db.Extents.EnumerateAsync(null, query, token).ConfigureAwait(false);
                return await MapAsync(page, new Dictionary<string, string>(), token).ConfigureAwait(false);
            }
            catch (Exception __ex)
            {
                __ok = false;
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
            finally
            {
                PepperXTelemetry.RecordSearch("all", Stopwatch.GetElapsedTime(__ts).TotalSeconds, __ok);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<EnumerationResult<ObjectMetadata>> MapAsync(EnumerationResult<Extent> page, Dictionary<string, string> nameCache, CancellationToken token)
        {
            List<ObjectMetadata> mapped = new List<ObjectMetadata>();
            foreach (Extent extent in page.Objects)
            {
                if (!nameCache.TryGetValue(extent.ContainerId, out string? name))
                {
                    Container? container = await _Db.Containers.ReadByIdAsync(extent.ContainerId, token).ConfigureAwait(false);
                    name = container?.Name;
                    nameCache[extent.ContainerId] = name ?? String.Empty;
                }

                mapped.Add(new ObjectMetadata
                {
                    Key = extent.Key,
                    ExtentId = extent.Id,
                    ContainerId = extent.ContainerId,
                    ContainerName = String.IsNullOrEmpty(name) ? null : name,
                    SizeBytes = extent.SizeBytes,
                    Sha256 = extent.Sha256,
                    Md5 = extent.Md5,
                    Etag = extent.Etag,
                    ContentType = extent.ContentType,
                    Labels = new List<string>(extent.Labels),
                    Tags = new Dictionary<string, string>(extent.Tags),
                    Object = null,
                    HasMetadataObject = extent.HasMetadataObject,
                    CreatedUtc = extent.CreatedUtc
                });
            }

            return new EnumerationResult<ObjectMetadata>
            {
                Success = page.Success,
                StartUtc = page.StartUtc,
                EndUtc = page.EndUtc,
                MaxResults = page.MaxResults,
                ContinuationToken = page.ContinuationToken,
                EndOfResults = page.EndOfResults,
                TotalRecords = page.TotalRecords,
                RecordsRemaining = page.RecordsRemaining,
                Objects = mapped
            };
        }

        #endregion
    }
}
