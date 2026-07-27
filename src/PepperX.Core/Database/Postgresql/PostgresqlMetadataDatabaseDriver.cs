namespace PepperX.Core.Database.Postgresql
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using PepperX.Core.Database.Interfaces;
    using PepperX.Core.Database.Postgresql.Implementations;
    using PepperX.Core.Enums;
    using PepperX.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of the metadata database driver. Owns a pooled data source and applies
    /// tracked, idempotent migrations on initialization.
    /// </summary>
    public sealed class PostgresqlMetadataDatabaseDriver : IMetadataDatabaseDriver
    {
        #region Public-Members

        /// <summary>
        /// The provider type.
        /// </summary>
        public DatabaseTypeEnum DatabaseType => DatabaseTypeEnum.Postgresql;

        /// <summary>
        /// Container data access.
        /// </summary>
        public IContainerMethods Containers => _Containers ?? throw new InvalidOperationException("Driver is not initialized.");

        /// <summary>
        /// Extent data access.
        /// </summary>
        public IExtentMethods Extents => _Extents ?? throw new InvalidOperationException("Driver is not initialized.");

        /// <summary>
        /// Read lease data access.
        /// </summary>
        public IReadLeaseMethods ReadLeases => _ReadLeases ?? throw new InvalidOperationException("Driver is not initialized.");

        /// <summary>
        /// Node data access.
        /// </summary>
        public INodeMethods Nodes => _Nodes ?? throw new InvalidOperationException("Driver is not initialized.");

        /// <summary>
        /// Request history data access.
        /// </summary>
        public IRequestHistoryMethods RequestHistory => _RequestHistory ?? throw new InvalidOperationException("Driver is not initialized.");

        /// <summary>
        /// S3 multipart upload data access.
        /// </summary>
        public IMultipartMethods MultipartUploads => _MultipartUploads ?? throw new InvalidOperationException("Driver is not initialized.");

        #endregion

        #region Private-Members

        private readonly string _Header = "[PostgresqlMetadataDatabaseDriver] ";
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule? _Logging;

        private NpgsqlDataSource? _DataSource;
        private IContainerMethods? _Containers;
        private IExtentMethods? _Extents;
        private IReadLeaseMethods? _ReadLeases;
        private INodeMethods? _Nodes;
        private IRequestHistoryMethods? _RequestHistory;
        private IMultipartMethods? _MultipartUploads;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the driver.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        public PostgresqlMetadataDatabaseDriver(DatabaseSettings settings, LoggingModule? logging = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open the connection pool and apply pending migrations.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task InitializeAsync(CancellationToken token = default)
        {
            _DataSource = NpgsqlDataSource.Create(_Settings.BuildConnectionString());

            await RunMigrationsAsync(_DataSource, token).ConfigureAwait(false);

            _Containers = new PostgresqlContainerMethods(_DataSource);
            _Extents = new PostgresqlExtentMethods(_DataSource);
            _ReadLeases = new PostgresqlReadLeaseMethods(_DataSource);
            _Nodes = new PostgresqlNodeMethods(_DataSource);
            _RequestHistory = new PostgresqlRequestHistoryMethods(_DataSource);
            _MultipartUploads = new PostgresqlMultipartMethods(_DataSource);

            _Logging?.Info(_Header + "initialized");
        }

        /// <summary>
        /// Report the approximate database size in bytes.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Database size in bytes.</returns>
        public async Task<long> GetDatabaseSizeBytesAsync(CancellationToken token = default)
        {
            NpgsqlDataSource source = RequireSource();
            await using (NpgsqlCommand cmd = source.CreateCommand("SELECT pg_database_size(current_database());"))
            {
                object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
            }
        }

        /// <summary>
        /// Close the driver and dispose the connection pool.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task CloseAsync(CancellationToken token = default)
        {
            if (_DataSource != null)
            {
                await _DataSource.DisposeAsync().ConfigureAwait(false);
                _DataSource = null;
            }
        }

        /// <summary>
        /// Dispose the driver.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _DataSource?.Dispose();
            _DataSource = null;
            _Disposed = true;
        }

        /// <summary>
        /// Asynchronously dispose the driver.
        /// </summary>
        /// <returns>Value task.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;
            if (_DataSource != null) await _DataSource.DisposeAsync().ConfigureAwait(false);
            _DataSource = null;
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private NpgsqlDataSource RequireSource()
        {
            return _DataSource ?? throw new InvalidOperationException("Driver is not initialized.");
        }

        private async Task RunMigrationsAsync(NpgsqlDataSource source, CancellationToken token)
        {
            await using (NpgsqlCommand create = source.CreateCommand(
                "CREATE TABLE IF NOT EXISTS schema_migrations (version int PRIMARY KEY, description text NOT NULL, applied_utc timestamptz NOT NULL DEFAULT now());"))
            {
                await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            HashSet<int> applied = new HashSet<int>();
            await using (NpgsqlCommand select = source.CreateCommand("SELECT version FROM schema_migrations;"))
            await using (NpgsqlDataReader reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false)) applied.Add(reader.GetInt32(0));
            }

            foreach (SchemaMigration migration in PostgresqlMigrations.All())
            {
                if (applied.Contains(migration.Version)) continue;

                await using (NpgsqlConnection connection = await source.OpenConnectionAsync(token).ConfigureAwait(false))
                await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    foreach (string statement in migration.Statements)
                    {
                        await using (NpgsqlCommand cmd = new NpgsqlCommand(statement, connection, transaction))
                        {
                            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        }
                    }

                    await using (NpgsqlCommand track = new NpgsqlCommand(
                        "INSERT INTO schema_migrations (version, description) VALUES (@v, @d);", connection, transaction))
                    {
                        track.Parameters.AddWithValue("v", migration.Version);
                        track.Parameters.AddWithValue("d", migration.Description);
                        await track.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }

                _Logging?.Info(_Header + "applied migration " + migration.Version + " (" + migration.Description + ")");
            }
        }

        #endregion
    }
}
