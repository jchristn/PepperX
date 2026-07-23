namespace PepperX.Sdk.Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Sdk;
    using PepperX.Sdk.Models;
    using Touchstone.Core;

    /// <summary>
    /// Test suites exercising the PepperX C# SDK against a live node. The node URL comes from
    /// <c>PEPPERX_URL</c> (REST) and <c>PEPPERX_WS_URL</c> (WebSockets); suites skip when the node is
    /// unreachable.
    /// </summary>
    public static class SdkSuites
    {
        #region Public-Members

        /// <summary>
        /// All registered suites.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor> { RestSuite(), WebsocketSuite() };
            }
        }

        /// <summary>
        /// REST base URL under test.
        /// </summary>
        public static string RestUrl
        {
            get
            {
                return Environment.GetEnvironmentVariable("PEPPERX_URL") ?? "http://127.0.0.1:8000";
            }
        }

        /// <summary>
        /// WebSocket URL under test.
        /// </summary>
        public static string WebsocketUrl
        {
            get
            {
                return Environment.GetEnvironmentVariable("PEPPERX_WS_URL") ?? "ws://127.0.0.1:8002/";
            }
        }

        #endregion

        #region Private-Members

        private static bool? _Available;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the REST client suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor RestSuite()
        {
            return new TestSuiteDescriptor("SdkRest", "SDK: REST client", new List<TestCaseDescriptor>
            {
                Case("SdkRest", "Health", "Health check succeeds", async ct =>
                {
                    using (PepperXRestClient client = new PepperXRestClient(RestUrl))
                    {
                        Assert(await client.HealthAsync(ct), "health check");
                    }
                }),

                Case("SdkRest", "ContainerLifecycle", "Container create, read, enumerate, tags, delete", async ct =>
                {
                    using (PepperXRestClient client = new PepperXRestClient(RestUrl))
                    {
                        string name = NewContainerName();
                        ContainerResponse created = await client.CreateContainerAsync(name, new Dictionary<string, string> { { "team", "sdk" } }, ct);
                        AssertEqual(name, created.Name, "created name");

                        ContainerResponse? read = await client.ReadContainerAsync(name, ct);
                        Assert(read != null, "container read");
                        AssertEqual("sdk", read!.Tags["team"], "tag round-trips");
                        Assert(await client.ContainerExistsAsync(name, ct), "container exists");

                        ContainerResponse updated = await client.UpdateContainerTagsAsync(name, new Dictionary<string, string> { { "tier", "gold" } }, ct);
                        AssertEqual("gold", updated.Tags["tier"], "tags replaced");

                        EnumerationResult<ContainerResponse> page = await client.EnumerateContainersAsync(new EnumerationQuery { MaxResults = 1000 }, ct);
                        Assert(page.TotalRecords >= 1, "containers enumerated");

                        await client.DeleteContainerAsync(name, false, ct);
                        Assert(!await client.ContainerExistsAsync(name, ct), "container deleted");
                    }
                }),

                Case("SdkRest", "ObjectLifecycle", "Object write, read, metadata, delete", async ct =>
                {
                    using (PepperXRestClient client = new PepperXRestClient(RestUrl))
                    {
                        string container = NewContainerName();
                        await client.CreateContainerAsync(container, null, ct);
                        try
                        {
                            byte[] payload = Encoding.UTF8.GetBytes("sdk-payload");
                            WriteObjectRequest metadata = new WriteObjectRequest
                            {
                                ContentType = "text/plain",
                                Labels = new List<string> { "sdk", "test" },
                                Tags = new Dictionary<string, string> { { "lang", "csharp" } },
                                Object = new Dictionary<string, object> { { "nested", true } }
                            };

                            ObjectWriteResponse write = await client.WriteObjectAsync(container, "a/b/c.txt", payload, metadata, ct);
                            Assert(!String.IsNullOrEmpty(write.ExtentId), "extent id returned");
                            AssertEqual(payload.Length, (int)write.SizeBytes, "size reported");

                            ObjectReadResult? read = await client.ReadObjectAsync(container, "a/b/c.txt", ct);
                            Assert(read != null, "object read");
                            AssertEqual("sdk-payload", Encoding.UTF8.GetString(read!.Data), "payload round-trips");

                            ObjectMetadata? meta = await client.ReadObjectMetadataAsync(container, "a/b/c.txt", ct);
                            Assert(meta != null, "metadata read");
                            AssertEqual(2, meta!.Labels.Count, "labels round-trip");
                            AssertEqual("csharp", meta.Tags["lang"], "tags round-trip");
                            Assert(meta.Object != null, "metadata object round-trips");

                            Assert(await client.ObjectExistsAsync(container, "a/b/c.txt", ct), "object exists");
                            Assert(await client.DeleteObjectAsync(container, "a/b/c.txt", ct), "object deleted");
                            Assert(!await client.ObjectExistsAsync(container, "a/b/c.txt", ct), "object gone");
                        }
                        finally
                        {
                            await client.DeleteContainerAsync(container, true, ct);
                        }
                    }
                }),

                Case("SdkRest", "Streaming", "Objects stream without buffering", async ct =>
                {
                    using (PepperXRestClient client = new PepperXRestClient(RestUrl))
                    {
                        string container = NewContainerName();
                        await client.CreateContainerAsync(container, null, ct);
                        try
                        {
                            byte[] payload = new byte[256 * 1024];
                            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 251);

                            using (MemoryStream source = new MemoryStream(payload, false))
                            {
                                await client.WriteObjectAsync(container, "large.bin", source, new WriteObjectRequest { ContentType = "application/octet-stream" }, ct);
                            }

                            using (Stream? stream = await client.OpenObjectAsync(container, "large.bin", ct))
                            {
                                Assert(stream != null, "stream opened");
                                using (MemoryStream target = new MemoryStream())
                                {
                                    await stream!.CopyToAsync(target, ct);
                                    byte[] roundTripped = target.ToArray();
                                    AssertEqual(payload.Length, roundTripped.Length, "streamed length");
                                    for (int i = 0; i < payload.Length; i += 4096)
                                    {
                                        if (payload[i] != roundTripped[i]) throw new Exception("streamed payload mismatch at " + i);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            await client.DeleteContainerAsync(container, true, ct);
                        }
                    }
                }),

                Case("SdkRest", "Search", "Label and tag search returns typed results", async ct =>
                {
                    using (PepperXRestClient client = new PepperXRestClient(RestUrl))
                    {
                        string container = NewContainerName();
                        await client.CreateContainerAsync(container, null, ct);
                        try
                        {
                            await client.WriteObjectAsync(container, "match", Encoding.UTF8.GetBytes("a"),
                                new WriteObjectRequest { Labels = new List<string> { "findme" }, Tags = new Dictionary<string, string> { { "tier", "gold" } } }, ct);
                            await client.WriteObjectAsync(container, "other", Encoding.UTF8.GetBytes("b"),
                                new WriteObjectRequest { Labels = new List<string> { "ignore" } }, ct);

                            EnumerationResult<ObjectMetadata> found = await client.EnumerateObjectsAsync(container,
                                new EnumerationQuery { Labels = new List<string> { "findme" }, Tags = new Dictionary<string, string> { { "tier", "gold" } } }, ct);

                            AssertEqual(1L, found.TotalRecords, "one match");
                            AssertEqual("match", found.Objects[0].Key, "correct object");
                        }
                        finally
                        {
                            await client.DeleteContainerAsync(container, true, ct);
                        }
                    }
                }),

                Case("SdkRest", "TypedErrors", "Server errors surface as typed exceptions", async ct =>
                {
                    using (PepperXRestClient client = new PepperXRestClient(RestUrl))
                    {
                        string container = NewContainerName();
                        await client.CreateContainerAsync(container, null, ct);
                        try
                        {
                            try
                            {
                                await client.CreateContainerAsync(container, null, ct);
                                throw new Exception("expected a conflict for a duplicate container");
                            }
                            catch (PepperXException ex)
                            {
                                AssertEqual(ApiErrorEnum.Conflict, ex.ErrorType, "conflict classification");
                                AssertEqual(409, ex.StatusCode, "conflict status");
                            }

                            Assert(await client.ReadContainerAsync("no-such-container-xyz", ct) == null, "missing container reads null");
                            Assert(await client.ReadObjectAsync(container, "missing", ct) == null, "missing object reads null");
                        }
                        finally
                        {
                            await client.DeleteContainerAsync(container, true, ct);
                        }
                    }
                }),

                Case("SdkRest", "Statistics", "Statistics and nodes return typed objects", async ct =>
                {
                    using (PepperXRestClient client = new PepperXRestClient(RestUrl))
                    {
                        StatisticsResponse stats = await client.StatisticsAsync(ct);
                        Assert(stats.ContainerCount >= 0, "container count present");
                        List<NodeResponse> nodes = await client.NodesAsync(ct);
                        Assert(nodes.Count >= 1, "at least one node");
                    }
                })
            });
        }

        /// <summary>
        /// Build the WebSocket client suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor WebsocketSuite()
        {
            return new TestSuiteDescriptor("SdkWebsocket", "SDK: WebSocket client", new List<TestCaseDescriptor>
            {
                Case("SdkWebsocket", "Health", "Health check succeeds over WebSockets", async ct =>
                {
                    await using (PepperXWebsocketClient client = new PepperXWebsocketClient(WebsocketUrl))
                    {
                        await client.ConnectAsync(ct);
                        Assert(await client.HealthAsync(ct), "health check");
                    }
                }),

                Case("SdkWebsocket", "ObjectLifecycle", "Container and object operations over WebSockets", async ct =>
                {
                    await using (PepperXWebsocketClient client = new PepperXWebsocketClient(WebsocketUrl))
                    {
                        await client.ConnectAsync(ct);
                        string container = NewContainerName();
                        await client.CreateContainerAsync(container, null, ct);

                        try
                        {
                            byte[] payload = Encoding.UTF8.GetBytes("ws-sdk-payload");
                            ObjectWriteResponse write = await client.WriteObjectAsync(container, "k", payload,
                                new WriteObjectRequest { ContentType = "text/plain", Labels = new List<string> { "ws" } }, ct);
                            Assert(!String.IsNullOrEmpty(write.ExtentId), "extent id returned");

                            byte[]? read = await client.ReadObjectAsync(container, "k", ct);
                            Assert(read != null, "object read");
                            AssertEqual("ws-sdk-payload", Encoding.UTF8.GetString(read!), "payload round-trips");

                            ObjectMetadata? meta = await client.ReadObjectMetadataAsync(container, "k", ct);
                            AssertEqual("ws", meta!.Labels[0], "label round-trips");

                            Assert(await client.DeleteObjectAsync(container, "k", ct), "object deleted");
                            Assert(await client.ReadObjectAsync(container, "k", ct) == null, "object gone");
                        }
                        finally
                        {
                            await client.DeleteContainerAsync(container, ct);
                        }
                    }
                }),

                Case("SdkWebsocket", "Concurrency", "Concurrent operations correlate correctly", async ct =>
                {
                    await using (PepperXWebsocketClient client = new PepperXWebsocketClient(WebsocketUrl))
                    {
                        await client.ConnectAsync(ct);

                        List<Task<bool>> tasks = new List<Task<bool>>();
                        for (int i = 0; i < 16; i++) tasks.Add(client.HealthAsync(ct));
                        bool[] results = await Task.WhenAll(tasks);

                        foreach (bool result in results) Assert(result, "each concurrent health call succeeded");
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            if (!IsAvailable())
            {
                return new TestCaseDescriptor(suiteId, caseId, displayName, _ => Task.CompletedTask,
                    skip: true, skipReason: "PepperX node unavailable at " + RestUrl);
            }

            return new TestCaseDescriptor(suiteId, caseId, displayName, ct => body(ct));
        }

        private static bool IsAvailable()
        {
            if (_Available.HasValue) return _Available.Value;

            try
            {
                using (PepperXRestClient client = new PepperXRestClient(RestUrl, TimeSpan.FromSeconds(3)))
                {
                    _Available = client.HealthAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
            }
            catch (Exception)
            {
                _Available = false;
            }

            return _Available.Value;
        }

        private static string NewContainerName()
        {
            return "sdk" + Guid.NewGuid().ToString("N").Substring(0, 18);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion failed: " + message);
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception("Assertion failed: " + message + " (expected '" + expected + "', got '" + actual + "')");
        }

        #endregion
    }
}
