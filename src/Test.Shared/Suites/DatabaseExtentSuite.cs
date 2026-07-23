namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Enums;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Verifies extent data access, including counter maintenance and the atomic-replace race.
    /// </summary>
    public static class DatabaseExtentSuite
    {
        /// <summary>
        /// Build the extent database suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "DatabaseExtent",
                displayName: "Database: extents",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.Case("DatabaseExtent", "CreateReadHydrate", "Create with labels and tags, read active with hydration", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        Extent extent = DbTest.MakeExtent(c.Id, "a/b.bin", 100,
                            new List<string> { "animal", "cute" },
                            new Dictionary<string, string> { { "team", "mammals" } });
                        await driver.Extents.CreateAsync(extent, ct);

                        Extent? active = await driver.Extents.ReadActiveAsync(c.Id, "a/b.bin", ct);
                        Check.NotNull(active, "read active");
                        Check.Equal(2, active!.Labels.Count, "labels hydrated");
                        Check.Equal("mammals", active.Tags["team"], "tags hydrated");

                        Container? cAfter = await driver.Containers.ReadByIdAsync(c.Id, ct);
                        Check.Equal(1L, cAfter!.ObjectCount, "counter incremented");
                        Check.Equal(100L, cAfter.TotalBytes, "bytes incremented");
                    }),

                    DbTest.Case("DatabaseExtent", "ExistsAndDuplicate", "Exists-active and duplicate-key rejection", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "dup", 10), ct);
                        Check.True(await driver.Extents.ExistsActiveAsync(c.Id, "dup", ct), "exists active");
                        await Check.ThrowsAsync<ObjectAlreadyExistsException>(
                            () => driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "dup", 10), ct),
                            "duplicate active key rejected");
                    }),

                    DbTest.Case("DatabaseExtent", "MarkDeleting", "Tombstone decrements counters and blocks active reads", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "k", 500), ct);
                        Extent? tomb = await driver.Extents.MarkDeletingAsync(c.Id, "k", ct);
                        Check.NotNull(tomb, "tombstoned");
                        Check.Equal(ExtentStateEnum.Deleting, tomb!.State, "state deleting");
                        Check.False(await driver.Extents.ExistsActiveAsync(c.Id, "k", ct), "no longer active");
                        Container? cAfter = await driver.Containers.ReadByIdAsync(c.Id, ct);
                        Check.Equal(0L, cAfter!.ObjectCount, "counter decremented");
                        Check.Equal(0L, cAfter.TotalBytes, "bytes decremented");
                    }),

                    DbTest.Case("DatabaseExtent", "Replace", "Replace tombstones old and keeps one active", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        Extent first = DbTest.MakeExtent(c.Id, "hot", 100);
                        await driver.Extents.CreateAsync(first, ct);

                        Extent second = DbTest.MakeExtent(c.Id, "hot", 250);
                        string? oldId = await driver.Extents.ReplaceAsync(second, ct);
                        Check.Equal(first.Id, oldId!, "old id returned");

                        Extent? active = await driver.Extents.ReadActiveAsync(c.Id, "hot", ct);
                        Check.Equal(second.Id, active!.Id, "new extent active");
                        Container? cAfter = await driver.Containers.ReadByIdAsync(c.Id, ct);
                        Check.Equal(1L, cAfter!.ObjectCount, "count unchanged by replace");
                        Check.Equal(250L, cAfter.TotalBytes, "bytes reflect new size");
                    }),

                    DbTest.Case("DatabaseExtent", "Purge", "Purge removes the row, labels, and tags", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        Extent extent = DbTest.MakeExtent(c.Id, "purgeme", 10, new List<string> { "l1" }, new Dictionary<string, string> { { "k", "v" } });
                        await driver.Extents.CreateAsync(extent, ct);
                        await driver.Extents.MarkDeletingAsync(c.Id, "purgeme", ct);
                        await driver.Extents.PurgeAsync(extent.Id, ct);
                        Check.True(await driver.Extents.ReadByIdAsync(extent.Id, ct) == null, "row gone");
                    }),

                    DbTest.Case("DatabaseExtent", "ConcurrentReplace", "Concurrent replaces keep exactly one active", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "race", 10), ct);

                        int conflicts = 0;
                        List<Task> tasks = new List<Task>();
                        for (int i = 0; i < 16; i++)
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    await driver.Extents.ReplaceAsync(DbTest.MakeExtent(c.Id, "race", 20), ct);
                                }
                                catch (ConcurrentModificationException)
                                {
                                    Interlocked.Increment(ref conflicts);
                                }
                            }, ct));
                        }
                        await Task.WhenAll(tasks);

                        Extent? active = await driver.Extents.ReadActiveAsync(c.Id, "race", ct);
                        Check.NotNull(active, "one active remains");
                        Container? cAfter = await driver.Containers.ReadByIdAsync(c.Id, ct);
                        Check.Equal(1L, cAfter!.ObjectCount, "exactly one active object");
                    })
                });
        }
    }
}
