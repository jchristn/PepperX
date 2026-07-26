namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Caching;
    using PepperX.Core.Enums;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using Touchstone.Core;

    /// <summary>
    /// Verifies functional cache behavior at the service layer: read hits skip storage, misses hydrate,
    /// write-through, delete-first, coherence on replace, metadata hits, range reads, the size ceiling,
    /// eviction policy, memory-cap eviction, reconfigure/disable/enable, statistics, and that a
    /// cache-disabled container is unaffected. Runs in Cluster coordination mode (DB read leases), matching
    /// the other service-stack suites; a cache hit deliberately holds no lease (D2).
    /// </summary>
    public static class ContainerCacheSuite
    {
        /// <summary>
        /// Build the functional cache suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ContainerCache",
                displayName: "Container cache: functional",
                cases: new List<TestCaseDescriptor>
                {
                    Case("ReadHitSkipsStorage", "A validated hit is served from memory without storage", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        byte[] payload = Encoding.UTF8.GetBytes("hello world");
                        await WriteAsync(stack, c.Name, "k", payload, ct);
                        await ReadAsync(stack, c.Name, "k", null, null, ct); // ensure resident

                        await DeleteStorageFileAsync(stack, c.Id, "k", ct); // storage now gone; DB still Active
                        byte[]? again = await ReadAsync(stack, c.Name, "k", null, null, ct);
                        Check.NotNull(again, "served from cache despite missing storage file");
                        Check.Equal("hello world", Encoding.UTF8.GetString(again!), "cached bytes intact");
                        Check.True(stack.Cache.Statistics(c.Id)!.HitCount >= 1, "hit recorded");
                    }),

                    Case("MissHydrates", "A cold read misses then hydrates so the next read hits", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct, enabled: false);
                        await WriteAsync(stack, c.Name, "k", Encoding.UTF8.GetBytes("data"), ct); // not cached (disabled)
                        await stack.Containers.UpdateCacheSettingsAsync(c.Name, Enable(), ct);

                        byte[]? first = await ReadAsync(stack, c.Name, "k", null, null, ct);
                        Check.Equal("data", Encoding.UTF8.GetString(first!), "cold read correct");
                        await ReadAsync(stack, c.Name, "k", null, null, ct);

                        CacheStatistics stats = stack.Cache.Statistics(c.Id)!;
                        Check.True(stats.MissCount >= 1, "miss recorded on cold read");
                        Check.True(stats.HitCount >= 1, "hit recorded after hydration");
                    }),

                    Case("WriteThrough", "A write populates the cache before acknowledging", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        byte[] payload = RandomBytes(4096);
                        await WriteAsync(stack, c.Name, "k", payload, ct);

                        await DeleteStorageFileAsync(stack, c.Id, "k", ct); // never read from storage
                        byte[]? read = await ReadAsync(stack, c.Name, "k", null, null, ct);
                        Check.NotNull(read, "write-through hit");
                        Check.True(BytesEqual(payload, read!), "cached bytes equal what was written");
                    }),

                    Case("DeleteFirst", "Delete evicts the cache entry before terminal deletion", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        await WriteAsync(stack, c.Name, "k", Encoding.UTF8.GetBytes("bye"), ct);
                        await ReadAsync(stack, c.Name, "k", null, null, ct);
                        Check.Equal(1, stack.Cache.Statistics(c.Id)!.CurrentCount, "entry resident before delete");

                        Check.True(await stack.Deletes.DeleteAsync(c.Name, "k", ct), "deleted");
                        Check.Equal(0, stack.Cache.Statistics(c.Id)!.CurrentCount, "entry evicted");
                        Check.False(await stack.Reads.ExistsAsync(c.Name, "k", ct), "gone");
                    }),

                    Case("CoherenceOnReplace", "A replace is reflected; the stale entry is not served", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        await WriteAsync(stack, c.Name, "k", Encoding.UTF8.GetBytes("v1"), ct);
                        await ReadAsync(stack, c.Name, "k", null, null, ct);
                        await WriteAsync(stack, c.Name, "k", Encoding.UTF8.GetBytes("v2-longer"), ct);

                        byte[]? read = await ReadAsync(stack, c.Name, "k", null, null, ct);
                        Check.Equal("v2-longer", Encoding.UTF8.GetString(read!), "new payload served after replace");
                    }),

                    Case("MetadataAndExistsHit", "Metadata and exists are served from a validated hit", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        await WriteAsync(stack, c.Name, "k", Encoding.UTF8.GetBytes("m"), ct,
                            new List<string> { "L1" }, new Dictionary<string, string> { { "t", "v" } }, new Dictionary<string, object> { { "n", 5 } });
                        await ReadAsync(stack, c.Name, "k", null, null, ct); // hydrate incl. metadata

                        await DeleteStorageFileAsync(stack, c.Id, "k", ct);
                        ObjectMetadata? meta = await stack.Reads.ReadMetadataAsync(c.Name, "k", ct);
                        Check.NotNull(meta, "metadata from cache without storage");
                        Check.Equal("v", meta!.Tags["t"], "tag from cached metadata");
                        Check.NotNull(meta.Object, "freeform object present in cached metadata");
                        Check.True(await stack.Reads.ExistsAsync(c.Name, "k", ct), "exists true");
                    }),

                    Case("RangeReadSlicesCache", "A range read after a full hit slices the cached payload", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        byte[] payload = RandomBytes(200);
                        await WriteAsync(stack, c.Name, "k", payload, ct);
                        await ReadAsync(stack, c.Name, "k", null, null, ct); // full read hydrates

                        await DeleteStorageFileAsync(stack, c.Id, "k", ct);
                        byte[]? slice = await ReadAsync(stack, c.Name, "k", 10, 20, ct);
                        Check.NotNull(slice, "range served from cached full payload");
                        Check.Equal(20, slice!.Length, "range length");
                        byte[] expected = new byte[20];
                        Array.Copy(payload, 10, expected, 0, 20);
                        Check.True(BytesEqual(expected, slice), "range bytes match the cached payload slice");
                        Check.Equal(1, stack.Cache.Statistics(c.Id)!.CurrentCount, "range read added no extra entry");
                    }),

                    Case("SizeCeilingBoundary", "The per-object ceiling admits exactly-at-limit and rejects over-limit", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct, ceiling: 1024);
                        // SizeBytes = payload + 512 overhead; ceiling 1024 admits a payload of <= 512.
                        await WriteAsync(stack, c.Name, "fits", RandomBytes(512), ct);
                        await WriteAsync(stack, c.Name, "toobig", RandomBytes(4096), ct);

                        CacheStatistics stats = stack.Cache.Statistics(c.Id)!;
                        Check.Equal(1, stats.CurrentCount, "only the within-ceiling object is cached");

                        await DeleteStorageFileAsync(stack, c.Id, "fits", ct);
                        Check.NotNull(await ReadAsync(stack, c.Name, "fits", null, null, ct), "within-ceiling object served from cache");
                    }),

                    Case("CeilingZeroNoLimit", "A zero ceiling caches lazily on read regardless of size", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct, ceiling: 0);
                        byte[] payload = RandomBytes(20000);
                        await WriteAsync(stack, c.Name, "k", payload, ct); // capture skipped for 0 ceiling
                        await ReadAsync(stack, c.Name, "k", null, null, ct); // hydrate on read

                        await DeleteStorageFileAsync(stack, c.Id, "k", ct);
                        byte[]? again = await ReadAsync(stack, c.Name, "k", null, null, ct);
                        Check.True(BytesEqual(payload, again!), "large object cached under zero ceiling");
                    }),

                    Case("FifoEviction", "FIFO evicts the oldest-inserted key", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct, policy: CacheEvictionPolicyEnum.FIFO, maxObjects: 3, evict: 1);
                        for (int i = 1; i <= 3; i++) await WriteAsync(stack, c.Name, "k" + i, Encoding.UTF8.GetBytes("v" + i), ct);
                        await WriteAsync(stack, c.Name, "k4", Encoding.UTF8.GetBytes("v4"), ct); // evicts k1

                        await DeleteAllStorageFilesAsync(stack, c.Id, ct, "k1", "k2", "k3", "k4");
                        Check.False(await IsCachedAsync(stack, c.Name, "k1", ct), "k1 evicted (oldest)");
                        Check.True(await IsCachedAsync(stack, c.Name, "k3", ct), "k3 retained");
                        Check.True(await IsCachedAsync(stack, c.Name, "k4", ct), "k4 retained");
                    }),

                    Case("LruEviction", "LRU evicts the least-recently-used key", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct, policy: CacheEvictionPolicyEnum.LRU, maxObjects: 3, evict: 1);
                        for (int i = 1; i <= 3; i++) await WriteAsync(stack, c.Name, "k" + i, Encoding.UTF8.GetBytes("v" + i), ct);
                        await ReadAsync(stack, c.Name, "k1", null, null, ct); // touch k1 -> MRU
                        await WriteAsync(stack, c.Name, "k4", Encoding.UTF8.GetBytes("v4"), ct); // evicts LRU = k2

                        await DeleteAllStorageFilesAsync(stack, c.Id, ct, "k1", "k2", "k3", "k4");
                        Check.True(await IsCachedAsync(stack, c.Name, "k1", ct), "k1 retained (recently used)");
                        Check.False(await IsCachedAsync(stack, c.Name, "k2", ct), "k2 evicted (least recently used)");
                        Check.True(await IsCachedAsync(stack, c.Name, "k4", ct), "k4 retained");
                    }),

                    Case("MemoryCapEviction", "The memory cap bounds resident bytes", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct, maxObjects: 1000, memBytes: 4096, ceiling: 8192);
                        for (int i = 0; i < 8; i++) await WriteAsync(stack, c.Name, "k" + i, RandomBytes(1000), ct);

                        CacheStatistics stats = stack.Cache.Statistics(c.Id)!;
                        Check.True(stats.CurrentMemoryBytes <= 4096, "resident memory within the cap");
                        Check.True(stats.CurrentCount < 8, "some objects evicted under memory pressure");
                    }),

                    Case("DisableEnableReconfigure", "Disabling drops the cache; re-enabling starts cold", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct);
                        await WriteAsync(stack, c.Name, "k", Encoding.UTF8.GetBytes("x"), ct);
                        await ReadAsync(stack, c.Name, "k", null, null, ct);
                        Check.Equal(1, stack.Cache.Statistics(c.Id)!.CurrentCount, "resident while enabled");

                        await stack.Containers.UpdateCacheSettingsAsync(c.Name, Disable(), ct);
                        Check.True(stack.Cache.Statistics(c.Id) == null, "cache dropped when disabled");
                        Check.Equal("x", Encoding.UTF8.GetString((await ReadAsync(stack, c.Name, "k", null, null, ct))!), "reads bypass to storage when disabled");

                        await stack.Containers.UpdateCacheSettingsAsync(c.Name, Enable(), ct);
                        Check.Equal(0, stack.Cache.Statistics(c.Id)!.CurrentCount, "re-enabled cache starts cold");
                    }),

                    Case("StatisticsAccuracy", "Hit/miss/hit-rate match a scripted sequence", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct, enabled: false);
                        await WriteAsync(stack, c.Name, "k", Encoding.UTF8.GetBytes("s"), ct);
                        await stack.Containers.UpdateCacheSettingsAsync(c.Name, Enable(), ct);

                        await ReadAsync(stack, c.Name, "k", null, null, ct); // miss + hydrate
                        await ReadAsync(stack, c.Name, "k", null, null, ct); // hit
                        await ReadAsync(stack, c.Name, "k", null, null, ct); // hit

                        CacheStatistics stats = stack.Cache.Statistics(c.Id)!;
                        Check.Equal(2L, stats.HitCount, "two hits");
                        Check.Equal(1L, stats.MissCount, "one miss");
                        Check.True(Math.Abs(stats.HitRate - (2.0 / 3.0)) < 0.001, "hit rate 2/3");
                    }),

                    Case("DisabledContainerUnaffected", "A cache-disabled container behaves as before and holds no cache", async (stack, ct) =>
                    {
                        ContainerResponse c = await CreateAsync(stack, ct, enabled: false);
                        await WriteAsync(stack, c.Name, "k", Encoding.UTF8.GetBytes("plain"), ct);
                        Check.Equal("plain", Encoding.UTF8.GetString((await ReadAsync(stack, c.Name, "k", null, null, ct))!), "read works");
                        Check.True(stack.Cache.Statistics(c.Id) == null, "no cache instance for a disabled container");
                        Check.True(await stack.Deletes.DeleteAsync(c.Name, "k", ct), "delete works");
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<ServiceStack, CancellationToken, Task> body)
        {
            return DbTest.StackCase("ContainerCache", caseId, displayName, body);
        }

        private static async Task<ContainerResponse> CreateAsync(ServiceStack stack, CancellationToken ct,
            bool enabled = true, CacheEvictionPolicyEnum policy = CacheEvictionPolicyEnum.LRU,
            int maxObjects = 1000, long memBytes = 0, int evict = 10, long ceiling = 1048576)
        {
            ContainerCreateRequest req = new ContainerCreateRequest
            {
                Name = DbTest.NewContainerName(),
                Cache = new UpdateCacheSettingsRequest
                {
                    Enabled = enabled,
                    Policy = policy,
                    MaxObjects = maxObjects,
                    MaxMemoryBytes = memBytes,
                    EvictCount = evict,
                    MaxCacheableObjectBytes = ceiling
                }
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

        private static Task<ObjectWriteResponse> WriteAsync(ServiceStack stack, string container, string key, byte[] bytes, CancellationToken ct,
            List<string>? labels = null, Dictionary<string, string>? tags = null, object? obj = null)
        {
            return stack.Writes.WriteAsync(container, key, new MemoryStream(bytes), "application/octet-stream", labels, tags, obj, false, ct);
        }

        private static async Task<byte[]?> ReadAsync(ServiceStack stack, string container, string key, long? offset, long? count, CancellationToken ct)
        {
            await using (ObjectReadHandle? handle = await stack.Reads.ReadAsync(container, key, offset, count, ct))
            {
                if (handle == null) return null;
                using (MemoryStream ms = new MemoryStream())
                {
                    await handle.Payload.CopyToAsync(ms, ct);
                    return ms.ToArray();
                }
            }
        }

        private static async Task DeleteStorageFileAsync(ServiceStack stack, string containerId, string key, CancellationToken ct)
        {
            Extent? active = await stack.Db.Extents.ReadActiveAsync(containerId, key, ct);
            if (active != null) await stack.Storage.DeleteAsync(active.StorageLocation, ct);
        }

        private static async Task DeleteAllStorageFilesAsync(ServiceStack stack, string containerId, CancellationToken ct, params string[] keys)
        {
            foreach (string key in keys) await DeleteStorageFileAsync(stack, containerId, key, ct);
        }

        private static async Task<bool> IsCachedAsync(ServiceStack stack, string container, string key, CancellationToken ct)
        {
            try
            {
                byte[]? bytes = await ReadAsync(stack, container, key, null, null, ct);
                return bytes != null;
            }
            catch (Exception)
            {
                return false; // storage file removed, so a miss cannot hydrate: proves the entry was not cached
            }
        }

        private static byte[] RandomBytes(int length)
        {
            byte[] bytes = new byte[length];
            new Random(length * 2654435761u.GetHashCode()).NextBytes(bytes);
            return bytes;
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
