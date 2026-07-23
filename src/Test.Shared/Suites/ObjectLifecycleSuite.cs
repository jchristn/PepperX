namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the full object lifecycle through the service layer: write, read, replace, metadata, delete,
    /// limits, and the delete-blocks-reads guarantee within a single node.
    /// </summary>
    public static class ObjectLifecycleSuite
    {
        /// <summary>
        /// Build the object lifecycle suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ObjectLifecycle",
                displayName: "Object lifecycle",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.StackCase("ObjectLifecycle", "WriteReadReplaceDelete", "Write, read, replace, and delete an object", async (stack, ct) =>
                    {
                        string container = await NewContainerAsync(stack, ct);
                        await WriteAsync(stack, container, "a/b.bin", "hello", new List<string> { "greeting" }, new Dictionary<string, string> { { "lang", "en" } }, new Dictionary<string, object> { { "n", 1 } }, false, ct);

                        await using (ObjectReadHandle? handle = await stack.Reads.ReadAsync(container, "a/b.bin", null, null, ct))
                        {
                            Check.NotNull(handle, "read handle");
                            Check.Equal("hello", await ReadStringAsync(handle!, ct), "payload");
                        }

                        ObjectMetadata? meta = await stack.Reads.ReadMetadataAsync(container, "a/b.bin", ct);
                        Check.NotNull(meta, "metadata");
                        Check.Equal("en", meta!.Tags["lang"], "tag");
                        Check.NotNull(meta.Object, "metadata object");

                        ObjectWriteResponse replaced = await WriteAsync(stack, container, "a/b.bin", "hello world", null, null, null, false, ct);
                        Check.True(replaced.Replaced, "replace flagged");

                        await using (ObjectReadHandle? handle2 = await stack.Reads.ReadAsync(container, "a/b.bin", null, null, ct))
                        {
                            Check.Equal("hello world", await ReadStringAsync(handle2!, ct), "replaced payload");
                        }

                        Check.True(await stack.Deletes.DeleteAsync(container, "a/b.bin", ct), "deleted");
                        Check.False(await stack.Reads.ExistsAsync(container, "a/b.bin", ct), "gone");
                    }),

                    DbTest.StackCase("ObjectLifecycle", "NoOverwrite", "No-overwrite write rejects an existing key", async (stack, ct) =>
                    {
                        string container = await NewContainerAsync(stack, ct);
                        await WriteAsync(stack, container, "k", "one", null, null, null, false, ct);
                        await Check.ThrowsAsync<ObjectAlreadyExistsException>(
                            () => WriteAsync(stack, container, "k", "two", null, null, null, true, ct),
                            "no-overwrite conflict");
                    }),

                    DbTest.StackCase("ObjectLifecycle", "Limits", "Metadata limits are enforced", async (stack, ct) =>
                    {
                        string container = await NewContainerAsync(stack, ct);
                        List<string> tooMany = new List<string>();
                        for (int i = 0; i < 100; i++) tooMany.Add("label" + i);
                        await Check.ThrowsAsync<ObjectTooLargeException>(
                            () => WriteAsync(stack, container, "k", "x", tooMany, null, null, false, ct),
                            "too many labels rejected");
                    }),

                    DbTest.StackCase("ObjectLifecycle", "UpdateMetadata", "Metadata update rewrites the extent", async (stack, ct) =>
                    {
                        string container = await NewContainerAsync(stack, ct);
                        await WriteAsync(stack, container, "k", "payload", new List<string> { "old" }, null, null, false, ct);

                        UpdateMetadataRequest update = new UpdateMetadataRequest { Labels = new List<string> { "new1", "new2" }, Tags = new Dictionary<string, string> { { "t", "v" } } };
                        await stack.Writes.UpdateMetadataAsync(container, "k", update, ct);

                        ObjectMetadata? meta = await stack.Reads.ReadMetadataAsync(container, "k", ct);
                        Check.Equal(2, meta!.Labels.Count, "labels updated");
                        Check.Equal("v", meta.Tags["t"], "tag updated");

                        await using (ObjectReadHandle? handle = await stack.Reads.ReadAsync(container, "k", null, null, ct))
                        {
                            Check.Equal("payload", await ReadStringAsync(handle!, ct), "payload preserved through rewrite");
                        }
                    }),

                    DbTest.StackCase("ObjectLifecycle", "ReadDuringDelete", "A delete waits for an in-flight read and blocks new reads", async (stack, ct) =>
                    {
                        string container = await NewContainerAsync(stack, ct);
                        await WriteAsync(stack, container, "k", "content", null, null, null, false, ct);

                        ObjectReadHandle? reader = await stack.Reads.ReadAsync(container, "k", null, null, ct);
                        Check.NotNull(reader, "reader open");

                        Task<bool> deleteTask = Task.Run(() => stack.Deletes.DeleteAsync(container, "k", ct), ct);
                        await Task.Delay(300, ct);

                        Check.False(deleteTask.IsCompleted, "delete blocked while read is open");
                        ObjectReadHandle? blocked = await stack.Reads.ReadAsync(container, "k", null, null, ct);
                        Check.True(blocked == null, "new read blocked immediately by tombstone");

                        await reader!.DisposeAsync();
                        bool deleted = await deleteTask;
                        Check.True(deleted, "delete completed after read released");
                        Check.False(await stack.Reads.ExistsAsync(container, "k", ct), "object gone");
                    }),

                    DbTest.StackCase("ObjectLifecycle", "ReplaceChurn", "Concurrent overwrites converge to one active object", async (stack, ct) =>
                    {
                        string container = await NewContainerAsync(stack, ct);
                        await WriteAsync(stack, container, "hot", "seed", null, null, null, false, ct);

                        List<Task> tasks = new List<Task>();
                        for (int i = 0; i < 16; i++)
                        {
                            int n = i;
                            tasks.Add(Task.Run(async () =>
                            {
                                try { await WriteAsync(stack, container, "hot", "v" + n, null, null, null, false, ct); }
                                catch (ConcurrentModificationException) { }
                            }, ct));
                        }
                        await Task.WhenAll(tasks);

                        Check.True(await stack.Reads.ExistsAsync(container, "hot", ct), "one active object");
                        ContainerResponse? c = await stack.Containers.ReadAsync(container, ct);
                        Check.Equal(1L, c!.ObjectCount, "exactly one active object counted");
                    })
                });
        }

        private static async Task<string> NewContainerAsync(ServiceStack stack, CancellationToken ct)
        {
            string name = DbTest.NewContainerName();
            await stack.Containers.CreateAsync(new ContainerCreateRequest { Name = name }, ct);
            return name;
        }

        private static Task<ObjectWriteResponse> WriteAsync(ServiceStack stack, string container, string key, string payload, List<string>? labels, Dictionary<string, string>? tags, object? metadataObject, bool noOverwrite, CancellationToken ct)
        {
            MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            return stack.Writes.WriteAsync(container, key, ms, "text/plain", labels, tags, metadataObject, noOverwrite, ct);
        }

        private static async Task<string> ReadStringAsync(ObjectReadHandle handle, CancellationToken ct)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                await handle.Payload.CopyToAsync(ms, ct);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }
}
