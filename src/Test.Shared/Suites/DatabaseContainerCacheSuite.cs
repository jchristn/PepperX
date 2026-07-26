namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using PepperX.Core.Database;
    using PepperX.Core.Enums;
    using PepperX.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Verifies persistence and read-back of per-container cache settings, the disabled column defaults on a
    /// plainly-created container, create-with-settings, and normalization of a deliberately out-of-range
    /// stored row on read.
    /// </summary>
    public static class DatabaseContainerCacheSuite
    {
        /// <summary>
        /// Build the container cache database suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "DatabaseContainerCache",
                displayName: "Database: container cache settings",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.Case("DatabaseContainerCache", "PersistAndRead", "Persist cache settings and read them back", async (driver, ct) =>
                    {
                        Container created = await DbTest.NewContainerAsync(driver, ct);
                        ContainerCacheSettings settings = new ContainerCacheSettings
                        {
                            Enabled = true,
                            Policy = CacheEvictionPolicyEnum.FIFO,
                            MaxObjects = 250,
                            MaxMemoryBytes = 16 * 1024 * 1024,
                            EvictCount = 7,
                            MaxCacheableObjectBytes = 65536
                        };

                        Container? updated = await driver.Containers.UpdateCacheSettingsAsync(created.Id, settings, ct);
                        Check.NotNull(updated, "update returned a row");

                        Container? byId = await driver.Containers.ReadByIdAsync(created.Id, ct);
                        Check.True(byId!.Cache.Enabled, "enabled persisted");
                        Check.Equal(CacheEvictionPolicyEnum.FIFO, byId.Cache.Policy, "policy persisted");
                        Check.Equal(250, byId.Cache.MaxObjects, "max objects persisted");
                        Check.Equal(16L * 1024 * 1024, byId.Cache.MaxMemoryBytes, "memory persisted");
                        Check.Equal(7, byId.Cache.EvictCount, "evict count persisted");
                        Check.Equal(65536L, byId.Cache.MaxCacheableObjectBytes, "ceiling persisted");

                        Container? byName = await driver.Containers.ReadByNameAsync(created.Name, ct);
                        Check.Equal(CacheEvictionPolicyEnum.FIFO, byName!.Cache.Policy, "policy via name read");
                    }),

                    DbTest.Case("DatabaseContainerCache", "DefaultsOnPlainCreate", "A plainly created container carries the disabled column defaults", async (driver, ct) =>
                    {
                        Container created = await DbTest.NewContainerAsync(driver, ct);
                        Container? read = await driver.Containers.ReadByIdAsync(created.Id, ct);
                        Check.False(read!.Cache.Enabled, "disabled by default at the driver layer");
                        Check.Equal(CacheEvictionPolicyEnum.LRU, read.Cache.Policy, "LRU default");
                        Check.Equal(1000, read.Cache.MaxObjects, "MaxObjects default");
                        Check.Equal(10, read.Cache.EvictCount, "EvictCount default");
                        Check.Equal(1048576L, read.Cache.MaxCacheableObjectBytes, "ceiling default");
                    }),

                    DbTest.Case("DatabaseContainerCache", "CreateWithCacheSettings", "Cache settings supplied at create time are persisted", async (driver, ct) =>
                    {
                        Container container = new Container
                        {
                            Name = DbTest.NewContainerName(),
                            Cache = new ContainerCacheSettings { Enabled = true, Policy = CacheEvictionPolicyEnum.LRU, MaxObjects = 42, EvictCount = 4 }
                        };
                        await driver.Containers.CreateAsync(container, ct);

                        Container? read = await driver.Containers.ReadByIdAsync(container.Id, ct);
                        Check.True(read!.Cache.Enabled, "enabled persisted on create");
                        Check.Equal(42, read.Cache.MaxObjects, "max objects persisted on create");
                        Check.Equal(4, read.Cache.EvictCount, "evict count persisted on create");
                    }),

                    NormalizeBadRowCase()
                });
        }

        private static TestCaseDescriptor NormalizeBadRowCase()
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestCaseDescriptor("DatabaseContainerCache", "NormalizeBadRow", "Out-of-range stored row is normalized on read",
                    _ => Task.CompletedTask, skip: true, skipReason: "PostgreSQL test database unavailable");
            }

            return new TestCaseDescriptor("DatabaseContainerCache", "NormalizeBadRow", "Out-of-range stored row is normalized on read", async ct =>
            {
                string dbName = await PostgresTestFixture.CreateDatabaseAsync(ct);
                try
                {
                    await using (IMetadataDatabaseDriver driver = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(TestEnvironment.SettingsFor(dbName), null, ct))
                    {
                        Container created = await driver.Containers.CreateAsync(new Container { Name = "badrow" }, ct);

                        // Hand-corrupt the row the way a bad migration or manual edit might.
                        string connString = "Host=" + TestEnvironment.Host + ";Port=" + TestEnvironment.Port + ";Database=" + dbName +
                            ";Username=" + TestEnvironment.User + ";Password=" + TestEnvironment.Password + ";";
                        await using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                        {
                            await conn.OpenAsync(ct);
                            await using (NpgsqlCommand cmd = new NpgsqlCommand(
                                "UPDATE containers SET cache_enabled = true, cache_policy = 'bogus', cache_max_objects = -3, " +
                                "cache_max_memory_bytes = -100, cache_evict_count = -5, cache_max_object_bytes = -1 WHERE id = @id;", conn))
                            {
                                cmd.Parameters.AddWithValue("id", created.Id);
                                await cmd.ExecuteNonQueryAsync(ct);
                            }
                        }

                        Container? read = await driver.Containers.ReadByIdAsync(created.Id, ct);
                        Check.NotNull(read, "row read back");
                        Check.Equal(CacheEvictionPolicyEnum.LRU, read!.Cache.Policy, "bogus policy normalized to LRU");
                        Check.Equal(1, read.Cache.MaxObjects, "negative MaxObjects clamped to 1");
                        Check.Equal(0L, read.Cache.MaxMemoryBytes, "negative memory clamped to 0");
                        Check.Equal(1, read.Cache.EvictCount, "negative EvictCount clamped to 1");
                        Check.Equal(0L, read.Cache.MaxCacheableObjectBytes, "negative ceiling clamped to 0");
                    }
                }
                finally
                {
                    await PostgresTestFixture.DropDatabaseAsync(dbName, ct);
                }
            });
        }
    }
}
