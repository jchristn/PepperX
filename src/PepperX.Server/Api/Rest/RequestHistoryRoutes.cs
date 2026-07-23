namespace PepperX.Server.Api.Rest
{
    using System;
    using System.Globalization;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Enums;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Settings;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using ApiErrorResponse = PepperX.Core.Responses.ApiErrorResponse;

    /// <summary>
    /// Request history (observability) routes. When capture is disabled, list and summary routes return
    /// empty results rather than errors.
    /// </summary>
    public sealed class RequestHistoryRoutes
    {
        #region Private-Members

        private readonly IMetadataDatabaseDriver _Db;
        private readonly RequestHistorySettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate request history routes.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="settings">Request history settings.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public RequestHistoryRoutes(IMetadataDatabaseDriver db, RequestHistorySettings settings)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the request history routes.
        /// </summary>
        /// <param name="server">Web server.</param>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Get("/v1.0/api/request-history", ListAsync, openApi => openApi
                .WithTag("Request History").WithDescription("List captured requests (bodies omitted). Filters: method, statusCode, pathContains, fromUtc, toUtc, pageNumber, pageSize.")
                .WithResponse(200, OpenApiResponseMetadata.Json("Paginated request history", null)));

            server.Get("/v1.0/api/request-history/summary", SummaryAsync, openApi => openApi
                .WithTag("Request History").WithDescription("Time-bucketed request summary. Required: fromUtc, toUtc, bucketMinutes.")
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Range start (ISO-8601)", true))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Range end (ISO-8601)", true))
                .WithParameter(OpenApiParameterMetadata.Query("bucketMinutes", "Bucket width in minutes", true))
                .WithResponse(200, OpenApiResponseMetadata.Json("Request summary", null)));

            server.Get("/v1.0/api/request-history/{id}", ReadAsync, openApi => openApi
                .WithTag("Request History").WithDescription("Read a single captured request, including headers and bodies.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Entry identifier"))
                .WithResponse(200, OpenApiResponseMetadata.Json("Request history entry", null))
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Delete("/v1.0/api/request-history/{id}", DeleteAsync, openApi => openApi
                .WithTag("Request History").WithDescription("Delete a single captured request.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Entry identifier"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound()));

            server.Delete("/v1.0/api/request-history", DeleteManyAsync, openApi => openApi
                .WithTag("Request History").WithDescription("Bulk-delete captured requests matching a filter. Returns the deleted count.")
                .WithResponse(200, OpenApiResponseMetadata.Create("Deleted count")));
        }

        #endregion

        #region Private-Methods

        private Task<object> ListAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                if (!_Settings.Enabled) return new RequestHistoryPage { PageNumber = 1, PageSize = 25 };
                RequestHistoryFilter filter = BuildFilter(request);
                return await _Db.RequestHistory.EnumerateAsync(filter, request.CancellationToken).ConfigureAwait(false);
            });
        }

        private Task<object> SummaryAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                if (!_Settings.Enabled) return new RequestHistorySummary();
                RequestHistoryFilter filter = BuildFilter(request);
                return await _Db.RequestHistory.SummarizeAsync(filter, request.CancellationToken).ConfigureAwait(false);
            });
        }

        private Task<object> ReadAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string id = request.Parameters["id"];
                PepperX.Core.Models.RequestHistoryEntry? entry = await _Db.RequestHistory.ReadAsync(id, request.CancellationToken).ConfigureAwait(false);
                if (entry == null)
                {
                    request.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse(ApiErrorEnum.NotFound, "Request history entry not found.", 404);
                }
                return entry;
            });
        }

        private Task<object> DeleteAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                string id = request.Parameters["id"];
                bool deleted = await _Db.RequestHistory.DeleteAsync(id, request.CancellationToken).ConfigureAwait(false);
                if (!deleted)
                {
                    request.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse(ApiErrorEnum.NotFound, "Request history entry not found.", 404);
                }
                request.Http.Response.StatusCode = 204;
                return null!;
            });
        }

        private Task<object> DeleteManyAsync(ApiRequest request)
        {
            return RouteHelpers.HandleAsync(request, async () =>
            {
                RequestHistoryFilter filter = BuildFilter(request);
                int deleted = await _Db.RequestHistory.DeleteManyAsync(filter, request.CancellationToken).ConfigureAwait(false);
                return new { DeletedCount = deleted };
            });
        }

        private static RequestHistoryFilter BuildFilter(ApiRequest request)
        {
            RequestHistoryFilter filter = new RequestHistoryFilter
            {
                Method = RouteHelpers.OptionalQuery(request, "method"),
                PathContains = RouteHelpers.OptionalQuery(request, "pathContains")
            };

            string? status = RouteHelpers.OptionalQuery(request, "statusCode");
            if (!String.IsNullOrEmpty(status) && Int32.TryParse(status, out int code)) filter.StatusCode = code;

            filter.FromUtc = ParseUtc(RouteHelpers.OptionalQuery(request, "fromUtc"));
            filter.ToUtc = ParseUtc(RouteHelpers.OptionalQuery(request, "toUtc"));

            string? page = RouteHelpers.OptionalQuery(request, "pageNumber");
            if (!String.IsNullOrEmpty(page) && Int32.TryParse(page, out int p)) filter.PageNumber = p;

            string? size = RouteHelpers.OptionalQuery(request, "pageSize");
            if (!String.IsNullOrEmpty(size) && Int32.TryParse(size, out int s)) filter.PageSize = s;

            string? bucket = RouteHelpers.OptionalQuery(request, "bucketMinutes");
            if (!String.IsNullOrEmpty(bucket) && Int32.TryParse(bucket, out int b)) filter.BucketMinutes = b;

            return filter;
        }

        private static DateTime? ParseUtc(string? value)
        {
            if (String.IsNullOrEmpty(value)) return null;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed)) return parsed;
            return null;
        }

        #endregion
    }
}
