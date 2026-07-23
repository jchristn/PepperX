namespace PepperX.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using PepperX.Core.Database.Interfaces;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Enums;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;

    /// <summary>
    /// PostgreSQL implementation of extent data access. Container counters are maintained transactionally:
    /// create increments, tombstone decrements, replace applies the byte delta, and purge does not change
    /// them.
    /// </summary>
    public sealed class PostgresqlExtentMethods : IExtentMethods
    {
        #region Private-Members

        private const string _Columns = "id, container_id, object_key, state, size_bytes, sha256, content_type, storage_driver, storage_location, has_metadata_object, created_utc, last_update_utc";
        private const string _UniqueViolation = "23505";
        private readonly NpgsqlDataSource _DataSource;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate extent methods.
        /// </summary>
        /// <param name="dataSource">Data source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> is null.</exception>
        public PostgresqlExtentMethods(NpgsqlDataSource dataSource)
        {
            _DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Extent> CreateAsync(Extent extent, CancellationToken token = default)
        {
            if (extent == null) throw new ArgumentNullException(nameof(extent));

            await using (NpgsqlConnection connection = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            await using (NpgsqlTransaction tx = await connection.BeginTransactionAsync(token).ConfigureAwait(false))
            {
                try
                {
                    await InsertExtentAsync(connection, tx, extent, token).ConfigureAwait(false);
                    await AdjustCountersAsync(connection, tx, extent.ContainerId, 1, extent.SizeBytes, token).ConfigureAwait(false);
                    await tx.CommitAsync(token).ConfigureAwait(false);
                }
                catch (PostgresException ex) when (ex.SqlState == _UniqueViolation)
                {
                    await tx.RollbackAsync(token).ConfigureAwait(false);
                    throw new ObjectAlreadyExistsException(extent.ContainerId, extent.Key);
                }
            }

            return extent;
        }

        /// <inheritdoc />
        public async Task<Extent?> ReadActiveAsync(string containerId, string key, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            Extent? extent = null;
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "SELECT " + _Columns + " FROM extents WHERE container_id = @cid AND object_key = @key AND state = 'Active' LIMIT 1;"))
            {
                cmd.Parameters.AddWithValue("cid", containerId);
                cmd.Parameters.AddWithValue("key", key);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(token).ConfigureAwait(false)) extent = Converters.ReadExtent(reader);
                }
            }

            if (extent != null) await HydrateAsync(new List<Extent> { extent }, token).ConfigureAwait(false);
            return extent;
        }

        /// <inheritdoc />
        public async Task<Extent?> ReadByIdAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Extent? extent = null;
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("SELECT " + _Columns + " FROM extents WHERE id = @id LIMIT 1;"))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(token).ConfigureAwait(false)) extent = Converters.ReadExtent(reader);
                }
            }

            if (extent != null) await HydrateAsync(new List<Extent> { extent }, token).ConfigureAwait(false);
            return extent;
        }

        /// <inheritdoc />
        public async Task<bool> ExistsActiveAsync(string containerId, string key, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "SELECT 1 FROM extents WHERE container_id = @cid AND object_key = @key AND state = 'Active' LIMIT 1;"))
            {
                cmd.Parameters.AddWithValue("cid", containerId);
                cmd.Parameters.AddWithValue("key", key);
                object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                return result != null && result != DBNull.Value;
            }
        }

        /// <inheritdoc />
        public async Task<Extent?> MarkDeletingAsync(string containerId, string key, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            await using (NpgsqlConnection connection = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            await using (NpgsqlTransaction tx = await connection.BeginTransactionAsync(token).ConfigureAwait(false))
            {
                Extent? extent = null;
                await using (NpgsqlCommand cmd = new NpgsqlCommand(
                    "UPDATE extents SET state = 'Deleting', last_update_utc = now() WHERE container_id = @cid AND object_key = @key AND state = 'Active' RETURNING " + _Columns + ";",
                    connection, tx))
                {
                    cmd.Parameters.AddWithValue("cid", containerId);
                    cmd.Parameters.AddWithValue("key", key);
                    await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false)) extent = Converters.ReadExtent(reader);
                    }
                }

                if (extent == null)
                {
                    await tx.RollbackAsync(token).ConfigureAwait(false);
                    return null;
                }

                await AdjustCountersAsync(connection, tx, containerId, -1, -extent.SizeBytes, token).ConfigureAwait(false);
                await tx.CommitAsync(token).ConfigureAwait(false);
                return extent;
            }
        }

        /// <inheritdoc />
        public async Task<string?> ReplaceAsync(Extent newExtent, CancellationToken token = default)
        {
            if (newExtent == null) throw new ArgumentNullException(nameof(newExtent));

            await using (NpgsqlConnection connection = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            await using (NpgsqlTransaction tx = await connection.BeginTransactionAsync(token).ConfigureAwait(false))
            {
                try
                {
                    string? oldId = null;
                    long oldSize = 0;

                    await using (NpgsqlCommand tombstone = new NpgsqlCommand(
                        "UPDATE extents SET state = 'Deleting', last_update_utc = now() WHERE container_id = @cid AND object_key = @key AND state = 'Active' RETURNING id, size_bytes;",
                        connection, tx))
                    {
                        tombstone.Parameters.AddWithValue("cid", newExtent.ContainerId);
                        tombstone.Parameters.AddWithValue("key", newExtent.Key);
                        await using (NpgsqlDataReader reader = await tombstone.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(token).ConfigureAwait(false))
                            {
                                oldId = reader.GetString(0);
                                oldSize = reader.GetInt64(1);
                            }
                        }
                    }

                    await InsertExtentAsync(connection, tx, newExtent, token).ConfigureAwait(false);

                    long countDelta = oldId == null ? 1 : 0;
                    long byteDelta = newExtent.SizeBytes - (oldId == null ? 0 : oldSize);
                    await AdjustCountersAsync(connection, tx, newExtent.ContainerId, countDelta, byteDelta, token).ConfigureAwait(false);

                    await tx.CommitAsync(token).ConfigureAwait(false);
                    return oldId;
                }
                catch (PostgresException ex) when (ex.SqlState == _UniqueViolation)
                {
                    await tx.RollbackAsync(token).ConfigureAwait(false);
                    throw new ConcurrentModificationException("A concurrent write to key '" + newExtent.Key + "' occurred; retry the operation.");
                }
            }
        }

        /// <inheritdoc />
        public async Task PurgeAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            await using (NpgsqlConnection connection = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            await using (NpgsqlTransaction tx = await connection.BeginTransactionAsync(token).ConfigureAwait(false))
            {
                await using (NpgsqlCommand leases = new NpgsqlCommand("DELETE FROM extent_read_leases WHERE extent_id = @id;", connection, tx))
                {
                    leases.Parameters.AddWithValue("id", id);
                    await leases.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await using (NpgsqlCommand extent = new NpgsqlCommand("DELETE FROM extents WHERE id = @id;", connection, tx))
                {
                    extent.Parameters.AddWithValue("id", id);
                    await extent.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await tx.CommitAsync(token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Extent>> EnumerateAsync(string? containerId, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();
            StringBuilder where = new StringBuilder(" WHERE e.state = 'Active'");

            if (!String.IsNullOrEmpty(containerId))
            {
                where.Append(" AND e.container_id = @cid");
                parameters.Add(new NpgsqlParameter("cid", containerId));
            }
            else if (query.Containers != null && query.Containers.Count > 0)
            {
                where.Append(" AND e.container_id IN (SELECT id FROM containers WHERE name = ANY(@cnames))");
                parameters.Add(new NpgsqlParameter("cnames", query.Containers.ToArray()));
            }

            if (!String.IsNullOrEmpty(query.Prefix))
            {
                where.Append(" AND e.object_key LIKE @prefix ESCAPE '\\'");
                parameters.Add(new NpgsqlParameter("prefix", Converters.EscapeLike(query.Prefix) + "%"));
            }
            if (!String.IsNullOrEmpty(query.Suffix))
            {
                where.Append(" AND e.object_key LIKE @suffix ESCAPE '\\'");
                parameters.Add(new NpgsqlParameter("suffix", "%" + Converters.EscapeLike(query.Suffix)));
            }
            if (query.CreatedAfterUtc.HasValue)
            {
                where.Append(" AND e.created_utc > @after");
                parameters.Add(new NpgsqlParameter("after", Converters.AsUtc(query.CreatedAfterUtc.Value)));
            }
            if (query.CreatedBeforeUtc.HasValue)
            {
                where.Append(" AND e.created_utc < @before");
                parameters.Add(new NpgsqlParameter("before", Converters.AsUtc(query.CreatedBeforeUtc.Value)));
            }

            AppendLabelFilter(where, parameters, query);
            AppendTagFilter(where, parameters, query);

            long total = await CountAsync(where.ToString(), parameters, token).ConfigureAwait(false);

            StringBuilder sql = new StringBuilder("SELECT " + PrefixColumns() + " FROM extents e");
            sql.Append(where);

            bool useKeyset = !String.IsNullOrEmpty(query.ContinuationToken)
                && (query.Ordering == EnumerationOrderEnum.CreatedAscending || query.Ordering == EnumerationOrderEnum.CreatedDescending);
            if (useKeyset)
            {
                sql.Append(query.Ordering == EnumerationOrderEnum.CreatedDescending ? " AND e.id < @token" : " AND e.id > @token");
                parameters.Add(new NpgsqlParameter("token", query.ContinuationToken));
            }

            sql.Append(OrderByClause(query.Ordering));
            sql.Append(" LIMIT @limit");
            parameters.Add(new NpgsqlParameter("limit", query.MaxResults));
            if (!useKeyset && query.Skip > 0)
            {
                sql.Append(" OFFSET @skip");
                parameters.Add(new NpgsqlParameter("skip", query.Skip));
            }

            List<Extent> results = new List<Extent>();
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(sql.ToString()))
            {
                foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(p);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(Converters.ReadExtent(reader));
                }
            }

            await HydrateAsync(results, token).ConfigureAwait(false);
            return BuildResult(query, total, results);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Extent>> ListDeletingAsync(int limit, CancellationToken token = default)
        {
            if (limit < 1) limit = 1;

            List<Extent> results = new List<Extent>();
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "SELECT " + _Columns + " FROM extents WHERE state = 'Deleting' ORDER BY id ASC LIMIT @limit;"))
            {
                cmd.Parameters.AddWithValue("limit", limit);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(Converters.ReadExtent(reader));
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<long> CountActiveAsync(string? containerId, CancellationToken token = default)
        {
            string sql = String.IsNullOrEmpty(containerId)
                ? "SELECT COUNT(*) FROM extents WHERE state = 'Active';"
                : "SELECT COUNT(*) FROM extents WHERE state = 'Active' AND container_id = @cid;";

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(sql))
            {
                if (!String.IsNullOrEmpty(containerId)) cmd.Parameters.AddWithValue("cid", containerId);
                object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
            }
        }

        #endregion

        #region Private-Methods

        private static string PrefixColumns()
        {
            return "e.id, e.container_id, e.object_key, e.state, e.size_bytes, e.sha256, e.content_type, e.storage_driver, e.storage_location, e.has_metadata_object, e.created_utc, e.last_update_utc";
        }

        private static async Task InsertExtentAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Extent extent, CancellationToken token)
        {
            await using (NpgsqlCommand cmd = new NpgsqlCommand(
                "INSERT INTO extents (id, container_id, object_key, state, size_bytes, sha256, content_type, storage_driver, storage_location, has_metadata_object, created_utc, last_update_utc) " +
                "VALUES (@id, @cid, @key, @state, @size, @sha, @ct, @driver, @loc, @hasobj, @cu, @lu);", connection, tx))
            {
                cmd.Parameters.AddWithValue("id", extent.Id);
                cmd.Parameters.AddWithValue("cid", extent.ContainerId);
                cmd.Parameters.AddWithValue("key", extent.Key);
                cmd.Parameters.AddWithValue("state", extent.State.ToString());
                cmd.Parameters.AddWithValue("size", extent.SizeBytes);
                cmd.Parameters.AddWithValue("sha", extent.Sha256);
                cmd.Parameters.AddWithValue("ct", (object?)extent.ContentType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("driver", extent.StorageDriver.ToString());
                cmd.Parameters.AddWithValue("loc", extent.StorageLocation);
                cmd.Parameters.AddWithValue("hasobj", extent.HasMetadataObject);
                cmd.Parameters.AddWithValue("cu", Converters.AsUtc(extent.CreatedUtc));
                cmd.Parameters.AddWithValue("lu", Converters.AsUtc(extent.LastUpdateUtc));
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            foreach (string label in extent.Labels)
            {
                await using (NpgsqlCommand cmd = new NpgsqlCommand(
                    "INSERT INTO extent_labels (extent_id, container_id, label) VALUES (@eid, @cid, @label);", connection, tx))
                {
                    cmd.Parameters.AddWithValue("eid", extent.Id);
                    cmd.Parameters.AddWithValue("cid", extent.ContainerId);
                    cmd.Parameters.AddWithValue("label", label);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            foreach (KeyValuePair<string, string> tag in extent.Tags)
            {
                await using (NpgsqlCommand cmd = new NpgsqlCommand(
                    "INSERT INTO extent_tags (extent_id, container_id, tag_key, tag_value) VALUES (@eid, @cid, @k, @v);", connection, tx))
                {
                    cmd.Parameters.AddWithValue("eid", extent.Id);
                    cmd.Parameters.AddWithValue("cid", extent.ContainerId);
                    cmd.Parameters.AddWithValue("k", tag.Key);
                    cmd.Parameters.AddWithValue("v", tag.Value);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static async Task AdjustCountersAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string containerId, long objectDelta, long byteDelta, CancellationToken token)
        {
            await using (NpgsqlCommand cmd = new NpgsqlCommand(
                "UPDATE containers SET object_count = object_count + @od, total_bytes = total_bytes + @bd, last_update_utc = now() WHERE id = @cid;", connection, tx))
            {
                cmd.Parameters.AddWithValue("od", objectDelta);
                cmd.Parameters.AddWithValue("bd", byteDelta);
                cmd.Parameters.AddWithValue("cid", containerId);
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        private static void AppendLabelFilter(StringBuilder where, List<NpgsqlParameter> parameters, EnumerationQuery query)
        {
            if (query.Labels == null || query.Labels.Count == 0) return;

            string expr = query.CaseInsensitive ? "lower(label)" : "label";
            string match = query.CaseInsensitive ? "lower(label) = ANY(@labels)" : "label = ANY(@labels)";
            string[] values = query.CaseInsensitive ? LowerAll(query.Labels) : query.Labels.ToArray();

            where.Append(" AND e.id IN (SELECT extent_id FROM extent_labels WHERE " + match + " GROUP BY extent_id HAVING COUNT(DISTINCT " + expr + ") = @labelcount)");
            parameters.Add(new NpgsqlParameter("labels", values));
            parameters.Add(new NpgsqlParameter("labelcount", (long)query.Labels.Count));
        }

        private static void AppendTagFilter(StringBuilder where, List<NpgsqlParameter> parameters, EnumerationQuery query)
        {
            if (query.Tags == null || query.Tags.Count == 0) return;

            StringBuilder ors = new StringBuilder();
            int index = 0;
            foreach (KeyValuePair<string, string> tag in query.Tags)
            {
                if (index > 0) ors.Append(" OR ");
                string keyParam = "tk" + index;
                string valueParam = "tv" + index;
                if (query.CaseInsensitive)
                {
                    ors.Append("(lower(tag_key) = @" + keyParam + " AND lower(tag_value) = @" + valueParam + ")");
                    parameters.Add(new NpgsqlParameter(keyParam, tag.Key.ToLowerInvariant()));
                    parameters.Add(new NpgsqlParameter(valueParam, tag.Value.ToLowerInvariant()));
                }
                else
                {
                    ors.Append("(tag_key = @" + keyParam + " AND tag_value = @" + valueParam + ")");
                    parameters.Add(new NpgsqlParameter(keyParam, tag.Key));
                    parameters.Add(new NpgsqlParameter(valueParam, tag.Value));
                }
                index++;
            }

            string keyExpr = query.CaseInsensitive ? "lower(tag_key)" : "tag_key";
            where.Append(" AND e.id IN (SELECT extent_id FROM extent_tags WHERE (" + ors + ") GROUP BY extent_id HAVING COUNT(DISTINCT " + keyExpr + ") = @tagcount)");
            parameters.Add(new NpgsqlParameter("tagcount", (long)query.Tags.Count));
        }

        private async Task<long> CountAsync(string where, List<NpgsqlParameter> parameters, CancellationToken token)
        {
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("SELECT COUNT(*) FROM extents e" + where + ";"))
            {
                foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.Value));
                object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
            }
        }

        private async Task HydrateAsync(List<Extent> extents, CancellationToken token)
        {
            if (extents.Count == 0) return;

            string[] ids = new string[extents.Count];
            Dictionary<string, Extent> byId = new Dictionary<string, Extent>();
            for (int i = 0; i < extents.Count; i++)
            {
                ids[i] = extents[i].Id;
                extents[i].Labels = new List<string>();
                extents[i].Tags = new Dictionary<string, string>();
                byId[extents[i].Id] = extents[i];
            }

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("SELECT extent_id, label FROM extent_labels WHERE extent_id = ANY(@ids);"))
            {
                cmd.Parameters.AddWithValue("ids", ids);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (byId.TryGetValue(reader.GetString(0), out Extent? extent)) extent.Labels.Add(reader.GetString(1));
                    }
                }
            }

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("SELECT extent_id, tag_key, tag_value FROM extent_tags WHERE extent_id = ANY(@ids);"))
            {
                cmd.Parameters.AddWithValue("ids", ids);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (byId.TryGetValue(reader.GetString(0), out Extent? extent)) extent.Tags[reader.GetString(1)] = reader.GetString(2);
                    }
                }
            }
        }

        private static string OrderByClause(EnumerationOrderEnum ordering)
        {
            switch (ordering)
            {
                case EnumerationOrderEnum.CreatedAscending:
                    return " ORDER BY e.id ASC";
                case EnumerationOrderEnum.KeyAscending:
                    return " ORDER BY e.object_key ASC, e.id ASC";
                case EnumerationOrderEnum.KeyDescending:
                    return " ORDER BY e.object_key DESC, e.id DESC";
                default:
                    return " ORDER BY e.id DESC";
            }
        }

        private static EnumerationResult<Extent> BuildResult(EnumerationQuery query, long total, List<Extent> results)
        {
            EnumerationResult<Extent> result = new EnumerationResult<Extent>
            {
                MaxResults = query.MaxResults,
                TotalRecords = total,
                Objects = results
            };

            long consumed = query.Skip + results.Count;
            long remaining = total - consumed;
            result.RecordsRemaining = remaining > 0 ? remaining : 0;
            result.EndOfResults = results.Count < query.MaxResults || remaining <= 0;

            bool createdOrdered = query.Ordering == EnumerationOrderEnum.CreatedAscending || query.Ordering == EnumerationOrderEnum.CreatedDescending;
            if (!result.EndOfResults && createdOrdered && results.Count > 0)
                result.ContinuationToken = results[results.Count - 1].Id;

            result.EndUtc = DateTime.UtcNow;
            return result;
        }

        private static string[] LowerAll(List<string> values)
        {
            string[] result = new string[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = values[i].ToLowerInvariant();
            return result;
        }

        #endregion
    }
}
