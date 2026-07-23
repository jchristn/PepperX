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
    using PepperX.Core.Responses;

    /// <summary>
    /// PostgreSQL implementation of container data access.
    /// </summary>
    public sealed class PostgresqlContainerMethods : IContainerMethods
    {
        #region Private-Members

        private const string _Columns = "id, name, tags, object_count, total_bytes, created_utc, last_update_utc";
        private readonly NpgsqlDataSource _DataSource;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate container methods.
        /// </summary>
        /// <param name="dataSource">Data source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> is null.</exception>
        public PostgresqlContainerMethods(NpgsqlDataSource dataSource)
        {
            _DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Container> CreateAsync(Container container, CancellationToken token = default)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "INSERT INTO containers (id, name, tags, object_count, total_bytes, created_utc, last_update_utc) " +
                "VALUES (@id, @name, @tags, @oc, @tb, @cu, @lu);"))
            {
                cmd.Parameters.AddWithValue("id", container.Id);
                cmd.Parameters.AddWithValue("name", container.Name);
                cmd.Parameters.Add(Converters.JsonbParam("tags", container.Tags));
                cmd.Parameters.AddWithValue("oc", container.ObjectCount);
                cmd.Parameters.AddWithValue("tb", container.TotalBytes);
                cmd.Parameters.AddWithValue("cu", Converters.AsUtc(container.CreatedUtc));
                cmd.Parameters.AddWithValue("lu", Converters.AsUtc(container.LastUpdateUtc));

                try
                {
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                catch (PostgresException ex) when (ex.SqlState == "23505")
                {
                    throw new PepperXException(ApiErrorEnum.Conflict, 409, "Container '" + container.Name + "' already exists.", ex);
                }
            }

            return container;
        }

        /// <inheritdoc />
        public Task<Container?> ReadByIdAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return ReadOneAsync("SELECT " + _Columns + " FROM containers WHERE id = @v;", id, token);
        }

        /// <inheritdoc />
        public Task<Container?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            return ReadOneAsync("SELECT " + _Columns + " FROM containers WHERE name = @v;", name, token);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("SELECT 1 FROM containers WHERE name = @v LIMIT 1;"))
            {
                cmd.Parameters.AddWithValue("v", name);
                object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                return result != null && result != DBNull.Value;
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Container>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();
            StringBuilder where = new StringBuilder(" WHERE 1=1");

            if (!String.IsNullOrEmpty(query.Prefix))
            {
                where.Append(" AND name LIKE @prefix ESCAPE '\\'");
                parameters.Add(new NpgsqlParameter("prefix", Converters.EscapeLike(query.Prefix) + "%"));
            }
            if (!String.IsNullOrEmpty(query.Suffix))
            {
                where.Append(" AND name LIKE @suffix ESCAPE '\\'");
                parameters.Add(new NpgsqlParameter("suffix", "%" + Converters.EscapeLike(query.Suffix)));
            }
            if (query.CreatedAfterUtc.HasValue)
            {
                where.Append(" AND created_utc > @after");
                parameters.Add(new NpgsqlParameter("after", Converters.AsUtc(query.CreatedAfterUtc.Value)));
            }
            if (query.CreatedBeforeUtc.HasValue)
            {
                where.Append(" AND created_utc < @before");
                parameters.Add(new NpgsqlParameter("before", Converters.AsUtc(query.CreatedBeforeUtc.Value)));
            }

            long total = await CountAsync(where.ToString(), parameters, token).ConfigureAwait(false);

            StringBuilder sql = new StringBuilder("SELECT " + _Columns + " FROM containers");
            sql.Append(where);

            bool useKeyset = !String.IsNullOrEmpty(query.ContinuationToken)
                && (query.Ordering == EnumerationOrderEnum.CreatedAscending || query.Ordering == EnumerationOrderEnum.CreatedDescending);
            if (useKeyset)
            {
                sql.Append(query.Ordering == EnumerationOrderEnum.CreatedDescending ? " AND id < @token" : " AND id > @token");
                parameters.Add(new NpgsqlParameter("token", query.ContinuationToken));
            }

            sql.Append(OrderByClause(query.Ordering, "name"));
            sql.Append(" LIMIT @limit");
            parameters.Add(new NpgsqlParameter("limit", query.MaxResults));
            if (!useKeyset && query.Skip > 0)
            {
                sql.Append(" OFFSET @skip");
                parameters.Add(new NpgsqlParameter("skip", query.Skip));
            }

            List<Container> results = new List<Container>();
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(sql.ToString()))
            {
                foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(p);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(Converters.ReadContainer(reader));
                }
            }

            return BuildResult(query, total, results);
        }

        /// <inheritdoc />
        public async Task<Container?> UpdateTagsAsync(string id, Dictionary<string, string> tags, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "UPDATE containers SET tags = @tags, last_update_utc = now() WHERE id = @id;"))
            {
                cmd.Parameters.Add(Converters.JsonbParam("tags", tags ?? new Dictionary<string, string>()));
                cmd.Parameters.AddWithValue("id", id);
                int affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                if (affected == 0) return null;
            }

            return await ReadByIdAsync(id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("DELETE FROM containers WHERE id = @id;"))
            {
                cmd.Parameters.AddWithValue("id", id);
                int affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return affected > 0;
            }
        }

        /// <inheritdoc />
        public async Task AdjustCountersAsync(string id, long objectDelta, long byteDelta, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "UPDATE containers SET object_count = object_count + @od, total_bytes = total_bytes + @bd, last_update_utc = now() WHERE id = @id;"))
            {
                cmd.Parameters.AddWithValue("od", objectDelta);
                cmd.Parameters.AddWithValue("bd", byteDelta);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ContainerStatistics>> ReadAllStatisticsAsync(CancellationToken token = default)
        {
            List<ContainerStatistics> results = new List<ContainerStatistics>();

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "SELECT id, name, object_count, total_bytes FROM containers ORDER BY name ASC;"))
            await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    results.Add(new ContainerStatistics
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        ObjectCount = reader.GetInt64(2),
                        TotalBytes = reader.GetInt64(3)
                    });
                }
            }

            return results;
        }

        #endregion

        #region Private-Methods

        private async Task<Container?> ReadOneAsync(string sql, string value, CancellationToken token)
        {
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(sql))
            {
                cmd.Parameters.AddWithValue("v", value);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                    return Converters.ReadContainer(reader);
                }
            }
        }

        private async Task<long> CountAsync(string where, List<NpgsqlParameter> parameters, CancellationToken token)
        {
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("SELECT COUNT(*) FROM containers" + where + ";"))
            {
                foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(Clone(p));
                object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
            }
        }

        private static string OrderByClause(EnumerationOrderEnum ordering, string nameColumn)
        {
            switch (ordering)
            {
                case EnumerationOrderEnum.CreatedAscending:
                    return " ORDER BY id ASC";
                case EnumerationOrderEnum.KeyAscending:
                    return " ORDER BY " + nameColumn + " ASC, id ASC";
                case EnumerationOrderEnum.KeyDescending:
                    return " ORDER BY " + nameColumn + " DESC, id DESC";
                default:
                    return " ORDER BY id DESC";
            }
        }

        private static EnumerationResult<Container> BuildResult(EnumerationQuery query, long total, List<Container> results)
        {
            EnumerationResult<Container> result = new EnumerationResult<Container>
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

        private static NpgsqlParameter Clone(NpgsqlParameter source)
        {
            return new NpgsqlParameter(source.ParameterName, source.Value);
        }

        #endregion
    }
}
