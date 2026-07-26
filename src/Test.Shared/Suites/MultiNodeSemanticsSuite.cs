namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Enums;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Services;
    using Touchstone.Core;

    /// <summary>
    /// Verifies cluster semantics using two independent service stacks (two node identities) over one shared
    /// database and one shared storage root.
    /// </summary>
    public static class MultiNodeSemanticsSuite
    {
        /// <summary>
        /// Build the multi-node semantics suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "MultiNodeSemantics",
                displayName: "Multi-node semantics",
                cases: new List<TestCaseDescriptor>
                {
                    TwoNodeCase("CrossNodeDeleteWaitsForRead", "A delete on one node waits for a read on another", async (driver, node1, node2, ct) =>
                    {
                        // Caching disabled: the cross-node delete-waits-for-readers guarantee protects a read
                        // that streams from storage under a database lease. A cache hit holds no lease (D2), so
                        // this case exercises the uncached path where the lease drain is observable.
                        string container = DbTest.NewContainerName();
                        await node1.Containers.CreateAsync(new ContainerCreateRequest { Name = container, Cache = new UpdateCacheSettingsRequest { Enabled = false } }, ct);
                        await WriteAsync(node1, container, "k", "content", ct);

                        ObjectReadHandle? reader = await node1.Reads.ReadAsync(container, "k", null, null, ct);
                        Check.NotNull(reader, "node1 reader open");

                        Task<bool> deleteTask = Task.Run(() => node2.Deletes.DeleteAsync(container, "k", ct), ct);
                        await Task.Delay(300, ct);

                        Check.False(deleteTask.IsCompleted, "node2 delete blocked by node1 read");
                        ObjectReadHandle? blocked = await node1.Reads.ReadAsync(container, "k", null, null, ct);
                        Check.True(blocked == null, "new read on node1 blocked by tombstone");

                        await reader!.DisposeAsync();
                        Check.True(await deleteTask, "delete completed after node1 read released");
                        Check.False(await node2.Reads.ExistsAsync(container, "k", ct), "object gone cluster-wide");
                    }),

                    TwoNodeCase("JanitorReclaimsDeadNodeLeases", "The janitor reclaims leases held by a dead node", async (driver, node1, node2, ct) =>
                    {
                        string container = DbTest.NewContainerName();
                        await node1.Containers.CreateAsync(new ContainerCreateRequest { Name = container }, ct);
                        await WriteAsync(node1, container, "k", "content", ct);

                        Container? c = await driver.Containers.ReadByNameAsync(container, ct);
                        Extent? extent = await driver.Extents.ReadActiveAsync(c!.Id, "k", ct);
                        LeaseAcquisition? ghost = await driver.ReadLeases.AcquireForActiveExtentAsync(c.Id, "k", "ghostnode", 600, ct);
                        Check.NotNull(ghost, "ghost lease acquired");
                        Check.Equal(1L, await driver.ReadLeases.CountActiveForExtentAsync(extent!.Id, ct), "ghost lease present");

                        await driver.Nodes.UpsertHeartbeatAsync(new NodeRecord { Id = "ghostnode", Hostname = "ghost", StartedUtc = DateTime.UtcNow.AddHours(-2), LastHeartbeatUtc = DateTime.UtcNow.AddHours(-2) }, ct);

                        await node2.Janitor.RunOnceAsync(ct);
                        Check.Equal(0L, await driver.ReadLeases.CountActiveForExtentAsync(extent.Id, ct), "dead node lease reclaimed");
                    })
                });
        }

        private static TestCaseDescriptor TwoNodeCase(string caseId, string displayName, Func<IMetadataDatabaseDriver, ServiceStack, ServiceStack, CancellationToken, Task> body)
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestCaseDescriptor("MultiNodeSemantics", caseId, displayName, _ => Task.CompletedTask,
                    skip: true, skipReason: "PostgreSQL test database unavailable");
            }

            return new TestCaseDescriptor("MultiNodeSemantics", caseId, displayName, async ct =>
            {
                IMetadataDatabaseDriver driver = await PostgresTestFixture.GetSharedAsync(ct).ConfigureAwait(false);
                string root = StorageTestHelper.NewRoot();
                try
                {
                    ServiceStack node1 = await ServiceStack.CreateAsync(driver, root, DeleteCoordinationModeEnum.Cluster, "node1", ct).ConfigureAwait(false);
                    ServiceStack node2 = await ServiceStack.CreateAsync(driver, root, DeleteCoordinationModeEnum.Cluster, "node2", ct).ConfigureAwait(false);
                    await body(driver, node1, node2, ct).ConfigureAwait(false);
                }
                finally
                {
                    StorageTestHelper.Cleanup(root);
                }
            });
        }

        private static Task WriteAsync(ServiceStack stack, string container, string key, string payload, CancellationToken ct)
        {
            MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            return stack.Writes.WriteAsync(container, key, ms, "text/plain", null, null, null, false, ct);
        }
    }
}
