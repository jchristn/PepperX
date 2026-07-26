namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Responses;
    using Touchstone.Core;

    /// <summary>
    /// Verifies container data access.
    /// </summary>
    public static class DatabaseContainerSuite
    {
        /// <summary>
        /// Build the container database suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "DatabaseContainer",
                displayName: "Database: containers",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.Case("DatabaseContainer", "CreateReadExists", "Create, read by id and name, and exists", async (driver, ct) =>
                    {
                        Container created = await DbTest.NewContainerAsync(driver, ct);
                        Container? byId = await driver.Containers.ReadByIdAsync(created.Id, ct);
                        Container? byName = await driver.Containers.ReadByNameAsync(created.Name, ct);
                        Check.NotNull(byId, "read by id");
                        Check.NotNull(byName, "read by name");
                        Check.Equal(created.Id, byName!.Id, "same container");
                        Check.True(await driver.Containers.ExistsAsync(created.Name, ct), "exists true");
                        Check.False(await driver.Containers.ExistsAsync("does-not-exist-xyz", ct), "exists false");
                    }),

                    DbTest.Case("DatabaseContainer", "UpdateTags", "Update container tags", async (driver, ct) =>
                    {
                        Container created = await DbTest.NewContainerAsync(driver, ct);
                        Container? updated = await driver.Containers.UpdateTagsAsync(created.Id, new Dictionary<string, string> { { "tier", "gold" } }, ct);
                        Check.NotNull(updated, "updated returned");
                        Check.Equal("gold", updated!.Tags["tier"], "tag persisted");
                    }),

                    DbTest.Case("DatabaseContainer", "Counters", "Adjust counters", async (driver, ct) =>
                    {
                        Container created = await DbTest.NewContainerAsync(driver, ct);
                        await driver.Containers.AdjustCountersAsync(created.Id, 3, 3000, ct);
                        Container? after = await driver.Containers.ReadByIdAsync(created.Id, ct);
                        Check.Equal(3L, after!.ObjectCount, "object count");
                        Check.Equal(3000L, after.TotalBytes, "total bytes");
                        await driver.Containers.AdjustCountersAsync(created.Id, -1, -1000, ct);
                        Container? after2 = await driver.Containers.ReadByIdAsync(created.Id, ct);
                        Check.Equal(2L, after2!.ObjectCount, "object count decremented");
                    }),

                    DbTest.Case("DatabaseContainer", "RespIndexAssignReadClear", "Assign, look up, and clear a RESP database index", async (driver, ct) =>
                    {
                        Container created = await DbTest.NewContainerAsync(driver, ct);

                        Container? assigned = await driver.Containers.UpdateRespDatabaseIndexAsync(created.Id, 5, ct);
                        Check.Equal(5, assigned!.RespDatabaseIndex ?? -1, "index assigned");

                        Container? byId = await driver.Containers.ReadByIdAsync(created.Id, ct);
                        Check.Equal(5, byId!.RespDatabaseIndex ?? -1, "index persisted");

                        Container? byIndex = await driver.Containers.ReadByRespDatabaseIndexAsync(5, ct);
                        Check.NotNull(byIndex, "lookup by index finds the container");
                        Check.Equal(created.Id, byIndex!.Id, "same container");

                        Container? cleared = await driver.Containers.UpdateRespDatabaseIndexAsync(created.Id, null, ct);
                        Check.True(cleared!.RespDatabaseIndex == null, "index cleared");
                        Check.True(await driver.Containers.ReadByRespDatabaseIndexAsync(5, ct) == null, "lookup finds nothing after clear");
                    }),

                    DbTest.Case("DatabaseContainer", "RespIndexUnique", "A RESP database index is unique across containers", async (driver, ct) =>
                    {
                        Container first = await DbTest.NewContainerAsync(driver, ct);
                        Container second = await DbTest.NewContainerAsync(driver, ct);

                        await driver.Containers.UpdateRespDatabaseIndexAsync(first.Id, 9, ct);
                        await Check.ThrowsAsync<PepperXException>(
                            () => driver.Containers.UpdateRespDatabaseIndexAsync(second.Id, 9, ct),
                            "assigning an already-claimed index conflicts");

                        // The second container can still claim a different index.
                        Container? ok = await driver.Containers.UpdateRespDatabaseIndexAsync(second.Id, 10, ct);
                        Check.Equal(10, ok!.RespDatabaseIndex ?? -1, "a free index is accepted");
                    }),

                    DbTest.Case("DatabaseContainer", "Delete", "Delete a container", async (driver, ct) =>
                    {
                        Container created = await DbTest.NewContainerAsync(driver, ct);
                        Check.True(await driver.Containers.DeleteAsync(created.Id, ct), "deleted");
                        Check.False(await driver.Containers.ExistsAsync(created.Name, ct), "gone");
                        Check.False(await driver.Containers.DeleteAsync(created.Id, ct), "second delete false");
                    }),

                    DbTest.Case("DatabaseContainer", "Enumerate", "Enumerate containers with pagination", async (driver, ct) =>
                    {
                        for (int i = 0; i < 5; i++) await DbTest.NewContainerAsync(driver, ct);
                        EnumerationQuery query = new EnumerationQuery { MaxResults = 3 };
                        EnumerationResult<Container> page = await driver.Containers.EnumerateAsync(query, ct);
                        Check.True(page.Objects.Count <= 3, "page size respected");
                        Check.True(page.TotalRecords >= 5, "total at least five");
                    }),

                    DbTest.Case("DatabaseContainer", "Statistics", "Read all statistics", async (driver, ct) =>
                    {
                        Container created = await DbTest.NewContainerAsync(driver, ct);
                        await driver.Containers.AdjustCountersAsync(created.Id, 2, 200, ct);
                        IReadOnlyList<ContainerStatistics> stats = await driver.Containers.ReadAllStatisticsAsync(ct);
                        bool found = false;
                        foreach (ContainerStatistics s in stats)
                        {
                            if (s.Id == created.Id) { found = true; Check.Equal(2L, s.ObjectCount, "stat object count"); }
                        }
                        Check.True(found, "container present in stats");
                    })
                });
        }
    }
}
