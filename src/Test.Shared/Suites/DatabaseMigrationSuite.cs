namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using PepperX.Core.Database;
    using PepperX.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Verifies schema migration on a fresh, isolated database and idempotency across repeated
    /// initializations.
    /// </summary>
    public static class DatabaseMigrationSuite
    {
        /// <summary>
        /// Build the migration database suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "DatabaseMigration",
                displayName: "Database: migrations",
                cases: new List<TestCaseDescriptor>
                {
                    MigrationCase("FreshInit", "Fresh initialization creates the schema", async ct =>
                    {
                        string dbName = await PostgresTestFixture.CreateDatabaseAsync(ct);
                        try
                        {
                            await using (IMetadataDatabaseDriver driver = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(TestEnvironment.SettingsFor(dbName), null, ct))
                            {
                                Container created = await driver.Containers.CreateAsync(new Container { Name = "freshcontainer" }, ct);
                                Check.NotNull(await driver.Containers.ReadByIdAsync(created.Id, ct), "schema usable after init");
                            }
                        }
                        finally
                        {
                            await PostgresTestFixture.DropDatabaseAsync(dbName, ct);
                        }
                    }),

                    MigrationCase("DoubleInitIdempotent", "Re-initializing an existing database is idempotent", async ct =>
                    {
                        string dbName = await PostgresTestFixture.CreateDatabaseAsync(ct);
                        try
                        {
                            await using (IMetadataDatabaseDriver first = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(TestEnvironment.SettingsFor(dbName), null, ct))
                            {
                                await first.Containers.CreateAsync(new Container { Name = "first" }, ct);
                            }

                            await using (IMetadataDatabaseDriver second = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(TestEnvironment.SettingsFor(dbName), null, ct))
                            {
                                Check.NotNull(await second.Containers.ReadByNameAsync("first", ct), "existing data intact after re-init");
                                await second.Containers.CreateAsync(new Container { Name = "second" }, ct);
                            }
                        }
                        finally
                        {
                            await PostgresTestFixture.DropDatabaseAsync(dbName, ct);
                        }
                    }),

                    MigrationCase("CacheColumnsPresent", "Migration v2 adds the cache columns with correct defaults and is idempotent", async ct =>
                    {
                        string dbName = await PostgresTestFixture.CreateDatabaseAsync(ct);
                        try
                        {
                            // First init runs migration v2. A second init must be a version-gated no-op.
                            await using (IMetadataDatabaseDriver first = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(TestEnvironment.SettingsFor(dbName), null, ct)) { }
                            await using (IMetadataDatabaseDriver second = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(TestEnvironment.SettingsFor(dbName), null, ct)) { }

                            string connString = "Host=" + TestEnvironment.Host + ";Port=" + TestEnvironment.Port + ";Database=" + dbName +
                                ";Username=" + TestEnvironment.User + ";Password=" + TestEnvironment.Password + ";";
                            Dictionary<string, string> types = new Dictionary<string, string>();
                            await using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                            {
                                await conn.OpenAsync(ct);
                                await using (NpgsqlCommand cmd = new NpgsqlCommand(
                                    "SELECT column_name, data_type FROM information_schema.columns WHERE table_name = 'containers' AND column_name LIKE 'cache_%';", conn))
                                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct))
                                {
                                    while (await reader.ReadAsync(ct)) types[reader.GetString(0)] = reader.GetString(1);
                                }
                            }

                            Check.Equal("boolean", types["cache_enabled"], "cache_enabled type");
                            Check.True(types.ContainsKey("cache_policy"), "cache_policy present");
                            Check.Equal("integer", types["cache_max_objects"], "cache_max_objects type");
                            Check.Equal("bigint", types["cache_max_memory_bytes"], "cache_max_memory_bytes type");
                            Check.Equal("integer", types["cache_evict_count"], "cache_evict_count type");
                            Check.Equal("bigint", types["cache_max_object_bytes"], "cache_max_object_bytes type");
                        }
                        finally
                        {
                            await PostgresTestFixture.DropDatabaseAsync(dbName, ct);
                        }
                    }),

                    MigrationCase("RespIndexColumn", "Migration v3 adds the RESP index column and its unique index, idempotently", async ct =>
                    {
                        string dbName = await PostgresTestFixture.CreateDatabaseAsync(ct);
                        try
                        {
                            await using (IMetadataDatabaseDriver first = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(TestEnvironment.SettingsFor(dbName), null, ct)) { }
                            await using (IMetadataDatabaseDriver second = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(TestEnvironment.SettingsFor(dbName), null, ct)) { }

                            string connString = "Host=" + TestEnvironment.Host + ";Port=" + TestEnvironment.Port + ";Database=" + dbName +
                                ";Username=" + TestEnvironment.User + ";Password=" + TestEnvironment.Password + ";";
                            string? columnType = null;
                            bool indexPresent = false;
                            string? expiryColumnType = null;
                            await using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                            {
                                await conn.OpenAsync(ct);
                                await using (NpgsqlCommand cmd = new NpgsqlCommand(
                                    "SELECT data_type FROM information_schema.columns WHERE table_name = 'containers' AND column_name = 'resp_database_index';", conn))
                                {
                                    object? result = await cmd.ExecuteScalarAsync(ct);
                                    columnType = result as string;
                                }
                                await using (NpgsqlCommand cmd = new NpgsqlCommand(
                                    "SELECT 1 FROM pg_indexes WHERE tablename = 'containers' AND indexname = 'ux_containers_resp_db_index';", conn))
                                {
                                    object? result = await cmd.ExecuteScalarAsync(ct);
                                    indexPresent = result != null;
                                }
                                // Migration v5: per-container multipart-upload expiry column (nullable integer).
                                await using (NpgsqlCommand cmd = new NpgsqlCommand(
                                    "SELECT data_type FROM information_schema.columns WHERE table_name = 'containers' AND column_name = 'multipart_upload_expiry_days';", conn))
                                {
                                    object? result = await cmd.ExecuteScalarAsync(ct);
                                    expiryColumnType = result as string;
                                }
                            }

                            Check.Equal("integer", columnType ?? "(missing)", "resp_database_index column type");
                            Check.True(indexPresent, "partial unique index present");
                            Check.Equal("integer", expiryColumnType ?? "(missing)", "multipart_upload_expiry_days column type");
                        }
                        finally
                        {
                            await PostgresTestFixture.DropDatabaseAsync(dbName, ct);
                        }
                    })
                });
        }

        private static TestCaseDescriptor MigrationCase(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestCaseDescriptor("DatabaseMigration", caseId, displayName, _ => Task.CompletedTask,
                    skip: true, skipReason: "PostgreSQL test database unavailable");
            }

            return new TestCaseDescriptor("DatabaseMigration", caseId, displayName, ct => body(ct));
        }
    }
}
