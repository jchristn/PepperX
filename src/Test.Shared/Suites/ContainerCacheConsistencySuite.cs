namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Enums;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using Touchstone.Core;

    /// <summary>
    /// Verifies cache consistency: the coherence-token invariant under a randomized single-node workload,
    /// read-your-writes, delete visibility, two-node coherence across replace and delete, and write-through
    /// durability (terminal storage is always the system of record).
    /// </summary>
    public static class ContainerCacheConsistencySuite
    {
        /// <summary>
        /// Build the cache consistency suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ContainerCacheConsistency",
                displayName: "Container cache: consistency",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.StackCase("ContainerCacheConsistency", "CoherenceInvariant", "A served hit's extent id always equals the DB active extent", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        string[] keys = { "k0", "k1", "k2" };
                        Random rng = new Random(12345);
                        byte counter = 1;

                        for (int step = 0; step < 200; step++)
                        {
                            string key = keys[rng.Next(keys.Length)];
                            int op = rng.Next(4);
                            if (op == 0 || op == 1)
                            {
                                try { await WriteAsync(stack, c.Name, key, Homogeneous(counter++, 32 + rng.Next(64)), ct); }
                                catch (Exception) { }
                                if (counter == 0) counter = 1;
                            }
                            else if (op == 2)
                            {
                                try { await stack.Deletes.DeleteAsync(c.Name, key, ct); } catch (Exception) { }
                            }
                            else
                            {
                                await using (ObjectReadHandle? handle = await stack.Reads.ReadAsync(c.Name, key, null, null, ct))
                                {
                                    if (handle == null) continue;
                                    byte[] bytes = await DrainAsync(handle, ct);
                                    Check.True(IsHomogeneous(bytes), "served payload is a single coherent version");
                                    Extent? active = await stack.Db.Extents.ReadActiveAsync(c.Id, key, ct);
                                    Check.NotNull(active, "active extent present for a served read");
                                    Check.Equal(active!.Id, handle.Extent.Id, "served extent id equals the current active extent id");
                                }
                            }
                        }
                    }),

                    DbTest.StackCase("ContainerCacheConsistency", "ReadYourWrites", "Every write is immediately visible to the next read", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        await WriteAsync(stack, c.Name, "k", Encoding.UTF8.GetBytes("first"), ct);
                        Check.Equal("first", await ReadStringAsync(stack, c.Name, "k", ct), "read-your-write after create");

                        await WriteAsync(stack, c.Name, "k", Encoding.UTF8.GetBytes("second-longer"), ct);
                        Check.Equal("second-longer", await ReadStringAsync(stack, c.Name, "k", ct), "read-your-write after replace");

                        UpdateMetadataRequest update = new UpdateMetadataRequest { Labels = new List<string> { "L" }, Tags = new Dictionary<string, string> { { "t", "v" } } };
                        await stack.Writes.UpdateMetadataAsync(c.Name, "k", update, ct);
                        ObjectMetadata? meta = await stack.Reads.ReadMetadataAsync(c.Name, "k", ct);
                        Check.Equal("v", meta!.Tags["t"], "metadata update visible immediately");
                        Check.Equal("second-longer", await ReadStringAsync(stack, c.Name, "k", ct), "payload intact after metadata update");
                    }),

                    DbTest.StackCase("ContainerCacheConsistency", "DeleteVisibility", "After a delete no read returns the object and the entry is gone", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        await WriteAsync(stack, c.Name, "k", Encoding.UTF8.GetBytes("x"), ct);
                        await ReadStringAsync(stack, c.Name, "k", ct);

                        Check.True(await stack.Deletes.DeleteAsync(c.Name, "k", ct), "deleted");
                        Check.True(await stack.Reads.ReadAsync(c.Name, "k", null, null, ct) == null, "read misses after delete");
                        Check.Equal(0, stack.Cache.Statistics(c.Id)!.CurrentCount, "cache entry gone");
                    }),

                    DbTest.StackCase("ContainerCacheConsistency", "WriteThroughDurability", "An over-ceiling object bypasses the cache but stays durable", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct, ceiling: 64);
                        byte[] payload = Homogeneous(0x66, 4096); // over the 64-byte ceiling
                        await WriteAsync(stack, c.Name, "big", payload, ct);

                        Check.Equal(0, stack.Cache.Statistics(c.Id)!.CurrentCount, "over-ceiling object not admitted to cache");
                        byte[]? read = await ReadAsync(stack, c.Name, "big", ct);
                        Check.NotNull(read, "readable from terminal storage");
                        Check.True(BytesEqual(payload, read!), "durable bytes intact though uncached");
                    }),

                    TwoNodeCase("TwoNodeCoherence", "Two nodes with independent caches stay coherent across replace and delete", async (driver, node1, node2, ct) =>
                    {
                        string name = DbTest.NewContainerName();
                        await node1.Containers.CreateAsync(new ContainerCreateRequest
                        {
                            Name = name,
                            Cache = new UpdateCacheSettingsRequest { Enabled = true, Policy = CacheEvictionPolicyEnum.LRU, MaxObjects = 1000, EvictCount = 10, MaxCacheableObjectBytes = 1048576 }
                        }, ct);

                        await WriteAsync(node1, name, "k", Encoding.UTF8.GetBytes("v1"), ct);
                        Check.Equal("v1", await ReadStringAsync(node1, name, "k", ct), "node1 sees its write");
                        Check.Equal("v1", await ReadStringAsync(node2, name, "k", ct), "node2 hydrates and sees v1");

                        // node1 replaces; node2's coherence check must detect the new extent id.
                        await WriteAsync(node1, name, "k", Encoding.UTF8.GetBytes("v2-new"), ct);
                        Check.Equal("v2-new", await ReadStringAsync(node2, name, "k", ct), "node2 sees the replacement, not its stale entry");

                        // node1 deletes; node2's next read misses.
                        Check.True(await node1.Deletes.DeleteAsync(name, "k", ct), "node1 deletes");
                        Check.True(await node2.Reads.ReadAsync(name, "k", null, null, ct) == null, "node2 misses after cross-node delete");

                        Container? cRow = await driver.Containers.ReadByNameAsync(name, ct);
                        Check.True(node1.Cache.Statistics(cRow!.Id) != null, "node1 stats independent");
                        Check.True(node2.Cache.Statistics(cRow.Id) != null, "node2 stats independent");
                    }),

                    TwoNodeCase("TwoNodeConcurrentReplace", "node2 readers never see a torn or uncommitted value while node1 replaces, and converge", async (driver, node1, node2, ct) =>
                    {
                        const int versions = 30;
                        string name = DbTest.NewContainerName();
                        await node1.Containers.CreateAsync(new ContainerCreateRequest
                        {
                            Name = name,
                            Cache = new UpdateCacheSettingsRequest { Enabled = true, Policy = CacheEvictionPolicyEnum.LRU, MaxObjects = 1000, EvictCount = 10, MaxCacheableObjectBytes = 1048576 }
                        }, ct);

                        // Each version is a homogeneous payload whose byte value IS the version number, so a
                        // torn read (mixed bytes) or an uncommitted value (out of range) is detectable.
                        await WriteAsync(node1, name, "hot", Homogeneous(1, 64), ct);
                        await ReadAsync(node1, name, "hot", ct);
                        await ReadAsync(node2, name, "hot", ct); // hydrate node2's independent cache

                        ConcurrentBag<Exception> errors = new ConcurrentBag<Exception>();
                        ConcurrentBag<string> bad = new ConcurrentBag<string>();
                        CancellationTokenSource stop = new CancellationTokenSource();

                        // node2 readers: every served value must be one complete, committed version.
                        List<Task> readers = new List<Task>();
                        for (int r = 0; r < 3; r++)
                        {
                            readers.Add(Task.Run(async () =>
                            {
                                while (!stop.IsCancellationRequested)
                                {
                                    try
                                    {
                                        byte[]? b = await ReadAsync(node2, name, "hot", ct);
                                        if (b != null && (b.Length != 64 || !IsHomogeneous(b) || b[0] < 1 || b[0] > versions))
                                            bad.Add(b.Length == 64 && IsHomogeneous(b) ? ("value " + b[0]) : "torn");
                                    }
                                    catch (Exception ex) { errors.Add(ex); }
                                    await Task.Delay(2, ct);
                                }
                            }, ct));
                        }

                        // node1 replaces through the versions.
                        for (int v = 2; v <= versions; v++)
                        {
                            try { await WriteAsync(node1, name, "hot", Homogeneous((byte)v, 64), ct); }
                            catch (ConcurrentModificationException) { }
                            catch (Exception ex) { errors.Add(ex); }
                            await Task.Delay(2, ct);
                        }

                        stop.Cancel();
                        await Task.WhenAll(readers);

                        Check.Equal(0, errors.Count, "no exceptions during concurrent cross-node replace");
                        Check.Equal(0, bad.Count, "node2 never served a torn or uncommitted value");

                        // Convergence: node2's next read reflects node1's final version, not a stale cache entry.
                        byte[]? final = await ReadAsync(node2, name, "hot", ct);
                        Check.NotNull(final, "final read present");
                        Check.Equal(versions, (int)final![0], "node2 converged to node1's latest version");
                    }),

                    TwoNodeCase("TwoNodeConcurrentReadDelete", "node2 readers never see a torn value and miss after node1 deletes", async (driver, node1, node2, ct) =>
                    {
                        string name = DbTest.NewContainerName();
                        await node1.Containers.CreateAsync(new ContainerCreateRequest
                        {
                            Name = name,
                            Cache = new UpdateCacheSettingsRequest { Enabled = true, Policy = CacheEvictionPolicyEnum.LRU, MaxObjects = 1000, EvictCount = 10, MaxCacheableObjectBytes = 1048576 }
                        }, ct);

                        await WriteAsync(node1, name, "hot", Homogeneous(0x55, 64), ct);
                        await ReadAsync(node1, name, "hot", ct);
                        await ReadAsync(node2, name, "hot", ct); // hydrate both

                        ConcurrentBag<Exception> errors = new ConcurrentBag<Exception>();
                        ConcurrentBag<bool> bad = new ConcurrentBag<bool>();
                        CancellationTokenSource stop = new CancellationTokenSource();

                        List<Task> readers = new List<Task>();
                        for (int r = 0; r < 3; r++)
                        {
                            readers.Add(Task.Run(async () =>
                            {
                                while (!stop.IsCancellationRequested)
                                {
                                    try
                                    {
                                        byte[]? b = await ReadAsync(node2, name, "hot", ct);
                                        if (b != null && (b.Length != 64 || !IsHomogeneous(b) || b[0] != 0x55)) bad.Add(true);
                                    }
                                    catch (Exception ex) { errors.Add(ex); }
                                    await Task.Delay(2, ct);
                                }
                            }, ct));
                        }

                        await Task.Delay(75, ct);
                        Check.True(await node1.Deletes.DeleteAsync(name, "hot", ct), "node1 deletes");
                        await Task.Delay(75, ct);
                        stop.Cancel();
                        await Task.WhenAll(readers);

                        Check.Equal(0, errors.Count, "no exceptions during concurrent cross-node delete");
                        Check.Equal(0, bad.Count, "node2 never served a torn or wrong value");
                        Check.True(await node2.Reads.ReadAsync(name, "hot", null, null, ct) == null, "node2 misses after cross-node delete");
                        Check.False(await node2.Reads.ExistsAsync(name, "hot", ct), "object gone cluster-wide");
                    }),

                    TwoNodeCase("TwoNodeMetadataCoherence", "A cross-node metadata rewrite is reflected on the other node's next metadata read", async (driver, node1, node2, ct) =>
                    {
                        string name = DbTest.NewContainerName();
                        await node1.Containers.CreateAsync(new ContainerCreateRequest
                        {
                            Name = name,
                            Cache = new UpdateCacheSettingsRequest { Enabled = true, Policy = CacheEvictionPolicyEnum.LRU, MaxObjects = 1000, EvictCount = 10, MaxCacheableObjectBytes = 1048576 }
                        }, ct);

                        await node1.Writes.WriteAsync(name, "k", new MemoryStream(Encoding.UTF8.GetBytes("payload")),
                            "text/plain", null, new Dictionary<string, string> { { "v", "1" } }, null, false, ct);

                        // node2 hydrates its metadata cache against the first extent.
                        ObjectMetadata? m1 = await node2.Reads.ReadMetadataAsync(name, "k", ct);
                        Check.Equal("1", m1!.Tags["v"], "node2 sees the original metadata");

                        // node1 rewrites the extent with new metadata (a new extent id).
                        await node1.Writes.UpdateMetadataAsync(name, "k", new UpdateMetadataRequest { Tags = new Dictionary<string, string> { { "v", "2" } } }, ct);

                        // node2's coherence check must detect the extent-id change and re-read the new metadata.
                        ObjectMetadata? m2 = await node2.Reads.ReadMetadataAsync(name, "k", ct);
                        Check.Equal("2", m2!.Tags["v"], "node2 sees the rewritten metadata, not its stale entry");
                        Check.Equal("payload", await ReadStringAsync(node2, name, "k", ct), "payload intact through the metadata rewrite");
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor TwoNodeCase(string caseId, string displayName, Func<IMetadataDatabaseDriver, ServiceStack, ServiceStack, CancellationToken, Task> body)
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestCaseDescriptor("ContainerCacheConsistency", caseId, displayName, _ => Task.CompletedTask,
                    skip: true, skipReason: "PostgreSQL test database unavailable");
            }

            return new TestCaseDescriptor("ContainerCacheConsistency", caseId, displayName, async ct =>
            {
                IMetadataDatabaseDriver driver = await PostgresTestFixture.GetSharedAsync(ct).ConfigureAwait(false);
                string root = StorageTestHelper.NewRoot();
                try
                {
                    ServiceStack node1 = await ServiceStack.CreateAsync(driver, root, DeleteCoordinationModeEnum.Cluster, "cnode1", ct).ConfigureAwait(false);
                    ServiceStack node2 = await ServiceStack.CreateAsync(driver, root, DeleteCoordinationModeEnum.Cluster, "cnode2", ct).ConfigureAwait(false);
                    await body(driver, node1, node2, ct).ConfigureAwait(false);
                }
                finally
                {
                    StorageTestHelper.Cleanup(root);
                }
            });
        }

        private static async Task<ContainerResponse> CreateAsync(ServiceStack stack, CancellationToken ct, long ceiling = 1048576)
        {
            ContainerCreateRequest req = new ContainerCreateRequest
            {
                Name = DbTest.NewContainerName(),
                Cache = new UpdateCacheSettingsRequest { Enabled = true, Policy = CacheEvictionPolicyEnum.LRU, MaxObjects = 1000, EvictCount = 10, MaxCacheableObjectBytes = ceiling }
            };
            return await stack.Containers.CreateAsync(req, ct);
        }

        private static Task<ObjectWriteResponse> WriteAsync(ServiceStack stack, string container, string key, byte[] bytes, CancellationToken ct)
        {
            return stack.Writes.WriteAsync(container, key, new MemoryStream(bytes), "application/octet-stream", null, null, null, false, ct);
        }

        private static async Task<byte[]?> ReadAsync(ServiceStack stack, string container, string key, CancellationToken ct)
        {
            await using (ObjectReadHandle? handle = await stack.Reads.ReadAsync(container, key, null, null, ct))
            {
                if (handle == null) return null;
                return await DrainAsync(handle, ct);
            }
        }

        private static async Task<string> ReadStringAsync(ServiceStack stack, string container, string key, CancellationToken ct)
        {
            byte[]? bytes = await ReadAsync(stack, container, key, ct);
            return bytes == null ? String.Empty : Encoding.UTF8.GetString(bytes);
        }

        private static async Task<byte[]> DrainAsync(ObjectReadHandle handle, CancellationToken ct)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                await handle.Payload.CopyToAsync(ms, ct);
                return ms.ToArray();
            }
        }

        private static byte[] Homogeneous(byte value, int length)
        {
            byte[] bytes = new byte[length];
            for (int i = 0; i < length; i++) bytes[i] = value;
            return bytes;
        }

        private static bool IsHomogeneous(byte[] bytes)
        {
            if (bytes.Length == 0) return false;
            byte first = bytes[0];
            for (int i = 1; i < bytes.Length; i++)
            {
                if (bytes[i] != first) return false;
            }
            return true;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        #endregion
    }
}
