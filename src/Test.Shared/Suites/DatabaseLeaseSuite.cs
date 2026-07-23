namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Verifies read lease data access.
    /// </summary>
    public static class DatabaseLeaseSuite
    {
        /// <summary>
        /// Build the lease database suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "DatabaseLease",
                displayName: "Database: read leases",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.Case("DatabaseLease", "AcquireRelease", "Acquire and release a lease", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        Extent extent = DbTest.MakeExtent(c.Id, "k", 100);
                        await driver.Extents.CreateAsync(extent, ct);

                        LeaseAcquisition? lease = await driver.ReadLeases.AcquireForActiveExtentAsync(c.Id, "k", "nodeA", 30, ct);
                        Check.NotNull(lease, "lease acquired");
                        Check.Equal(extent.Id, lease!.Extent.Id, "extent resolved");
                        Check.Equal(1L, await driver.ReadLeases.CountActiveForExtentAsync(extent.Id, ct), "one active lease");

                        await driver.ReadLeases.ReleaseAsync(lease.LeaseId, ct);
                        Check.Equal(0L, await driver.ReadLeases.CountActiveForExtentAsync(extent.Id, ct), "lease released");
                    }),

                    DbTest.Case("DatabaseLease", "BlockedWhenDeleting", "Acquisition fails once the extent is deleting", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        await driver.Extents.CreateAsync(DbTest.MakeExtent(c.Id, "k", 10), ct);
                        await driver.Extents.MarkDeletingAsync(c.Id, "k", ct);

                        LeaseAcquisition? lease = await driver.ReadLeases.AcquireForActiveExtentAsync(c.Id, "k", "nodeA", 30, ct);
                        Check.True(lease == null, "no lease on deleting extent");
                    }),

                    DbTest.Case("DatabaseLease", "Renew", "Renew extends a lease", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        Extent extent = DbTest.MakeExtent(c.Id, "k", 10);
                        await driver.Extents.CreateAsync(extent, ct);
                        LeaseAcquisition? lease = await driver.ReadLeases.AcquireForActiveExtentAsync(c.Id, "k", "nodeA", 5, ct);
                        Check.True(await driver.ReadLeases.RenewAsync(lease!.LeaseId, 100, ct), "renew succeeds");
                        Check.False(await driver.ReadLeases.RenewAsync("lse_missing", 100, ct), "renew missing fails");
                    }),

                    DbTest.Case("DatabaseLease", "PurgeExpired", "Expired leases are purged", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        Extent extent = DbTest.MakeExtent(c.Id, "k", 10);
                        await driver.Extents.CreateAsync(extent, ct);
                        await driver.ReadLeases.AcquireForActiveExtentAsync(c.Id, "k", "nodeA", 1, ct);
                        await Task.Delay(1300, ct);
                        await driver.ReadLeases.PurgeExpiredAsync(ct);
                        Check.Equal(0L, await driver.ReadLeases.CountActiveForExtentAsync(extent.Id, ct), "expired lease purged");
                    }),

                    DbTest.Case("DatabaseLease", "PurgeForNode", "Leases for a dead node are purged", async (driver, ct) =>
                    {
                        Container c = await DbTest.NewContainerAsync(driver, ct);
                        Extent extent = DbTest.MakeExtent(c.Id, "k", 10);
                        await driver.Extents.CreateAsync(extent, ct);
                        await driver.ReadLeases.AcquireForActiveExtentAsync(c.Id, "k", "deadNode", 60, ct);
                        int purged = await driver.ReadLeases.PurgeForNodeAsync("deadNode", ct);
                        Check.True(purged >= 1, "at least one lease purged");
                        Check.Equal(0L, await driver.ReadLeases.CountActiveForExtentAsync(extent.Id, ct), "node leases gone");
                    })
                });
        }
    }
}
