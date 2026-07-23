namespace Test.Shared
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using PepperX.Core.Database;
    using PepperX.Core.Settings;

    /// <summary>
    /// Shared PostgreSQL test fixture. Probes availability, provisions a per-run shared database and driver,
    /// and creates isolated databases for tests that need a clean schema. Not for production use.
    /// </summary>
    public static class PostgresTestFixture
    {
        #region Private-Members

        private static readonly object _Lock = new object();
        private static bool _Probed;
        private static bool _Available;

        private static readonly SemaphoreSlim _SharedGate = new SemaphoreSlim(1, 1);
        private static IMetadataDatabaseDriver? _Shared;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Determine whether the test database is reachable. Cached after the first probe.
        /// </summary>
        /// <returns>True if reachable.</returns>
        public static bool IsAvailable()
        {
            lock (_Lock)
            {
                if (_Probed) return _Available;
                _Probed = true;
                _Available = Probe();
                return _Available;
            }
        }

        /// <summary>
        /// Get (creating on first use) the shared, initialized driver for a per-run database.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The shared driver.</returns>
        public static async Task<IMetadataDatabaseDriver> GetSharedAsync(CancellationToken token = default)
        {
            if (_Shared != null) return _Shared;

            await _SharedGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Shared == null)
                {
                    string dbName = await CreateDatabaseAsync(token).ConfigureAwait(false);
                    _Shared = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(TestEnvironment.SettingsFor(dbName), null, token).ConfigureAwait(false);
                }
            }
            finally
            {
                _SharedGate.Release();
            }

            return _Shared;
        }

        /// <summary>
        /// Create a fresh, uniquely named database and return its name.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new database name.</returns>
        public static async Task<string> CreateDatabaseAsync(CancellationToken token = default)
        {
            string name = "pepperx_test_" + Guid.NewGuid().ToString("N");
            await using (NpgsqlConnection connection = new NpgsqlConnection(TestEnvironment.AdminConnectionString()))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                await using (NpgsqlCommand cmd = new NpgsqlCommand("CREATE DATABASE " + name + ";", connection))
                {
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
            return name;
        }

        /// <summary>
        /// Drop a database created by <see cref="CreateDatabaseAsync"/>, terminating its connections first.
        /// </summary>
        /// <param name="databaseName">Database name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task DropDatabaseAsync(string databaseName, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(databaseName)) return;

            await using (NpgsqlConnection connection = new NpgsqlConnection(TestEnvironment.AdminConnectionString()))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                await using (NpgsqlCommand terminate = new NpgsqlCommand(
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @n AND pid <> pg_backend_pid();", connection))
                {
                    terminate.Parameters.AddWithValue("n", databaseName);
                    await terminate.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                await using (NpgsqlCommand drop = new NpgsqlCommand("DROP DATABASE IF EXISTS " + databaseName + ";", connection))
                {
                    await drop.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static bool Probe()
        {
            try
            {
                using (NpgsqlConnection connection = new NpgsqlConnection(TestEnvironment.AdminConnectionString()))
                {
                    connection.Open();
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion
    }
}
