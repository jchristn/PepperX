namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Boots an in-process server and verifies the native REST surface end to end.
    /// </summary>
    public static class RestApiSuite
    {

        /// <summary>
        /// Build the REST API suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestSuiteDescriptor("RestApi", "REST API", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("RestApi", "Unavailable", "REST API (database unavailable)", _ => Task.CompletedTask, skip: true, skipReason: "PostgreSQL test database unavailable")
                });
            }

            return new TestSuiteDescriptor(
                suiteId: "RestApi",
                displayName: "REST API",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("RestApi", "Health", "Health endpoints respond", async ct =>
                    {
                        HttpResponseMessage root = await (await ClientAsync(ct)).GetAsync("/", ct);
                        Check.True(root.IsSuccessStatusCode, "GET / ok");
                        string body = await root.Content.ReadAsStringAsync(ct);
                        Check.True(body.Contains("PepperX", StringComparison.Ordinal), "product name present");
                    }),

                    new TestCaseDescriptor("RestApi", "ContainerCrud", "Container create, read, list, delete", async ct =>
                    {
                        string name = DbTest.NewContainerName();
                        HttpResponseMessage create = await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);
                        Check.Equal(HttpStatusCode.Created, create.StatusCode, "container created");

                        HttpResponseMessage read = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + name, ct);
                        Check.True(read.IsSuccessStatusCode, "container read");

                        HttpResponseMessage list = await (await ClientAsync(ct)).GetAsync("/v1.0/containers", ct);
                        Check.True((await list.Content.ReadAsStringAsync(ct)).Contains(name, StringComparison.Ordinal), "container listed");

                        HttpResponseMessage delete = await (await ClientAsync(ct)).DeleteAsync("/v1.0/containers/" + name, ct);
                        Check.Equal(HttpStatusCode.NoContent, delete.StatusCode, "container deleted");
                    }),

                    new TestCaseDescriptor("RestApi", "ObjectLifecycle", "Object write, read (slashed key), metadata, delete", async ct =>
                    {
                        string name = DbTest.NewContainerName();
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);

                        string key = Uri.EscapeDataString("a/b/c.bin");
                        HttpResponseMessage write = await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + name + "/object?key=" + key,
                            new StringContent("payload-data", Encoding.UTF8, "text/plain"), ct);
                        Check.Equal(HttpStatusCode.Created, write.StatusCode, "object written");

                        HttpResponseMessage read = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + name + "/object?key=" + key, ct);
                        Check.Equal("payload-data", await read.Content.ReadAsStringAsync(ct), "payload round-trips");

                        HttpResponseMessage meta = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + name + "/object/metadata?key=" + key, ct);
                        Check.True((await meta.Content.ReadAsStringAsync(ct)).Contains("a/b/c.bin", StringComparison.Ordinal), "metadata key present");

                        HttpResponseMessage delete = await (await ClientAsync(ct)).DeleteAsync("/v1.0/containers/" + name + "/object?key=" + key, ct);
                        Check.Equal(HttpStatusCode.NoContent, delete.StatusCode, "object deleted");

                        HttpResponseMessage gone = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + name + "/object?key=" + key, ct);
                        Check.Equal(HttpStatusCode.NotFound, gone.StatusCode, "object gone");
                    }),

                    new TestCaseDescriptor("RestApi", "Enumerate", "Object enumeration filters by label", async ct =>
                    {
                        string name = DbTest.NewContainerName();
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + name + "/object?key=red", new StringContent("x"), ct);
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + name + "/object?key=blue&", new StringContent("y"), ct);
                        await (await ClientAsync(ct)).PostAsync("/v1.0/containers/" + name + "/object?key=tagged",
                            Json("{\"ContentType\":\"text/plain\",\"Labels\":[\"special\"],\"DataBase64\":\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes("z")) + "\"}"), ct);

                        HttpResponseMessage search = await (await ClientAsync(ct)).PostAsync("/v1.0/containers/" + name + "/objects/enumerate",
                            Json("{\"Labels\":[\"special\"]}"), ct);
                        string body = await search.Content.ReadAsStringAsync(ct);
                        Check.True(body.Contains("\"TotalRecords\":1", StringComparison.Ordinal), "label filter returns one");
                        Check.True(body.Contains("tagged", StringComparison.Ordinal), "correct object");
                    }),

                    new TestCaseDescriptor("RestApi", "RequestHistory", "Requests are captured", async ct =>
                    {
                        await (await ClientAsync(ct)).GetAsync("/v1.0/api/health", ct);
                        await Task.Delay(500, ct);
                        HttpResponseMessage list = await (await ClientAsync(ct)).GetAsync("/v1.0/api/request-history?pathContains=/v1.0/api/health", ct);
                        string body = await list.Content.ReadAsStringAsync(ct);
                        Check.True(body.Contains("\"TotalCount\":", StringComparison.Ordinal), "history page shape");
                    }),

                    new TestCaseDescriptor("RestApi", "OpenApi", "OpenAPI document is served", async ct =>
                    {
                        HttpResponseMessage spec = await (await ClientAsync(ct)).GetAsync("/openapi.json", ct);
                        Check.True(spec.IsSuccessStatusCode, "openapi served");
                        string body = await spec.Content.ReadAsStringAsync(ct);
                        Check.True(body.Contains("/v1.0/containers", StringComparison.Ordinal), "paths present");
                        Check.True(body.Contains("PepperX REST API", StringComparison.Ordinal), "title present");
                    })
                });
        }

        private static async Task<HttpClient> ClientAsync(CancellationToken ct)
        {
            RestTestServer server = await SharedServer.GetAsync(ct).ConfigureAwait(false);
            return server.Client;
        }

        private static StringContent Json(string json)
        {
            return new StringContent(json, Encoding.UTF8, "application/json");
        }
    }
}
