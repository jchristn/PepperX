namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Caching;
    using PepperX.Core.Database;
    using PepperX.Core.Enums;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using Touchstone.Core;

    /// <summary>
    /// A bounded soak: a sustained mixed workload (write/replace/delete/read/metadata) across several
    /// cache-enabled containers, followed by a verification pass that asserts each cache honored its
    /// object and memory bounds and that the coherence-token invariant still holds (every served hit's
    /// extent id equals the database's current active extent). Guarded behind the <c>PEPPERX_SOAK</c>
    /// environment variable so continuous integration skips it by default; set it to run locally.
    /// </summary>
    public static class ContainerCacheSoakSuite
    {
        private const int _Containers = 3;
        private const int _Keys = 12;
        private const int _MaxObjects = 8;
        private const long _MaxMemoryBytes = 262144;
        private const long _Ceiling = 4096;
        private const int _Workers = 3;
        private const int _IterationsPerWorker = 120;

        /// <summary>
        /// Build the cache soak suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ContainerCacheSoak",
                displayName: "Container cache: soak",
                cases: new List<TestCaseDescriptor>
                {
                    SoakCase("MixedWorkload", "Sustained mixed workload keeps caches bounded and coherent", RunAsync)
                });
        }

        private static async Task RunAsync(ServiceStack stack, CancellationToken ct)
        {
            List<ContainerResponse> containers = new List<ContainerResponse>();
            for (int i = 0; i < _Containers; i++)
            {
                containers.Add(await stack.Containers.CreateAsync(new ContainerCreateRequest
                {
                    Name = DbTest.NewContainerName(),
                    Cache = new UpdateCacheSettingsRequest
                    {
                        Enabled = true,
                        Policy = i % 2 == 0 ? CacheEvictionPolicyEnum.LRU : CacheEvictionPolicyEnum.FIFO,
                        MaxObjects = _MaxObjects,
                        MaxMemoryBytes = _MaxMemoryBytes,
                        EvictCount = 2,
                        MaxCacheableObjectBytes = _Ceiling
                    }
                }, ct));
            }

            ConcurrentBag<Exception> errors = new ConcurrentBag<Exception>();
            ConcurrentBag<bool> torn = new ConcurrentBag<bool>();

            List<Task> workers = new List<Task>();
            for (int w = 0; w < _Workers; w++)
            {
                int seed = w * 7919;
                workers.Add(Task.Run(async () =>
                {
                    Random rng = new Random(seed);
                    for (int i = 0; i < _IterationsPerWorker; i++)
                    {
                        string container = containers[rng.Next(_Containers)].Name;
                        string key = "k" + rng.Next(_Keys);
                        int op = rng.Next(7);
                        try
                        {
                            if (op <= 1)
                            {
                                await stack.Writes.WriteAsync(container, key, new MemoryStream(Homogeneous((byte)(rng.Next(1, 251)), 512)),
                                    "application/octet-stream", null, null, null, false, ct).ConfigureAwait(false);
                            }
                            else if (op == 2)
                            {
                                await stack.Deletes.DeleteAsync(container, key, ct).ConfigureAwait(false);
                            }
                            else if (op == 6)
                            {
                                await stack.Reads.ReadMetadataAsync(container, key, ct).ConfigureAwait(false);
                            }
                            else
                            {
                                byte[]? b = await ReadAsync(stack, container, key, ct).ConfigureAwait(false);
                                if (b != null && !IsHomogeneous(b)) torn.Add(true);
                            }
                        }
                        catch (PepperX.Core.Exceptions.ConcurrentModificationException) { }
                        catch (PepperX.Core.Exceptions.ObjectNotFoundException) { }
                        catch (Exception ex) { errors.Add(ex); }
                        await Task.Delay(1, ct).ConfigureAwait(false);
                    }
                }, ct));
            }
            await Task.WhenAll(workers).ConfigureAwait(false);

            Check.Equal(0, errors.Count, "no unexpected exceptions during the soak");
            Check.Equal(0, torn.Count, "no torn payload observed during the soak");

            // Verification pass (quiescent): bounds and coherence per container.
            foreach (ContainerResponse c in containers)
            {
                CacheStatistics? stats = stack.Cache.Statistics(c.Id);
                if (stats != null)
                {
                    Check.True(stats.CurrentCount <= _MaxObjects, "cache count within the object bound for " + c.Name);
                    Check.True(stats.CurrentMemoryBytes <= _MaxMemoryBytes, "cache memory within the byte bound for " + c.Name);
                }

                for (int k = 0; k < _Keys; k++)
                {
                    string key = "k" + k;
                    Extent? active = await stack.Db.Extents.ReadActiveAsync(c.Id, key, ct).ConfigureAwait(false);
                    await using (ObjectReadHandle? handle = await stack.Reads.ReadAsync(c.Name, key, null, null, ct).ConfigureAwait(false))
                    {
                        if (active == null)
                        {
                            Check.True(handle == null, "no read served for a deleted key " + c.Name + "/" + key);
                        }
                        else
                        {
                            Check.NotNull(handle, "active key is readable " + c.Name + "/" + key);
                            Check.Equal(active.Id, handle!.Extent.Id, "served extent id equals the active extent id for " + c.Name + "/" + key);
                            byte[] bytes = await DrainAsync(handle, ct).ConfigureAwait(false);
                            Check.True(IsHomogeneous(bytes), "served payload is a single coherent version for " + c.Name + "/" + key);
                        }
                    }
                }
            }
        }

        #region Helpers

        private static TestCaseDescriptor SoakCase(string caseId, string displayName, Func<ServiceStack, CancellationToken, Task> body)
        {
            bool enabled = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PEPPERX_SOAK"));
            if (!enabled || !PostgresTestFixture.IsAvailable())
            {
                string reason = !enabled ? "set PEPPERX_SOAK to run the soak suite" : "PostgreSQL test database unavailable";
                return new TestCaseDescriptor("ContainerCacheSoak", caseId, displayName, _ => Task.CompletedTask, skip: true, skipReason: reason);
            }

            return new TestCaseDescriptor("ContainerCacheSoak", caseId, displayName, async ct =>
            {
                IMetadataDatabaseDriver driver = await PostgresTestFixture.GetSharedAsync(ct).ConfigureAwait(false);
                string root = StorageTestHelper.NewRoot();
                try
                {
                    ServiceStack stack = await ServiceStack.CreateAsync(driver, root, DeleteCoordinationModeEnum.Cluster, null, ct).ConfigureAwait(false);
                    await body(stack, ct).ConfigureAwait(false);
                }
                finally
                {
                    StorageTestHelper.Cleanup(root);
                }
            });
        }

        private static async Task<byte[]?> ReadAsync(ServiceStack stack, string container, string key, CancellationToken ct)
        {
            await using (ObjectReadHandle? handle = await stack.Reads.ReadAsync(container, key, null, null, ct).ConfigureAwait(false))
            {
                if (handle == null) return null;
                return await DrainAsync(handle, ct).ConfigureAwait(false);
            }
        }

        private static async Task<byte[]> DrainAsync(ObjectReadHandle handle, CancellationToken ct)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                await handle.Payload.CopyToAsync(ms, ct).ConfigureAwait(false);
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

        #endregion
    }
}
