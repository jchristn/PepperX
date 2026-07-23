namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Enums;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Requests;
    using PepperX.Core.Services;
    using Touchstone.Core;

    /// <summary>
    /// Negative and robustness coverage: cancellation, invalid input, oversized payloads, and error shapes
    /// across the service and REST layers.
    /// </summary>
    public static class RobustnessSuite
    {
        /// <summary>
        /// Build the robustness suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Robustness",
                displayName: "Robustness",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.StackCase("Robustness", "CancellationDuringWrite", "A cancelled write does not leave an active object", async (stack, ct) =>
                    {
                        string container = DbTest.NewContainerName();
                        await stack.Containers.CreateAsync(new ContainerCreateRequest { Name = container }, ct);

                        using (CancellationTokenSource cts = new CancellationTokenSource())
                        {
                            cts.Cancel();
                            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes("payload")))
                            {
                                try
                                {
                                    await stack.Writes.WriteAsync(container, "cancelled", ms, "text/plain", null, null, null, false, cts.Token);
                                }
                                catch (OperationCanceledException)
                                {
                                    // Expected.
                                }
                            }
                        }

                        Check.False(await stack.Reads.ExistsAsync(container, "cancelled", ct), "no active object after cancellation");
                    }),

                    DbTest.StackCase("Robustness", "MissingContainerErrors", "Operations against a missing container fail cleanly", async (stack, ct) =>
                    {
                        await Check.ThrowsAsync<ContainerNotFoundException>(
                            () => stack.Deletes.DeleteAsync("no-such-container-xyz", "k", ct),
                            "delete against missing container");

                        await Check.ThrowsAsync<ContainerNotFoundException>(async () =>
                        {
                            using (MemoryStream ms = new MemoryStream())
                            {
                                await stack.Writes.WriteAsync("no-such-container-xyz", "k", ms, null, null, null, null, false, ct);
                            }
                        }, "write against missing container");

                        Check.False(await stack.Reads.ExistsAsync("no-such-container-xyz", "k", ct), "exists is false for missing container");
                    }),

                    DbTest.StackCase("Robustness", "InvalidContainerName", "Invalid container names are rejected", async (stack, ct) =>
                    {
                        await Check.ThrowsAsync<ArgumentException>(
                            () => stack.Containers.CreateAsync(new ContainerCreateRequest { Name = "Invalid_Name" }, ct),
                            "invalid container name rejected");
                    }),

                    DbTest.StackCase("Robustness", "EmptyAndLargeKeys", "Empty keys are rejected and long keys are bounded", async (stack, ct) =>
                    {
                        string container = DbTest.NewContainerName();
                        await stack.Containers.CreateAsync(new ContainerCreateRequest { Name = container }, ct);

                        await Check.ThrowsAsync<ArgumentException>(async () =>
                        {
                            using (MemoryStream ms = new MemoryStream())
                            {
                                await stack.Writes.WriteAsync(container, string.Empty, ms, null, null, null, null, false, ct);
                            }
                        }, "empty key rejected");

                        await Check.ThrowsAsync<ObjectTooLargeException>(async () =>
                        {
                            using (MemoryStream ms = new MemoryStream())
                            {
                                await stack.Writes.WriteAsync(container, new string('k', 5000), ms, null, null, null, null, false, ct);
                            }
                        }, "oversized key rejected");
                    }),

                    RestCase("BadRequestShapes", "Malformed REST requests return typed errors", async ct =>
                    {
                        HttpClient client = (await SharedServer.GetAsync(ct)).Client;

                        HttpResponseMessage missingKey = await client.GetAsync("/v1.0/containers/anything/object", ct);
                        Check.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode, "missing key query parameter is a bad request");
                        Check.True((await missingKey.Content.ReadAsStringAsync(ct)).Contains("BadRequest", StringComparison.Ordinal), "typed error body");

                        HttpResponseMessage missingContainer = await client.GetAsync("/v1.0/containers/no-such-container-xyz", ct);
                        Check.Equal(HttpStatusCode.NotFound, missingContainer.StatusCode, "missing container is not found");

                        HttpResponseMessage badRoute = await client.GetAsync("/v1.0/not-a-route", ct);
                        Check.Equal(HttpStatusCode.NotFound, badRoute.StatusCode, "unknown route is not found");
                    }),

                    RestCase("ConflictShapes", "Conflicting operations return 409", async ct =>
                    {
                        HttpClient client = (await SharedServer.GetAsync(ct)).Client;
                        string container = DbTest.NewContainerName();
                        await client.PutAsync("/v1.0/containers", Json("{\"Name\":\"" + container + "\"}"), ct);

                        HttpResponseMessage duplicate = await client.PutAsync("/v1.0/containers", Json("{\"Name\":\"" + container + "\"}"), ct);
                        Check.Equal(HttpStatusCode.Conflict, duplicate.StatusCode, "duplicate container is a conflict");

                        await client.PutAsync("/v1.0/containers/" + container + "/object?key=k", new StringContent("v"), ct);
                        HttpResponseMessage noOverwrite = await client.PutAsync("/v1.0/containers/" + container + "/object?key=k&nooverwrite=true", new StringContent("v2"), ct);
                        Check.Equal(HttpStatusCode.Conflict, noOverwrite.StatusCode, "no-overwrite write is a conflict");

                        HttpResponseMessage notEmpty = await client.DeleteAsync("/v1.0/containers/" + container, ct);
                        Check.Equal(HttpStatusCode.Conflict, notEmpty.StatusCode, "non-empty container delete is a conflict");

                        HttpResponseMessage forced = await client.DeleteAsync("/v1.0/containers/" + container + "?force=true", ct);
                        Check.Equal(HttpStatusCode.NoContent, forced.StatusCode, "forced delete succeeds");
                    })
                });
        }

        private static TestCaseDescriptor RestCase(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestCaseDescriptor("Robustness", caseId, displayName, _ => Task.CompletedTask,
                    skip: true, skipReason: "PostgreSQL test database unavailable");
            }

            return new TestCaseDescriptor("Robustness", caseId, displayName, ct => body(ct));
        }

        private static StringContent Json(string json)
        {
            return new StringContent(json, Encoding.UTF8, "application/json");
        }
    }
}
