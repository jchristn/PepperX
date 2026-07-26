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
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using PepperX.Core.Storage.Disk;
    using Touchstone.Core;

    /// <summary>
    /// Verifies that a database can be rebuilt from raw extent storage, and that Verify and Repair detect and
    /// reconcile drift.
    /// </summary>
    public static class RehydrationSuite
    {
        /// <summary>
        /// Build the rehydration suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Rehydration",
                displayName: "Rehydration",
                cases: new List<TestCaseDescriptor>
                {
                    RehydrationCase("RebuildFromStorage", "Rebuild reconstructs containers, extents, and counters from storage", async ct =>
                    {
                        IMetadataDatabaseDriver source = await PostgresTestFixture.GetSharedAsync(ct);
                        string root = StorageTestHelper.NewRoot();
                        string targetDb = await PostgresTestFixture.CreateDatabaseAsync(ct);
                        try
                        {
                            ServiceStack stack = await ServiceStack.CreateAsync(source, root, DeleteCoordinationModeEnum.Cluster, "srcnode", ct);
                            string cA = await NewContainerAsync(stack, ct);
                            string cB = await NewContainerAsync(stack, ct);
                            // A container with a distinctive, non-default cache config to confirm a Rebuild
                            // restores cache settings from the manifest (D8/C1-06), not the disabled default.
                            string cC = DbTest.NewContainerName();
                            await stack.Containers.CreateAsync(new ContainerCreateRequest
                            {
                                Name = cC,
                                Cache = new UpdateCacheSettingsRequest { Enabled = true, Policy = CacheEvictionPolicyEnum.FIFO, MaxObjects = 333, EvictCount = 9, MaxCacheableObjectBytes = 4096 },
                                RespDatabaseIndex = 11
                            }, ct);
                            await Write(stack, cA, "photo1", "aaa", new List<string> { "animal" }, new Dictionary<string, string> { { "team", "a" } }, new Dictionary<string, object> { { "meta", 1 } }, ct);
                            await Write(stack, cA, "photo2", "bbbb", new List<string> { "plant" }, null, null, ct);
                            await Write(stack, cB, "doc1", "cc", null, new Dictionary<string, string> { { "tier", "gold" } }, null, ct);
                            await Write(stack, cC, "cached", "dd", null, null, null, ct);

                            await using (IMetadataDatabaseDriver target = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(TestEnvironment.SettingsFor(targetDb), null, ct))
                            {
                                DiskExtentStorageDriver targetStorage = new DiskExtentStorageDriver(new PepperX.Core.Settings.DiskStorageSettings { RootDirectory = root });
                                await targetStorage.InitializeAsync(ct);
                                RehydrationService rehydration = new RehydrationService(target, targetStorage);

                                RehydrationReport report = await rehydration.RehydrateAsync(RehydrationModeEnum.Rebuild, ct);
                                Check.True(report.Success, "rebuild succeeded");
                                Check.Equal(4L, report.ExtentsDiscovered, "four extents discovered");
                                Check.Equal(4L, report.RowsAdded, "four rows added");

                                Container? rebuiltA = await target.Containers.ReadByNameAsync(cA, ct);
                                Check.NotNull(rebuiltA, "container A rebuilt");
                                Check.Equal(2L, rebuiltA!.ObjectCount, "container A object count");

                                Extent? photo1 = await target.Extents.ReadActiveAsync(rebuiltA.Id, "photo1", ct);
                                Check.NotNull(photo1, "photo1 rebuilt");
                                Check.Equal("animal", photo1!.Labels[0], "labels rebuilt");
                                Check.Equal("a", photo1.Tags["team"], "tags rebuilt");
                                Check.True(photo1.HasMetadataObject, "metadata object flag rebuilt");

                                Container? rebuiltB = await target.Containers.ReadByNameAsync(cB, ct);
                                Check.Equal("gold", (await target.Extents.ReadActiveAsync(rebuiltB!.Id, "doc1", ct))!.Tags["tier"], "container B tag rebuilt");

                                // D8: cache settings are mirrored into the manifest, so a Rebuild restores them.
                                Container? rebuiltC = await target.Containers.ReadByNameAsync(cC, ct);
                                Check.True(rebuiltC!.Cache.Enabled, "container C cache enabled restored");
                                Check.Equal(CacheEvictionPolicyEnum.FIFO, rebuiltC.Cache.Policy, "cache policy restored");
                                Check.Equal(333, rebuiltC.Cache.MaxObjects, "cache max objects restored");
                                Check.Equal(9, rebuiltC.Cache.EvictCount, "cache evict count restored");
                                Check.Equal(4096L, rebuiltC.Cache.MaxCacheableObjectBytes, "cache ceiling restored");
                                Check.Equal(11, rebuiltC.RespDatabaseIndex ?? -1, "RESP database index restored from the manifest");
                            }
                        }
                        finally
                        {
                            await PostgresTestFixture.DropDatabaseAsync(targetDb, ct);
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    RehydrationCase("VerifyAndRepairDrift", "Verify reports drift and Repair reconciles it", async ct =>
                    {
                        // Uses an isolated database so that orphan detection reflects the production invariant
                        // of one database per storage backend.
                        string root = StorageTestHelper.NewRoot();
                        string dbName = await PostgresTestFixture.CreateDatabaseAsync(ct);
                        try
                        {
                            await using (IMetadataDatabaseDriver driver = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(TestEnvironment.SettingsFor(dbName), null, ct))
                            {
                                ServiceStack stack = await ServiceStack.CreateAsync(driver, root, DeleteCoordinationModeEnum.Cluster, "driftnode", ct);
                                string container = await NewContainerAsync(stack, ct);
                                await Write(stack, container, "keep", "x", null, null, null, ct);
                                await Write(stack, container, "orphan", "y", null, null, null, ct);

                                Container? c = await driver.Containers.ReadByNameAsync(container, ct);
                                Extent? orphan = await driver.Extents.ReadActiveAsync(c!.Id, "orphan", ct);
                                string absolute = Path.Combine(root, orphan!.StorageLocation.Replace('/', Path.DirectorySeparatorChar));
                                File.Delete(absolute);

                                RehydrationService rehydration = new RehydrationService(driver, stack.Storage);
                                RehydrationReport verify = await rehydration.RehydrateAsync(RehydrationModeEnum.Verify, ct);
                                Check.Equal(0L, verify.RowsRemoved, "verify changes nothing");
                                Check.True(verify.Drift.Count > 0, "verify reports drift");

                                RehydrationReport repair = await rehydration.RehydrateAsync(RehydrationModeEnum.Repair, ct);
                                Check.Equal(1L, repair.RowsRemoved, "repair removes the orphan row");
                                Check.True(await driver.Extents.ReadByIdAsync(orphan.Id, ct) == null, "orphan extent gone");
                            }
                        }
                        finally
                        {
                            await PostgresTestFixture.DropDatabaseAsync(dbName, ct);
                            StorageTestHelper.Cleanup(root);
                        }
                    })
                });
        }

        private static TestCaseDescriptor RehydrationCase(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestCaseDescriptor("Rehydration", caseId, displayName, _ => Task.CompletedTask,
                    skip: true, skipReason: "PostgreSQL test database unavailable");
            }

            return new TestCaseDescriptor("Rehydration", caseId, displayName, ct => body(ct));
        }

        private static async Task<string> NewContainerAsync(ServiceStack stack, CancellationToken ct)
        {
            string name = DbTest.NewContainerName();
            await stack.Containers.CreateAsync(new ContainerCreateRequest { Name = name }, ct);
            return name;
        }

        private static Task Write(ServiceStack stack, string container, string key, string payload, List<string>? labels, Dictionary<string, string>? tags, object? metadataObject, CancellationToken ct)
        {
            MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            return stack.Writes.WriteAsync(container, key, ms, "text/plain", labels, tags, metadataObject, false, ct);
        }
    }
}
