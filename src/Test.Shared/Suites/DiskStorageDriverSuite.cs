namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using PepperX.Core.Storage;
    using PepperX.Core.Storage.Disk;
    using PepperX.Core.Storage.Format;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the disk storage driver: lifecycle, concurrent reads, manifests, enumeration, capacity, and
    /// temp cleanup.
    /// </summary>
    public static class DiskStorageDriverSuite
    {
        /// <summary>
        /// Build the disk storage driver suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "DiskStorageDriver",
                displayName: "Disk storage driver",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("DiskStorageDriver", "WriteReadDeleteExists", "Write, read, exists, and delete behave", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            ExtentHeader header = StorageTestHelper.NewHeader("ctr_a", "cona", "k1");
                            byte[] payload = System.Text.Encoding.UTF8.GetBytes("payload-content");
                            ExtentWriteResult result = await StorageTestHelper.WriteBytesAsync(driver, header, payload, ct).ConfigureAwait(false);

                            Check.True(await driver.ExistsAsync(result.Location, ct).ConfigureAwait(false), "exists after write");
                            byte[] back = await StorageTestHelper.ReadAllAsync(driver, result.Location, false, ct).ConfigureAwait(false);
                            Check.Equal("payload-content", System.Text.Encoding.UTF8.GetString(back), "content");

                            Check.True(await driver.DeleteAsync(result.Location, ct).ConfigureAwait(false), "delete returns true");
                            Check.False(await driver.ExistsAsync(result.Location, ct).ConfigureAwait(false), "not exists after delete");
                            Check.False(await driver.DeleteAsync(result.Location, ct).ConfigureAwait(false), "second delete returns false");
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("DiskStorageDriver", "ConcurrentReads", "Many parallel readers of one extent all succeed", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            ExtentHeader header = StorageTestHelper.NewHeader("ctr_par", "conpar", "shared");
                            byte[] payload = new byte[8192];
                            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 256);
                            ExtentWriteResult result = await StorageTestHelper.WriteBytesAsync(driver, header, payload, ct).ConfigureAwait(false);

                            List<Task> readers = new List<Task>();
                            for (int r = 0; r < 32; r++)
                            {
                                readers.Add(Task.Run(async () =>
                                {
                                    byte[] back = await StorageTestHelper.ReadAllAsync(driver, result.Location, false, ct).ConfigureAwait(false);
                                    if (back.Length != payload.Length) throw new Exception("length mismatch");
                                    for (int i = 0; i < payload.Length; i++)
                                        if (back[i] != payload[i]) throw new Exception("byte mismatch at " + i);
                                }, ct));
                            }
                            await Task.WhenAll(readers).ConfigureAwait(false);
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("DiskStorageDriver", "ManifestRoundtrip", "Container manifests round-trip", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            ContainerManifest manifest = new ContainerManifest
                            {
                                Id = "ctr_man",
                                Name = "conman",
                                Tags = new Dictionary<string, string> { { "tier", "gold" } }
                            };
                            await driver.WriteContainerManifestAsync(manifest, ct).ConfigureAwait(false);

                            ContainerManifest? back = await driver.ReadContainerManifestAsync("ctr_man", ct).ConfigureAwait(false);
                            Check.NotNull(back, "manifest read");
                            Check.Equal("conman", back!.Name, "manifest name");
                            Check.Equal("gold", back.Tags["tier"], "manifest tag");

                            IReadOnlyList<ContainerManifest> all = await driver.ReadAllContainerManifestsAsync(ct).ConfigureAwait(false);
                            Check.Equal(1, all.Count, "all manifests count");
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("DiskStorageDriver", "EnumerateFidelity", "Enumeration finds every written extent", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            HashSet<string> written = new HashSet<string>();
                            for (int i = 0; i < 25; i++)
                            {
                                string container = i % 2 == 0 ? "ctr_x" : "ctr_y";
                                ExtentHeader header = StorageTestHelper.NewHeader(container, "con" + (i % 2), "key" + i);
                                ExtentWriteResult result = await StorageTestHelper.WriteBytesAsync(driver, header, new byte[16], ct).ConfigureAwait(false);
                                written.Add(result.Location);
                            }

                            HashSet<string> found = new HashSet<string>();
                            foreach (string location in driver.EnumerateExtentLocations()) found.Add(location);

                            Check.Equal(written.Count, found.Count, "enumerated count");
                            foreach (string location in written) Check.True(found.Contains(location), "location found: " + location);
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("DiskStorageDriver", "Capacity", "Capacity reports a positive total", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            StorageCapacity capacity = await driver.GetCapacityAsync(ct).ConfigureAwait(false);
                            Check.True(capacity.TotalBytes > 0, "total bytes positive");
                            Check.True(capacity.FreeBytes >= 0, "free bytes non-negative");
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("DiskStorageDriver", "TempCleanup", "Stale temp files are removed", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            string tempFile = Path.Combine(root, ".tmp", "stale.payload.tmp");
                            await File.WriteAllTextAsync(tempFile, "stale", ct).ConfigureAwait(false);
                            File.SetLastWriteTimeUtc(tempFile, DateTime.UtcNow.AddHours(-2));

                            int removed = await driver.CleanupTempFilesAsync(TimeSpan.FromHours(1), ct).ConfigureAwait(false);
                            Check.Equal(1, removed, "one temp removed");
                            Check.False(File.Exists(tempFile), "temp gone");
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    })
                });
        }
    }
}
