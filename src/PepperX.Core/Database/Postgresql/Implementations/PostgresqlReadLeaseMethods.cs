namespace PepperX.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using PepperX.Core.Database;
    using PepperX.Core.Database.Interfaces;
    using PepperX.Core.Helpers;
    using PepperX.Core.Models;

    /// <summary>
    /// PostgreSQL implementation of read lease data access.
    /// </summary>
    public sealed class PostgresqlReadLeaseMethods : IReadLeaseMethods
    {
        #region Private-Members

        private const string _ExtentColumns = "e.id, e.container_id, e.object_key, e.state, e.size_bytes, e.sha256, e.content_type, e.storage_driver, e.storage_location, e.has_metadata_object, e.created_utc, e.last_update_utc";
        private readonly NpgsqlDataSource _DataSource;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate read lease methods.
        /// </summary>
        /// <param name="dataSource">Data source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> is null.</exception>
        public PostgresqlReadLeaseMethods(NpgsqlDataSource dataSource)
        {
            _DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<LeaseAcquisition?> AcquireForActiveExtentAsync(string containerId, string key, string nodeId, int ttlSeconds, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (String.IsNullOrEmpty(nodeId)) throw new ArgumentNullException(nameof(nodeId));
            if (ttlSeconds < 1) ttlSeconds = 1;

            string leaseId = IdGenerator.GenerateLeaseId();

            string sql =
                "WITH target AS (" +
                "  SELECT id FROM extents WHERE container_id = @cid AND object_key = @key AND state = 'Active' LIMIT 1" +
                "), ins AS (" +
                "  INSERT INTO extent_read_leases (id, extent_id, node_id, acquired_utc, expires_utc) " +
                "  SELECT @lid, id, @nid, now(), now() + make_interval(secs => @ttl) FROM target RETURNING extent_id" +
                ") SELECT " + _ExtentColumns + " FROM extents e JOIN ins ON e.id = ins.extent_id;";

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(sql))
            {
                cmd.Parameters.AddWithValue("cid", containerId);
                cmd.Parameters.AddWithValue("key", key);
                cmd.Parameters.AddWithValue("lid", leaseId);
                cmd.Parameters.AddWithValue("nid", nodeId);
                cmd.Parameters.AddWithValue("ttl", ttlSeconds);

                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                    Extent extent = Converters.ReadExtent(reader);
                    return new LeaseAcquisition(extent, leaseId);
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> RenewAsync(string leaseId, int ttlSeconds, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(leaseId)) throw new ArgumentNullException(nameof(leaseId));
            if (ttlSeconds < 1) ttlSeconds = 1;

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "UPDATE extent_read_leases SET expires_utc = now() + make_interval(secs => @ttl) WHERE id = @id;"))
            {
                cmd.Parameters.AddWithValue("ttl", ttlSeconds);
                cmd.Parameters.AddWithValue("id", leaseId);
                int affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return affected > 0;
            }
        }

        /// <inheritdoc />
        public async Task ReleaseAsync(string leaseId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(leaseId)) throw new ArgumentNullException(nameof(leaseId));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("DELETE FROM extent_read_leases WHERE id = @id;"))
            {
                cmd.Parameters.AddWithValue("id", leaseId);
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<long> CountActiveForExtentAsync(string extentId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(extentId)) throw new ArgumentNullException(nameof(extentId));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "SELECT COUNT(*) FROM extent_read_leases WHERE extent_id = @id AND expires_utc > now();"))
            {
                cmd.Parameters.AddWithValue("id", extentId);
                object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
            }
        }

        /// <inheritdoc />
        public async Task<int> PurgeExpiredAsync(CancellationToken token = default)
        {
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("DELETE FROM extent_read_leases WHERE expires_utc <= now();"))
            {
                return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<int> PurgeForNodeAsync(string nodeId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(nodeId)) throw new ArgumentNullException(nameof(nodeId));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("DELETE FROM extent_read_leases WHERE node_id = @nid;"))
            {
                cmd.Parameters.AddWithValue("nid", nodeId);
                return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
