namespace PepperX.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using PepperX.Core.Database.Interfaces;
    using PepperX.Core.Models;

    /// <summary>
    /// PostgreSQL implementation of node data access.
    /// </summary>
    public sealed class PostgresqlNodeMethods : INodeMethods
    {
        #region Private-Members

        private const string _Columns = "id, hostname, started_utc, last_heartbeat_utc, created_utc";
        private readonly NpgsqlDataSource _DataSource;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate node methods.
        /// </summary>
        /// <param name="dataSource">Data source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> is null.</exception>
        public PostgresqlNodeMethods(NpgsqlDataSource dataSource)
        {
            _DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task UpsertHeartbeatAsync(NodeRecord node, CancellationToken token = default)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "INSERT INTO nodes (id, hostname, started_utc, last_heartbeat_utc, created_utc) VALUES (@id, @host, @start, @hb, @cu) " +
                "ON CONFLICT (id) DO UPDATE SET hostname = EXCLUDED.hostname, last_heartbeat_utc = EXCLUDED.last_heartbeat_utc;"))
            {
                cmd.Parameters.AddWithValue("id", node.Id);
                cmd.Parameters.AddWithValue("host", (object?)node.Hostname ?? DBNull.Value);
                cmd.Parameters.AddWithValue("start", Converters.AsUtc(node.StartedUtc));
                cmd.Parameters.AddWithValue("hb", Converters.AsUtc(node.LastHeartbeatUtc));
                cmd.Parameters.AddWithValue("cu", Converters.AsUtc(node.CreatedUtc));
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<NodeRecord>> ListAsync(CancellationToken token = default)
        {
            List<NodeRecord> results = new List<NodeRecord>();
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("SELECT " + _Columns + " FROM nodes ORDER BY id ASC;"))
            await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(Converters.ReadNode(reader));
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<NodeRecord>> ListDeadAsync(int deadAfterSeconds, CancellationToken token = default)
        {
            if (deadAfterSeconds < 1) deadAfterSeconds = 1;

            List<NodeRecord> results = new List<NodeRecord>();
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "SELECT " + _Columns + " FROM nodes WHERE last_heartbeat_utc < now() - make_interval(secs => @s) ORDER BY id ASC;"))
            {
                cmd.Parameters.AddWithValue("s", deadAfterSeconds);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(Converters.ReadNode(reader));
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string nodeId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(nodeId)) throw new ArgumentNullException(nameof(nodeId));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("DELETE FROM nodes WHERE id = @id;"))
            {
                cmd.Parameters.AddWithValue("id", nodeId);
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
