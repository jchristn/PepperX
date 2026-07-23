namespace PepperX.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using PepperX.Core.Database.Interfaces;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;

    /// <summary>
    /// PostgreSQL implementation of request history data access.
    /// </summary>
    public sealed class PostgresqlRequestHistoryMethods : IRequestHistoryMethods
    {
        #region Private-Members

        private const string _ListColumns = "id, method, path, url, status_code, duration_ms, source_ip, request_body_bytes, request_body_truncated, response_body_bytes, response_body_truncated, created_utc, completed_utc";
        private const string _FullColumns = "id, method, path, url, status_code, duration_ms, source_ip, request_headers, request_body, request_body_bytes, request_body_truncated, response_headers, response_body, response_body_bytes, response_body_truncated, created_utc, completed_utc";
        private readonly NpgsqlDataSource _DataSource;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate request history methods.
        /// </summary>
        /// <param name="dataSource">Data source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> is null.</exception>
        public PostgresqlRequestHistoryMethods(NpgsqlDataSource dataSource)
        {
            _DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task CreateAsync(RequestHistoryEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "INSERT INTO request_history (id, method, path, url, status_code, duration_ms, source_ip, request_headers, request_body, request_body_bytes, request_body_truncated, response_headers, response_body, response_body_bytes, response_body_truncated, created_utc, completed_utc) " +
                "VALUES (@id, @method, @path, @url, @status, @dur, @ip, @reqh, @reqb, @reqbytes, @reqtrunc, @resph, @respb, @respbytes, @resptrunc, @cu, @completed);"))
            {
                cmd.Parameters.AddWithValue("id", entry.Id);
                cmd.Parameters.AddWithValue("method", entry.Method);
                cmd.Parameters.AddWithValue("path", entry.Path);
                cmd.Parameters.AddWithValue("url", entry.Url);
                cmd.Parameters.AddWithValue("status", entry.StatusCode);
                cmd.Parameters.AddWithValue("dur", entry.DurationMs);
                cmd.Parameters.AddWithValue("ip", (object?)entry.SourceIp ?? DBNull.Value);
                cmd.Parameters.Add(Converters.JsonbParam("reqh", entry.RequestHeaders));
                cmd.Parameters.AddWithValue("reqb", (object?)entry.RequestBody ?? DBNull.Value);
                cmd.Parameters.AddWithValue("reqbytes", entry.RequestBodyBytes);
                cmd.Parameters.AddWithValue("reqtrunc", entry.RequestBodyTruncated);
                cmd.Parameters.Add(Converters.JsonbParam("resph", entry.ResponseHeaders));
                cmd.Parameters.AddWithValue("respb", (object?)entry.ResponseBody ?? DBNull.Value);
                cmd.Parameters.AddWithValue("respbytes", entry.ResponseBodyBytes);
                cmd.Parameters.AddWithValue("resptrunc", entry.ResponseBodyTruncated);
                cmd.Parameters.AddWithValue("cu", Converters.AsUtc(entry.CreatedUtc));
                cmd.Parameters.AddWithValue("completed", (object?)(entry.CompletedUtc.HasValue ? Converters.AsUtc(entry.CompletedUtc.Value) : null) ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<RequestHistoryEntry?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("SELECT " + _FullColumns + " FROM request_history WHERE id = @id LIMIT 1;"))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                    return Converters.ReadRequestHistory(reader, true);
                }
            }
        }

        /// <inheritdoc />
        public async Task<RequestHistoryPage> EnumerateAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            long total;
            await using (NpgsqlCommand count = _DataSource.CreateCommand(BuildCountSql(filter)))
            {
                BindFilters(count, filter, true);
                object? result = await count.ExecuteScalarAsync(token).ConfigureAwait(false);
                total = result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
            }

            StringBuilder sql = new StringBuilder("SELECT " + _ListColumns + " FROM request_history r WHERE 1=1");
            StringBuilder filterSql = new StringBuilder();
            AppendFilterSql(filterSql, filter, true);
            sql.Append(filterSql);
            sql.Append(" ORDER BY created_utc DESC LIMIT @limit OFFSET @offset;");

            List<RequestHistoryEntry> entries = new List<RequestHistoryEntry>();
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(sql.ToString()))
            {
                BindFilters(cmd, filter, true);
                cmd.Parameters.AddWithValue("limit", filter.PageSize);
                cmd.Parameters.AddWithValue("offset", (filter.PageNumber - 1) * filter.PageSize);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) entries.Add(Converters.ReadRequestHistory(reader, false));
                }
            }

            return new RequestHistoryPage
            {
                Entries = entries,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
                TotalCount = total
            };
        }

        /// <inheritdoc />
        public async Task<RequestHistorySummary> SummarizeAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            DateTime from = Converters.AsUtc(filter.FromUtc ?? DateTime.UtcNow.AddDays(-1));
            DateTime to = Converters.AsUtc(filter.ToUtc ?? DateTime.UtcNow);

            RequestHistorySummary summary = new RequestHistorySummary();

            StringBuilder totalsFilter = new StringBuilder();
            AppendFilterSql(totalsFilter, filter, false);
            string totalsSql =
                "SELECT COUNT(*), " +
                "COALESCE(SUM(CASE WHEN status_code < 400 THEN 1 ELSE 0 END),0), " +
                "COALESCE(SUM(CASE WHEN status_code >= 400 THEN 1 ELSE 0 END),0), " +
                "COALESCE(AVG(duration_ms),0) " +
                "FROM request_history r WHERE created_utc >= @from AND created_utc < @to" + totalsFilter + ";";

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(totalsSql))
            {
                cmd.Parameters.AddWithValue("from", from);
                cmd.Parameters.AddWithValue("to", to);
                BindFilters(cmd, filter, false);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        summary.TotalCount = reader.GetInt64(0);
                        summary.TotalSuccess = reader.GetInt64(1);
                        summary.TotalFailure = reader.GetInt64(2);
                        summary.AverageDurationMs = reader.GetDouble(3);
                    }
                }
            }

            StringBuilder onFilter = new StringBuilder();
            AppendFilterSql(onFilter, filter, false);
            string bucketsSql =
                "SELECT g.bucket_start, " +
                "g.bucket_start + make_interval(mins => @bucket) AS bucket_end, " +
                "COALESCE(SUM(CASE WHEN r.status_code < 400 THEN 1 ELSE 0 END),0) AS success, " +
                "COALESCE(SUM(CASE WHEN r.status_code >= 400 THEN 1 ELSE 0 END),0) AS failure, " +
                "COALESCE(AVG(r.duration_ms),0) AS avgms " +
                "FROM generate_series(@from, @to - make_interval(mins => @bucket), make_interval(mins => @bucket)) AS g(bucket_start) " +
                "LEFT JOIN request_history r ON r.created_utc >= g.bucket_start AND r.created_utc < g.bucket_start + make_interval(mins => @bucket)" + onFilter + " " +
                "GROUP BY g.bucket_start ORDER BY g.bucket_start ASC;";

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(bucketsSql))
            {
                cmd.Parameters.AddWithValue("from", from);
                cmd.Parameters.AddWithValue("to", to);
                cmd.Parameters.AddWithValue("bucket", filter.BucketMinutes);
                BindFilters(cmd, filter, false);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        summary.Buckets.Add(new RequestHistoryBucket
                        {
                            BucketStartUtc = reader.GetDateTime(0),
                            BucketEndUtc = reader.GetDateTime(1),
                            SuccessCount = reader.GetInt64(2),
                            FailureCount = reader.GetInt64(3),
                            AverageDurationMs = reader.GetDouble(4)
                        });
                    }
                }
            }

            return summary;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("DELETE FROM request_history WHERE id = @id;"))
            {
                cmd.Parameters.AddWithValue("id", id);
                int affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return affected > 0;
            }
        }

        /// <inheritdoc />
        public async Task<int> DeleteManyAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            StringBuilder sql = new StringBuilder("DELETE FROM request_history r WHERE 1=1");
            StringBuilder filterSql = new StringBuilder();
            AppendFilterSql(filterSql, filter, true);
            sql.Append(filterSql);
            sql.Append(';');

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(sql.ToString()))
            {
                BindFilters(cmd, filter, true);
                return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("DELETE FROM request_history WHERE created_utc < @cutoff;"))
            {
                cmd.Parameters.AddWithValue("cutoff", Converters.AsUtc(olderThanUtc));
                return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static string BuildCountSql(RequestHistoryFilter filter)
        {
            StringBuilder sql = new StringBuilder("SELECT COUNT(*) FROM request_history r WHERE 1=1");
            AppendFilterSql(sql, filter, true);
            sql.Append(';');
            return sql.ToString();
        }

        private static void AppendFilterSql(StringBuilder sql, RequestHistoryFilter filter, bool includeTimeRange)
        {
            if (!String.IsNullOrEmpty(filter.Method)) sql.Append(" AND lower(r.method) = @method");
            if (filter.StatusCode.HasValue) sql.Append(" AND r.status_code = @status");
            if (!String.IsNullOrEmpty(filter.PathContains)) sql.Append(" AND r.path LIKE @pathcontains ESCAPE '\\'");
            if (includeTimeRange && filter.FromUtc.HasValue) sql.Append(" AND r.created_utc >= @rangefrom");
            if (includeTimeRange && filter.ToUtc.HasValue) sql.Append(" AND r.created_utc <= @rangeto");
        }

        private static void BindFilters(NpgsqlCommand cmd, RequestHistoryFilter filter, bool includeTimeRange)
        {
            if (!String.IsNullOrEmpty(filter.Method)) cmd.Parameters.AddWithValue("method", filter.Method.ToLowerInvariant());
            if (filter.StatusCode.HasValue) cmd.Parameters.AddWithValue("status", filter.StatusCode.Value);
            if (!String.IsNullOrEmpty(filter.PathContains)) cmd.Parameters.AddWithValue("pathcontains", "%" + Converters.EscapeLike(filter.PathContains) + "%");
            if (includeTimeRange && filter.FromUtc.HasValue) cmd.Parameters.AddWithValue("rangefrom", Converters.AsUtc(filter.FromUtc.Value));
            if (includeTimeRange && filter.ToUtc.HasValue) cmd.Parameters.AddWithValue("rangeto", Converters.AsUtc(filter.ToUtc.Value));
        }

        #endregion
    }
}
