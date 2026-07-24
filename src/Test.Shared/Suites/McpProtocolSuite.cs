namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the MCP surface over Streamable HTTP: lifecycle, tool discovery, and tool invocation.
    /// </summary>
    public static class McpProtocolSuite
    {
        private static readonly SemaphoreSlim _McpGate = new SemaphoreSlim(1, 1);
        private static HttpClient? _Http;
        private static string? _SessionId;

        /// <summary>
        /// Build the MCP protocol suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestSuiteDescriptor("McpProtocol", "MCP protocol", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("McpProtocol", "Unavailable", "MCP protocol (database unavailable)", _ => Task.CompletedTask, skip: true, skipReason: "PostgreSQL test database unavailable")
                });
            }

            return new TestSuiteDescriptor(
                suiteId: "McpProtocol",
                displayName: "MCP protocol",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("McpProtocol", "ToolsList", "tools/list advertises every PepperX tool", async ct =>
                    {
                        string body = await RpcAsync("tools/list", "{}", ct);
                        foreach (string expected in new[]
                        {
                            "pepperx_container_create", "pepperx_container_read", "pepperx_container_list",
                            "pepperx_container_enumerate", "pepperx_container_update_tags", "pepperx_container_delete",
                            "pepperx_object_write", "pepperx_object_read", "pepperx_object_read_metadata",
                            "pepperx_object_update_metadata", "pepperx_object_delete", "pepperx_object_exists",
                            "pepperx_object_enumerate", "pepperx_search", "pepperx_stats", "pepperx_nodes"
                        })
                        {
                            Check.True(body.Contains(expected, StringComparison.Ordinal), "tool advertised: " + expected);
                        }
                    }),

                    new TestCaseDescriptor("McpProtocol", "ToolCallLifecycle", "Create a container and write/read an object via tools", async ct =>
                    {
                        string container = DbTest.NewContainerName();

                        string create = await CallToolAsync("pepperx_container_create", "{\"name\":\"" + container + "\"}", ct);
                        Check.True(create.Contains(container, StringComparison.Ordinal), "container created via MCP");

                        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("mcp-payload"));
                        string write = await CallToolAsync("pepperx_object_write", "{\"container\":\"" + container + "\",\"key\":\"k1\",\"dataBase64\":\"" + payload + "\"}", ct);
                        Check.True(write.Contains("ExtentId", StringComparison.Ordinal), "object written via MCP");

                        string read = await CallToolAsync("pepperx_object_read", "{\"container\":\"" + container + "\",\"key\":\"k1\"}", ct);
                        Check.True(read.Contains(payload, StringComparison.Ordinal), "payload returned via MCP");
                    }),

                    new TestCaseDescriptor("McpProtocol", "ToolCallError", "A missing object returns a tool error", async ct =>
                    {
                        string container = DbTest.NewContainerName();
                        await CallToolAsync("pepperx_container_create", "{\"name\":\"" + container + "\"}", ct);
                        string read = await CallToolAsync("pepperx_object_read", "{\"container\":\"" + container + "\",\"key\":\"missing\"}", ct);
                        Check.True(read.Contains("not found", StringComparison.OrdinalIgnoreCase), "error surfaced for missing object");
                    }),

                    new TestCaseDescriptor("McpProtocol", "ToolSchemas", "Tools publish argument schemas", async ct =>
                    {
                        // A tool whose schema is an open object tells a client nothing about what to
                        // pass. These assertions are what keep the surface usable by an agent that has
                        // only tools/list to go on.
                        string body = await RpcAsync("tools/list", "{}", ct);

                        Check.True(body.Contains("dataBase64", StringComparison.Ordinal), "write payload argument described");
                        Check.True(body.Contains("labelsFilter", StringComparison.Ordinal), "search filter argument described");
                        Check.True(body.Contains("Container name.", StringComparison.Ordinal), "arguments carry descriptions");
                        Check.True(body.Contains("required", StringComparison.Ordinal), "required arguments declared");
                    }),

                    new TestCaseDescriptor("McpProtocol", "ArgumentCasing", "camelCase arguments bind and off-schema names are refused", async ct =>
                    {
                        // MCP clients send the camelCase names the schema declares, while the argument
                        // type is PascalCase like the rest of the codebase. Case-sensitive binding left
                        // every property null, so a call arrived with no container and no key and then
                        // failed deep inside a service -- and this suite missed it entirely because it
                        // spoke the server's internal casing rather than the protocol's.
                        string container = DbTest.NewContainerName();
                        await CallToolAsync("pepperx_container_create", "{\"name\":\"" + container + "\"}", ct);

                        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("casing"));
                        await CallToolAsync("pepperx_object_write", "{\"container\":\"" + container + "\",\"key\":\"camel\",\"dataBase64\":\"" + payload + "\"}", ct);

                        string camel = await CallToolAsync("pepperx_object_read_metadata", "{\"container\":\"" + container + "\",\"key\":\"camel\"}", ct);
                        Check.True(camel.Contains("camel", StringComparison.Ordinal), "camelCase arguments bind");

                        // Names outside the published schema are rejected by schema validation before
                        // reaching the handler. That is the desired outcome: the client gets a precise
                        // "missing required property" instead of an obscure failure from inside a
                        // service that received nulls.
                        string offSchema = await CallToolAsync("pepperx_object_exists", "{\"Container\":\"" + container + "\",\"Key\":\"camel\"}", ct);
                        Check.True(offSchema.Contains("container", StringComparison.OrdinalIgnoreCase), "off-schema argument names named in the error");
                        Check.True(
                            offSchema.Contains("required", StringComparison.OrdinalIgnoreCase) || offSchema.Contains("error", StringComparison.OrdinalIgnoreCase),
                            "off-schema arguments rejected rather than silently ignored");
                    }),

                    new TestCaseDescriptor("McpProtocol", "CrossProtocol", "MCP-written object readable via REST", async ct =>
                    {
                        string container = DbTest.NewContainerName();
                        await CallToolAsync("pepperx_container_create", "{\"name\":\"" + container + "\"}", ct);
                        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("mcp-cross"));
                        await CallToolAsync("pepperx_object_write", "{\"container\":\"" + container + "\",\"key\":\"shared\",\"dataBase64\":\"" + payload + "\"}", ct);

                        HttpResponseMessage read = await (await SharedServer.GetAsync(ct)).Client.GetAsync("/v1.0/containers/" + container + "/object?key=shared", ct);
                        Check.Equal("mcp-cross", await read.Content.ReadAsStringAsync(ct), "MCP value read via REST");
                    })
                });
        }

        /// <summary>
        /// Write an object through the MCP tool surface (used by the protocol parity suite).
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="payload">Payload text.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The raw tool-call response.</returns>
        public static Task<string> WriteObjectAsync(string container, string key, string payload, CancellationToken ct)
        {
            string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            return CallToolAsync("pepperx_object_write", "{\"container\":\"" + container + "\",\"key\":\"" + key + "\",\"contentType\":\"text/plain\",\"dataBase64\":\"" + base64 + "\"}", ct);
        }

        /// <summary>
        /// Read an object through the MCP tool surface (used by the protocol parity suite).
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The raw tool-call response.</returns>
        public static Task<string> ReadObjectAsync(string container, string key, CancellationToken ct)
        {
            return CallToolAsync("pepperx_object_read", "{\"container\":\"" + container + "\",\"key\":\"" + key + "\"}", ct);
        }

        private static async Task<HttpClient> HttpAsync(CancellationToken ct)
        {
            if (_Http != null) return _Http;

            await _McpGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_Http == null)
                {
                    RestTestServer server = await SharedServer.GetAsync(ct).ConfigureAwait(false);
                    HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + server.McpHttpPort), Timeout = TimeSpan.FromSeconds(30) };
                    _Http = client;
                    await InitializeAsync(ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _McpGate.Release();
            }

            return _Http;
        }

        private static async Task InitializeAsync(CancellationToken ct)
        {
            string init = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"touchstone\",\"version\":\"1\"}}}";
            using (HttpRequestMessage request = NewRequest(init))
            using (HttpResponseMessage response = await _Http!.SendAsync(request, ct).ConfigureAwait(false))
            {
                if (response.Headers.TryGetValues("MCP-Session-Id", out IEnumerable<string>? values)) _SessionId = values.FirstOrDefault();
            }

            string initialized = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";
            using (HttpRequestMessage request = NewRequest(initialized))
            using (await _Http!.SendAsync(request, ct).ConfigureAwait(false)) { }
        }

        private static async Task<string> RpcAsync(string method, string paramsJson, CancellationToken ct)
        {
            HttpClient http = await HttpAsync(ct).ConfigureAwait(false);
            string payload = "{\"jsonrpc\":\"2.0\",\"id\":" + Random.Shared.Next(2, 100000) + ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}";
            using (HttpRequestMessage request = NewRequest(payload))
            using (HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false))
            {
                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
        }

        private static Task<string> CallToolAsync(string tool, string argumentsJson, CancellationToken ct)
        {
            return RpcAsync("tools/call", "{\"name\":\"" + tool + "\",\"arguments\":" + argumentsJson + "}", ct);
        }

        private static HttpRequestMessage NewRequest(string json)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            if (!string.IsNullOrEmpty(_SessionId))
            {
                request.Headers.TryAddWithoutValidation("MCP-Session-Id", _SessionId);
                request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-11-25");
            }
            return request;
        }
    }
}
