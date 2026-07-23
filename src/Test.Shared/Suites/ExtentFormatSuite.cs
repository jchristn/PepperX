namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Storage;
    using PepperX.Core.Storage.Disk;
    using PepperX.Core.Storage.Format;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the PXE1 extent file format: header fidelity, payload integrity, range reads, and corruption
    /// detection.
    /// </summary>
    public static class ExtentFormatSuite
    {
        /// <summary>
        /// Build the extent format suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ExtentFormat",
                displayName: "Extent file format",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ExtentFormat", "HeaderRoundtrip", "Header fields survive a write and header-only read", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            ExtentHeader header = StorageTestHelper.NewHeader("ctr_photos", "photos", "2026/07/猫.jpg");
                            header.ContentType = "image/jpeg";
                            header.Labels = new List<string> { "animal", "cute" };
                            header.Tags = new Dictionary<string, string> { { "team", "mammals" } };
                            header.Object = new Dictionary<string, object> { { "any", new List<object> { "json", 1, true } } };

                            byte[] payload = System.Text.Encoding.UTF8.GetBytes("hello world");
                            ExtentWriteResult result = await StorageTestHelper.WriteBytesAsync(driver, header, payload, ct).ConfigureAwait(false);

                            ExtentHeader back = await driver.ReadHeaderAsync(result.Location, ct).ConfigureAwait(false);
                            Check.Equal(header.ExtentId, back.ExtentId, "extent id");
                            Check.Equal("2026/07/猫.jpg", back.Key, "unicode key");
                            Check.Equal("image/jpeg", back.ContentType!, "content type");
                            Check.Equal(2, back.Labels.Count, "labels");
                            Check.Equal("mammals", back.Tags["team"], "tag");
                            Check.NotNull(back.Object, "metadata object present");
                            Check.Equal((long)payload.Length, back.SizeBytes, "size");
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("ExtentFormat", "PayloadIntegrity", "Payload bytes round-trip exactly", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            ExtentHeader header = StorageTestHelper.NewHeader("ctr_bin", "bin", "blob");
                            byte[] payload = new byte[4096];
                            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 256);

                            ExtentWriteResult result = await StorageTestHelper.WriteBytesAsync(driver, header, payload, ct).ConfigureAwait(false);
                            byte[] back = await StorageTestHelper.ReadAllAsync(driver, result.Location, true, ct).ConfigureAwait(false);
                            Check.Equal(payload.Length, back.Length, "length");
                            for (int i = 0; i < payload.Length; i++) Check.Equal(payload[i], back[i], "byte " + i);
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("ExtentFormat", "ZeroBytePayload", "A zero-byte payload round-trips", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            ExtentHeader header = StorageTestHelper.NewHeader("ctr_empty", "empty", "nothing");
                            ExtentWriteResult result = await StorageTestHelper.WriteBytesAsync(driver, header, Array.Empty<byte>(), ct).ConfigureAwait(false);
                            Check.Equal(0L, result.SizeBytes, "zero size");
                            byte[] back = await StorageTestHelper.ReadAllAsync(driver, result.Location, true, ct).ConfigureAwait(false);
                            Check.Equal(0, back.Length, "empty read");
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("ExtentFormat", "RangeRead", "Range reads return the correct slice", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            ExtentHeader header = StorageTestHelper.NewHeader("ctr_range", "range", "seq");
                            byte[] payload = new byte[1000];
                            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 256);
                            ExtentWriteResult result = await StorageTestHelper.WriteBytesAsync(driver, header, payload, ct).ConfigureAwait(false);

                            using (ExtentPayloadStream slice = await driver.OpenReadRangeAsync(result.Location, 100, 50, ct).ConfigureAwait(false))
                            using (MemoryStream ms = new MemoryStream())
                            {
                                await slice.CopyToAsync(ms, ct).ConfigureAwait(false);
                                byte[] got = ms.ToArray();
                                Check.Equal(50, got.Length, "slice length");
                                for (int i = 0; i < 50; i++) Check.Equal((byte)((100 + i) % 256), got[i], "slice byte " + i);
                            }
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("ExtentFormat", "TruncationDetected", "A truncated file is rejected", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            ExtentHeader header = StorageTestHelper.NewHeader("ctr_trunc", "trunc", "k");
                            ExtentWriteResult result = await StorageTestHelper.WriteBytesAsync(driver, header, new byte[500], ct).ConfigureAwait(false);

                            string abs = Path.Combine(root, result.Location.Replace('/', Path.DirectorySeparatorChar));
                            using (FileStream fs = new FileStream(abs, FileMode.Open, FileAccess.Write))
                            {
                                fs.SetLength(fs.Length - 10);
                            }

                            await Check.ThrowsAsync<ExtentCorruptException>(
                                () => driver.OpenReadAsync(result.Location, false, ct),
                                "truncated file should be rejected").ConfigureAwait(false);
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("ExtentFormat", "BadMagicDetected", "A bad leading magic is rejected", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            ExtentHeader header = StorageTestHelper.NewHeader("ctr_magic", "magic", "k");
                            ExtentWriteResult result = await StorageTestHelper.WriteBytesAsync(driver, header, new byte[10], ct).ConfigureAwait(false);

                            string abs = Path.Combine(root, result.Location.Replace('/', Path.DirectorySeparatorChar));
                            using (FileStream fs = new FileStream(abs, FileMode.Open, FileAccess.Write))
                            {
                                fs.Seek(0, SeekOrigin.Begin);
                                fs.WriteByte(0x00);
                            }

                            await Check.ThrowsAsync<ExtentCorruptException>(
                                () => driver.ReadHeaderAsync(result.Location, ct),
                                "bad magic should be rejected").ConfigureAwait(false);
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("ExtentFormat", "ChecksumMismatchDetected", "A corrupted payload fails checksum verification", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            ExtentHeader header = StorageTestHelper.NewHeader("ctr_ck", "ck", "k");
                            byte[] payload = new byte[256];
                            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)i;
                            ExtentWriteResult result = await StorageTestHelper.WriteBytesAsync(driver, header, payload, ct).ConfigureAwait(false);

                            string abs = Path.Combine(root, result.Location.Replace('/', Path.DirectorySeparatorChar));
                            ExtentHeader onDisk = await driver.ReadHeaderAsync(result.Location, ct).ConfigureAwait(false);
                            long payloadStart = ExtentFormatConstants.PrefixLength + System.Text.Encoding.UTF8.GetByteCount(new PepperX.Core.Serialization.PepperXSerializer().SerializeJson(onDisk)!);
                            using (FileStream fs = new FileStream(abs, FileMode.Open, FileAccess.Write))
                            {
                                fs.Seek(payloadStart, SeekOrigin.Begin);
                                fs.WriteByte(0xFF);
                            }

                            await Check.ThrowsAsync<ExtentCorruptException>(
                                () => driver.OpenReadAsync(result.Location, true, ct),
                                "checksum mismatch should be rejected").ConfigureAwait(false);
                        }
                        finally
                        {
                            StorageTestHelper.Cleanup(root);
                        }
                    }),

                    new TestCaseDescriptor("ExtentFormat", "LargePayloadStreamed", "A large payload streams through without buffering", async ct =>
                    {
                        string root = StorageTestHelper.NewRoot();
                        try
                        {
                            DiskExtentStorageDriver driver = await StorageTestHelper.NewDriverAsync(root, ct).ConfigureAwait(false);
                            ExtentHeader header = StorageTestHelper.NewHeader("ctr_big", "big", "large");
                            long size = 16L * 1024 * 1024;

                            ExtentWriteResult result;
                            using (GeneratedStream gen = new GeneratedStream(size))
                            {
                                result = await driver.WriteAsync(header, gen, ct).ConfigureAwait(false);
                            }
                            Check.Equal(size, result.SizeBytes, "size");

                            long verified = 0;
                            using (ExtentPayloadStream payload = await driver.OpenReadAsync(result.Location, true, ct).ConfigureAwait(false))
                            {
                                byte[] buffer = new byte[65536];
                                int read;
                                while ((read = await payload.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                                {
                                    for (int i = 0; i < read; i++)
                                    {
                                        if (buffer[i] != GeneratedStream.ByteAt(verified + i))
                                            throw new Exception("payload mismatch at position " + (verified + i));
                                    }
                                    verified += read;
                                }
                            }
                            Check.Equal(size, verified, "verified length");
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
