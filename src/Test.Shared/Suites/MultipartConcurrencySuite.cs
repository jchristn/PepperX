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
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Services;
    using Touchstone.Core;

    /// <summary>
    /// Verifies multipart upload behavior under concurrency and across nodes: parallel part uploads,
    /// concurrent re-upload of the same part, concurrent (double) completion, and cross-node completion.
    /// </summary>
    public static class MultipartConcurrencySuite
    {
        /// <summary>
        /// Build the multipart concurrency suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "MultipartConcurrency",
                displayName: "Multipart concurrency",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.StackCase("MultipartConcurrency", "ConcurrentParts", "Parallel uploads of distinct part numbers all land and assemble in order", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = await NewContainerAsync(stack, ct);
                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "k", null, null, ct);

                        const int count = 8;
                        List<Task> tasks = new List<Task>();
                        for (int i = 1; i <= count; i++)
                        {
                            int part = i;
                            tasks.Add(Task.Run(() => StageAsync(stack, container, upload.Id, part, Bytes("[" + part + "]"), ct), ct));
                        }
                        await Task.WhenAll(tasks);

                        IReadOnlyList<MultipartPart> all = await stack.Db.MultipartUploads.ListAllPartsAsync(upload.Id, ct);
                        Check.Equal(count, all.Count, "all parts landed");

                        CompleteMultipartUploadRequest request = new CompleteMultipartUploadRequest();
                        foreach (MultipartPart p in all) request.Parts.Add(new CompletedPart(p.PartNumber, p.Md5));
                        await stack.Multipart.CompleteAsync(container, upload.Id, request, ct);

                        StringBuilder expected = new StringBuilder();
                        for (int i = 1; i <= count; i++) expected.Append("[" + i + "]");
                        Check.Equal(expected.ToString(), Encoding.UTF8.GetString(await ReadObjectAsync(stack, container, "k", ct)), "assembled in ascending order");
                    }),

                    DbTest.StackCase("MultipartConcurrency", "ConcurrentReupload", "Concurrent re-uploads of one part number leave exactly one row", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = await NewContainerAsync(stack, ct);
                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "k", null, null, ct);

                        List<Task> tasks = new List<Task>();
                        for (int i = 0; i < 6; i++)
                        {
                            int n = i;
                            tasks.Add(Task.Run(async () =>
                            {
                                try { await StageAsync(stack, container, upload.Id, 1, Bytes("payload-" + n), ct); }
                                catch (Exception ex) when (DbTest.IsTransientContention(ex)) { }
                            }, ct));
                        }
                        await Task.WhenAll(tasks);

                        IReadOnlyList<MultipartPart> all = await stack.Db.MultipartUploads.ListAllPartsAsync(upload.Id, ct);
                        Check.Equal(1, all.Count, "exactly one part row for the number");
                    }),

                    DbTest.StackCase("MultipartConcurrency", "DoubleComplete", "Two concurrent completions produce exactly one winner", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = await NewContainerAsync(stack, ct);
                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "k", null, null, ct);
                        MultipartPart r1 = await StageAsync(stack, container, upload.Id, 1, Bytes("only-part"), ct);
                        CompleteMultipartUploadRequest request = new CompleteMultipartUploadRequest();
                        request.Parts.Add(new CompletedPart(1, r1.Md5));

                        int successes = 0;
                        int noSuchUpload = 0;
                        Task Complete() => Task.Run(async () =>
                        {
                            try { await stack.Multipart.CompleteAsync(container, upload.Id, request, ct); Interlocked.Increment(ref successes); }
                            catch (NoSuchUploadException) { Interlocked.Increment(ref noSuchUpload); }
                        }, ct);

                        await Task.WhenAll(Complete(), Complete());
                        Check.Equal(1, successes, "exactly one completion succeeded");
                        Check.Equal(1, noSuchUpload, "the other lost the race with NoSuchUpload");
                        Check.Equal("only-part", Encoding.UTF8.GetString(await ReadObjectAsync(stack, container, "k", ct)), "object present");
                    }),

                    TwoNodeCase("CrossNodeComplete", "Parts uploaded on one node complete on another", async (driver, node1, node2, ct) =>
                    {
                        node1.S3.MultipartMinPartBytes = 0;
                        node2.S3.MultipartMinPartBytes = 0;
                        string container = DbTest.NewContainerName();
                        await node1.Containers.CreateAsync(new ContainerCreateRequest { Name = container }, ct);

                        MultipartUpload upload = await node1.Multipart.InitiateAsync(container, "k", null, null, ct);
                        MultipartPart r1 = await StageAsync(node1, container, upload.Id, 1, Bytes("node1-part-1-"), ct);
                        MultipartPart r2 = await StageAsync(node1, container, upload.Id, 2, Bytes("node1-part-2"), ct);

                        // Complete on node 2 — it must see the shared upload rows and staged blobs.
                        CompleteMultipartUploadRequest request = new CompleteMultipartUploadRequest();
                        request.Parts.Add(new CompletedPart(1, r1.Md5));
                        request.Parts.Add(new CompletedPart(2, r2.Md5));
                        await node2.Multipart.CompleteAsync(container, upload.Id, request, ct);

                        Check.Equal("node1-part-1-node1-part-2", Encoding.UTF8.GetString(await ReadObjectAsync(node1, container, "k", ct)), "readable from node1");
                        Check.Equal("node1-part-1-node1-part-2", Encoding.UTF8.GetString(await ReadObjectAsync(node2, container, "k", ct)), "readable from node2");
                    })
                });
        }

        #region Private-Methods

        private static TestCaseDescriptor TwoNodeCase(string caseId, string displayName, Func<IMetadataDatabaseDriver, ServiceStack, ServiceStack, CancellationToken, Task> body)
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestCaseDescriptor("MultipartConcurrency", caseId, displayName, _ => Task.CompletedTask,
                    skip: true, skipReason: "PostgreSQL test database unavailable");
            }

            return new TestCaseDescriptor("MultipartConcurrency", caseId, displayName, async ct =>
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

        private static async Task<string> NewContainerAsync(ServiceStack stack, CancellationToken ct)
        {
            string name = DbTest.NewContainerName();
            await stack.Containers.CreateAsync(new ContainerCreateRequest { Name = name }, ct);
            return name;
        }

        private static async Task<MultipartPart> StageAsync(ServiceStack stack, string container, string uploadId, int partNumber, byte[] bytes, CancellationToken ct)
        {
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                return await stack.Multipart.UploadPartAsync(container, uploadId, partNumber, ms, ct);
            }
        }

        private static async Task<byte[]> ReadObjectAsync(ServiceStack stack, string container, string key, CancellationToken ct)
        {
            await using (ObjectReadHandle? handle = await stack.Reads.ReadAsync(container, key, null, null, ct))
            {
                Check.NotNull(handle, "read handle for " + key);
                using (MemoryStream ms = new MemoryStream())
                {
                    await handle!.Payload.CopyToAsync(ms, ct);
                    return ms.ToArray();
                }
            }
        }

        private static byte[] Bytes(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        #endregion
    }
}
