namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
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
