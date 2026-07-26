namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Enums;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the cache under concurrency: parallel reads, replace churn, read+write, read+delete, cache
    /// stampede, reconfigure under load, enable/disable races, cross-container isolation, and the
    /// manager-lifecycle race. Assertions are on invariants (completeness, coherence, no exceptions), not on
    /// timing. Cluster coordination mode.
    /// </summary>
    public static class ContainerCacheConcurrencySuite
    {
        // Parallelism is kept moderate on purpose: each cache-miss read/write touches the database (a read
        // lease insert, an extent replace), so very high fan-out can saturate a CI runner's Postgres
        // connection budget and surface transient connection errors unrelated to what these tests verify.
        // The counts here still interleave operations enough to exercise the concurrency invariants.
        private const int _Parallelism = 12;

        /// <summary>
        /// Build the cache concurrency suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ContainerCacheConcurrency",
                displayName: "Container cache: concurrency",
                cases: new List<TestCaseDescriptor>
                {
                    Case("ConcurrentReadsSameKey", "Many parallel reads of one key return identical bytes", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        byte[] payload = Homogeneous(0x41, 128);
                        await WriteAsync(stack, c.Name, "hot", payload, ct);
                        await ReadAsync(stack, c.Name, "hot", ct); // hydrate

                        ConcurrentBag<Exception> errors = new ConcurrentBag<Exception>();
                        ConcurrentBag<bool> mismatches = new ConcurrentBag<bool>();
                        await RunManyAsync(_Parallelism, async () =>
                        {
                            try
                            {
                                byte[]? b = await ReadAsync(stack, c.Name, "hot", ct);
                                if (b == null || !BytesEqual(b, payload)) mismatches.Add(true);
                            }
                            catch (Exception ex) { errors.Add(ex); }
                        });

                        Check.Equal(0, errors.Count, "no exceptions across parallel readers");
                        Check.Equal(0, mismatches.Count, "every reader saw the exact bytes");
                    }),

                    Case("ConcurrentReplaceChurn", "Concurrent replaces converge; the cache matches the active extent", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        await WriteAsync(stack, c.Name, "hot", Homogeneous(0xFF, 64), ct);

                        await RunManyAsync(8, async () =>
                        {
                            try { await WriteAsync(stack, c.Name, "hot", Homogeneous((byte)Environment.CurrentManagedThreadId, 64), ct); }
                            catch (ConcurrentModificationException) { }
                        });

                        Extent? active = await stack.Db.Extents.ReadActiveAsync(c.Id, "hot", ct);
                        Check.NotNull(active, "one active extent remains");
                        await using (ObjectReadHandle? handle = await stack.Reads.ReadAsync(c.Name, "hot", null, null, ct))
                        {
                            Check.NotNull(handle, "final read");
                            Check.Equal(active!.Id, handle!.Extent.Id, "served extent id matches the DB active extent (coherence)");
                            byte[] bytes = await DrainAsync(handle, ct);
                            Check.True(IsHomogeneous(bytes), "final payload is a single complete version, not torn");
                        }
                        ContainerResponse? cr = await stack.Containers.ReadAsync(c.Name, ct);
                        Check.Equal(1L, cr!.ObjectCount, "exactly one active object");
                    }),

                    Case("ConcurrentReadWrite", "Interleaved readers never see a torn payload", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        await WriteAsync(stack, c.Name, "hot", Homogeneous(0xFF, 64), ct);

                        ConcurrentBag<Exception> errors = new ConcurrentBag<Exception>();
                        ConcurrentBag<bool> torn = new ConcurrentBag<bool>();
                        List<Task> tasks = new List<Task>();

                        for (int w = 0; w < 4; w++)
                        {
                            byte val = (byte)(w + 1);
                            tasks.Add(Task.Run(async () =>
                            {
                                for (int i = 0; i < 10; i++)
                                {
                                    try { await WriteAsync(stack, c.Name, "hot", Homogeneous(val, 64), ct); }
                                    catch (ConcurrentModificationException) { }
                                    catch (Exception ex) { errors.Add(ex); }
                                    await Task.Delay(1, ct);
                                }
                            }, ct));
                        }
                        for (int r = 0; r < 4; r++)
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                for (int i = 0; i < 20; i++)
                                {
                                    try
                                    {
                                        byte[]? b = await ReadAsync(stack, c.Name, "hot", ct);
                                        if (b != null && (b.Length != 64 || !IsHomogeneous(b))) torn.Add(true);
                                    }
                                    catch (Exception ex) { errors.Add(ex); }
                                    await Task.Delay(1, ct);
                                }
                            }, ct));
                        }
                        await Task.WhenAll(tasks);

                        Check.Equal(0, errors.Count, "no unexpected exceptions");
                        Check.Equal(0, torn.Count, "no reader ever observed a torn/mixed payload");
                    }),

                    Case("ConcurrentReadDelete", "A delete interleaved with reads never serves a deleted object", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        await WriteAsync(stack, c.Name, "hot", Homogeneous(0x7A, 64), ct);

                        ConcurrentBag<Exception> errors = new ConcurrentBag<Exception>();
                        ConcurrentBag<bool> torn = new ConcurrentBag<bool>();
                        CancellationTokenSource stop = new CancellationTokenSource();

                        // Four readers with a brief pause between reads. Each cache-miss read inserts a
                        // read lease, so a tight unbounded loop of many readers can saturate the lease table
                        // and Npgsql pool on a slow CI runner and surface transient connection errors that
                        // have nothing to do with coherence. This still fully interleaves reads with the
                        // delete while keeping the database load sane.
                        List<Task> readers = new List<Task>();
                        for (int r = 0; r < 4; r++)
                        {
                            readers.Add(Task.Run(async () =>
                            {
                                while (!stop.IsCancellationRequested)
                                {
                                    try
                                    {
                                        byte[]? b = await ReadAsync(stack, c.Name, "hot", ct);
                                        if (b != null && (b.Length != 64 || !IsHomogeneous(b))) torn.Add(true);
                                    }
                                    catch (Exception ex) { errors.Add(ex); }
                                    await Task.Delay(2, ct);
                                }
                            }, ct));
                        }

                        await Task.Delay(75, ct);
                        Check.True(await stack.Deletes.DeleteAsync(c.Name, "hot", ct), "deleted");
                        await Task.Delay(75, ct);
                        stop.Cancel();
                        await Task.WhenAll(readers);

                        Check.Equal(0, errors.Count, "no exceptions during read/delete race");
                        Check.Equal(0, torn.Count, "no torn payloads");
                        Check.False(await stack.Reads.ExistsAsync(c.Name, "hot", ct), "object gone at the end");
                        Check.True(await ReadAsync(stack, c.Name, "hot", ct) == null, "post-delete reads miss");
                    }),

                    Case("CacheStampede", "Concurrent cold misses converge to one coherent entry", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct, enabled: false);
                        byte[] payload = Homogeneous(0x33, 256);
                        await WriteAsync(stack, c.Name, "cold", payload, ct);
                        await stack.Containers.UpdateCacheSettingsAsync(c.Name, Enable(), ct);

                        ConcurrentBag<bool> mismatches = new ConcurrentBag<bool>();
                        ConcurrentBag<Exception> errors = new ConcurrentBag<Exception>();
                        await RunManyAsync(_Parallelism, async () =>
                        {
                            try
                            {
                                byte[]? b = await ReadAsync(stack, c.Name, "cold", ct);
                                if (b == null || !BytesEqual(b, payload)) mismatches.Add(true);
                            }
                            catch (Exception ex) { errors.Add(ex); }
                        });

                        Check.Equal(0, errors.Count, "no exceptions during stampede");
                        Check.Equal(0, mismatches.Count, "every reader got correct bytes");
                        Check.Equal(1, stack.Cache.Statistics(c.Id)!.CurrentCount, "cache converged to a single entry");
                    }),

                    Case("ReconfigureUnderLoad", "Reads/writes proceed while settings are reconfigured", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        for (int i = 0; i < 5; i++) await WriteAsync(stack, c.Name, "k" + i, Homogeneous((byte)i, 64), ct);

                        ConcurrentBag<Exception> errors = new ConcurrentBag<Exception>();
                        List<Task> tasks = new List<Task>();
                        for (int t = 0; t < 4; t++)
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                for (int i = 0; i < 25; i++)
                                {
                                    try
                                    {
                                        await ReadAsync(stack, c.Name, "k" + (i % 5), ct);
                                        await WriteAsync(stack, c.Name, "k" + (i % 5), Homogeneous((byte)(i % 5), 64), ct);
                                    }
                                    catch (ConcurrentModificationException) { }
                                    catch (Exception ex) { errors.Add(ex); }
                                    await Task.Delay(1, ct);
                                }
                            }, ct));
                        }
                        // A single reconfigure driver (manifest writes must not race with each other).
                        tasks.Add(Task.Run(async () =>
                        {
                            CacheEvictionPolicyEnum[] policies = { CacheEvictionPolicyEnum.LRU, CacheEvictionPolicyEnum.FIFO };
                            for (int i = 0; i < 12; i++)
                            {
                                try
                                {
                                    await stack.Containers.UpdateCacheSettingsAsync(c.Name, new UpdateCacheSettingsRequest
                                    {
                                        Enabled = true,
                                        Policy = policies[i % 2],
                                        MaxObjects = 100 + (i * 10),
                                        EvictCount = 5,
                                        MaxCacheableObjectBytes = 1048576
                                    }, ct);
                                }
                                catch (Exception ex) { errors.Add(ex); }
                            }
                        }, ct));
                        await Task.WhenAll(tasks);

                        // Deterministic final Configure, then assert it stuck.
                        await stack.Containers.UpdateCacheSettingsAsync(c.Name, new UpdateCacheSettingsRequest
                        {
                            Enabled = true, Policy = CacheEvictionPolicyEnum.FIFO, MaxObjects = 777, EvictCount = 3, MaxCacheableObjectBytes = 1048576
                        }, ct);

                        Check.Equal(0, errors.Count, "no exceptions under reconfigure load");
                        ContainerCacheResponse cache = await stack.Containers.ReadCacheAsync(c.Name, ct);
                        Check.Equal(CacheEvictionPolicyEnum.FIFO, cache.Policy, "final policy applied");
                        Check.Equal(777, cache.MaxObjects, "final capacity applied");
                        Check.NotNull(await ReadAsync(stack, c.Name, "k0", ct), "reads still work after reconfigure");
                    }),

                    Case("EnableDisableRace", "Flipping enabled under load never corrupts data", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        byte[] payload = Homogeneous(0x5A, 64);
                        await WriteAsync(stack, c.Name, "k", payload, ct);

                        ConcurrentBag<Exception> errors = new ConcurrentBag<Exception>();
                        ConcurrentBag<bool> bad = new ConcurrentBag<bool>();
                        List<Task> tasks = new List<Task>();
                        for (int t = 0; t < 4; t++)
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                for (int i = 0; i < 25; i++)
                                {
                                    try
                                    {
                                        byte[]? b = await ReadAsync(stack, c.Name, "k", ct);
                                        if (b != null && !BytesEqual(b, payload)) bad.Add(true);
                                    }
                                    catch (Exception ex) { errors.Add(ex); }
                                    await Task.Delay(1, ct);
                                }
                            }, ct));
                        }
                        tasks.Add(Task.Run(async () =>
                        {
                            for (int i = 0; i < 10; i++)
                            {
                                try { await stack.Containers.UpdateCacheSettingsAsync(c.Name, (i % 2 == 0) ? Disable() : Enable(), ct); }
                                catch (Exception ex) { errors.Add(ex); }
                            }
                        }, ct));
                        await Task.WhenAll(tasks);

                        Check.Equal(0, errors.Count, "no exceptions while toggling enabled");
                        Check.Equal(0, bad.Count, "reads always returned the correct payload");
                    }),

                    Case("CrossContainerIsolation", "Parallel load across containers keeps data isolated", async (stack, ct) =>
                    {
                        int count = 8;
                        List<ContainerResponse> containers = new List<ContainerResponse>();
                        for (int i = 0; i < count; i++)
                        {
                            ContainerResponse c = await CreateAsync(stack, ct);
                            await WriteAsync(stack, c.Name, "k", Homogeneous((byte)i, 64), ct);
                            containers.Add(c);
                        }

                        ConcurrentBag<bool> leaks = new ConcurrentBag<bool>();
                        ConcurrentBag<Exception> errors = new ConcurrentBag<Exception>();
                        List<Task> tasks = new List<Task>();
                        for (int i = 0; i < count; i++)
                        {
                            ContainerResponse c = containers[i];
                            byte expected = (byte)i;
                            tasks.Add(Task.Run(async () =>
                            {
                                for (int r = 0; r < 20; r++)
                                {
                                    try
                                    {
                                        byte[]? b = await ReadAsync(stack, c.Name, "k", ct);
                                        if (b == null || b.Length != 64 || b[0] != expected || !IsHomogeneous(b)) leaks.Add(true);
                                    }
                                    catch (Exception ex) { errors.Add(ex); }
                                }
                            }, ct));
                        }
                        await Task.WhenAll(tasks);

                        Check.Equal(0, errors.Count, "no exceptions across containers");
                        Check.Equal(0, leaks.Count, "no byte crossed a container boundary");
                    }),

                    Case("ManagerLifecycleRace", "Cache remove/rebuild churn never throws a disposed-cache error", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        byte[] payload = Homogeneous(0x24, 64);
                        await WriteAsync(stack, c.Name, "k", payload, ct);

                        ConcurrentBag<Exception> errors = new ConcurrentBag<Exception>();
                        ConcurrentBag<bool> bad = new ConcurrentBag<bool>();
                        CancellationTokenSource stop = new CancellationTokenSource();

                        List<Task> tasks = new List<Task>();
                        for (int t = 0; t < 4; t++)
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                while (!stop.IsCancellationRequested)
                                {
                                    try
                                    {
                                        byte[]? b = await ReadAsync(stack, c.Name, "k", ct);
                                        if (b != null && !BytesEqual(b, payload)) bad.Add(true);
                                    }
                                    catch (Exception ex) { errors.Add(ex); }
                                    await Task.Delay(2, ct);
                                }
                            }, ct));
                        }
                        Task churner = Task.Run(() =>
                        {
                            ContainerCacheSettings settings = ContainerCacheSettings.CreationDefault();
                            for (int i = 0; i < 200 && !stop.IsCancellationRequested; i++)
                            {
                                try
                                {
                                    stack.Cache.Remove(c.Id);
                                    stack.Cache.Get(c.Id, settings);
                                }
                                catch (Exception ex) { errors.Add(ex); }
                            }
                        }, ct);

                        await churner;
                        await Task.Delay(30, ct);
                        stop.Cancel();
                        await Task.WhenAll(tasks);

                        Check.Equal(0, errors.Count, "no disposed-cache or other error leaked to callers");
                        Check.Equal(0, bad.Count, "reads always returned correct bytes (from cache or storage)");
                        Check.NotNull(await ReadAsync(stack, c.Name, "k", ct), "object still readable after the race");
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<ServiceStack, CancellationToken, Task> body)
        {
            return DbTest.StackCase("ContainerCacheConcurrency", caseId, displayName, body);
        }

        private static async Task<ContainerResponse> CreateAsync(ServiceStack stack, CancellationToken ct, bool enabled = true)
        {
            ContainerCreateRequest req = new ContainerCreateRequest
            {
                Name = DbTest.NewContainerName(),
                Cache = new UpdateCacheSettingsRequest { Enabled = enabled, Policy = CacheEvictionPolicyEnum.LRU, MaxObjects = 1000, EvictCount = 10, MaxCacheableObjectBytes = 1048576 }
            };
            return await stack.Containers.CreateAsync(req, ct);
        }

        private static UpdateCacheSettingsRequest Enable()
        {
            return new UpdateCacheSettingsRequest { Enabled = true, Policy = CacheEvictionPolicyEnum.LRU, MaxObjects = 1000, EvictCount = 10, MaxCacheableObjectBytes = 1048576 };
        }

        private static UpdateCacheSettingsRequest Disable()
        {
            return new UpdateCacheSettingsRequest { Enabled = false };
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

        private static async Task<byte[]> DrainAsync(ObjectReadHandle handle, CancellationToken ct)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                await handle.Payload.CopyToAsync(ms, ct);
                return ms.ToArray();
            }
        }

        private static Task RunManyAsync(int count, Func<Task> body)
        {
            List<Task> tasks = new List<Task>(count);
            for (int i = 0; i < count; i++) tasks.Add(Task.Run(body));
            return Task.WhenAll(tasks);
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
