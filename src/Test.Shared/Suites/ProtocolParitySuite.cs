namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;
    using PepperX.Core.Serialization;
    using PepperX.Server.Api.Websockets;
    using StackExchange.Redis;
    using Touchstone.Core;

    /// <summary>
    /// Writes one canonical object through each protocol surface and reads it back through the others,
    /// proving that every protocol shares one semantic core.
    /// </summary>
    public static class ProtocolParitySuite
    {
        private static readonly PepperXSerializer _Serializer = new PepperXSerializer();
        private const string _Payload = "parity-payload";

        /// <summary>
        /// Build the protocol parity suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestSuiteDescriptor("ProtocolParity", "Protocol parity", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ProtocolParity", "Unavailable", "Protocol parity (database unavailable)", _ => Task.CompletedTask, skip: true, skipReason: "PostgreSQL test database unavailable")
                });
            }

            return new TestSuiteDescriptor(
                suiteId: "ProtocolParity",
                displayName: "Protocol parity",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("ProtocolParity", "RestWritten", "An object written via REST reads identically on every protocol", async ct =>
                    {
                        string container = await NewContainerAsync(ct);
                        string key = "parity-rest";
                        HttpClient rest = (await SharedServer.GetAsync(ct)).Client;
                        await rest.PutAsync("/v1.0/containers/" + container + "/object?key=" + key, new StringContent(_Payload, Encoding.UTF8, "text/plain"), ct);
                        await AssertReadableEverywhereAsync(container, key, ct);
                    }),

                    new TestCaseDescriptor("ProtocolParity", "S3Written", "An object written via S3 reads identically on every protocol", async ct =>
                    {
                        string container = await NewContainerAsync(ct);
                        string key = "parity-s3";
                        using (AmazonS3Client s3 = await NewS3Async(ct))
                        {
                            await s3.PutObjectAsync(new PutObjectRequest { BucketName = container, Key = key, ContentBody = _Payload, ContentType = "text/plain" }, ct);
                        }
                        await AssertReadableEverywhereAsync(container, key, ct);
                    }),

                    new TestCaseDescriptor("ProtocolParity", "WebsocketWritten", "An object written via WebSockets reads identically on every protocol", async ct =>
                    {
                        string container = await NewContainerAsync(ct);
                        string key = "parity-ws";
                        RestTestServer server = await SharedServer.GetAsync(ct);
                        using (ClientWebSocket ws = new ClientWebSocket())
                        {
                            await ws.ConnectAsync(new Uri("ws://localhost:" + server.WsPort + "/"), ct);
                            string body = _Serializer.SerializeJson(new
                            {
                                RequestId = "p",
                                Operation = "ObjectWrite",
                                Container = container,
                                Key = key,
                                Body = new { ContentType = "text/plain", DataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(_Payload)) }
                            }, false)!;
                            await ws.SendAsync(Encoding.UTF8.GetBytes(body), WebSocketMessageType.Text, true, ct);

                            byte[] buffer = new byte[16384];
                            await ws.ReceiveAsync(buffer, ct);
                        }
                        await AssertReadableEverywhereAsync(container, key, ct);
                    }),

                    new TestCaseDescriptor("ProtocolParity", "McpWritten", "An object written via MCP reads identically on every protocol", async ct =>
                    {
                        string container = await NewContainerAsync(ct);
                        string key = "parity-mcp";
                        await McpProtocolSuite.WriteObjectAsync(container, key, _Payload, ct);
                        await AssertReadableEverywhereAsync(container, key, ct);
                    }),

                    new TestCaseDescriptor("ProtocolParity", "RespWritten", "A value written via RESP reads identically on REST and S3", async ct =>
                    {
                        // RESP maps database indices to fixed containers, so this case verifies the RESP
                        // container rather than an arbitrary one.
                        RestTestServer server = await SharedServer.GetAsync(ct);
                        string key = "parity-resp-" + Guid.NewGuid().ToString("N");

                        ConfigurationOptions options = ConfigurationOptions.Parse("localhost:" + server.RespPort);
                        options.AbortOnConnectFail = false;
                        options.ConnectTimeout = 15000;
                        using (ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(options))
                        {
                            await redis.GetDatabase(0).StringSetAsync(key, _Payload);
                        }

                        HttpResponseMessage rest = await server.Client.GetAsync("/v1.0/containers/resp0/object?key=" + key, ct);
                        Check.Equal(_Payload, await rest.Content.ReadAsStringAsync(ct), "RESP value via REST");

                        using (AmazonS3Client s3 = await NewS3Async(ct))
                        using (GetObjectResponse get = await s3.GetObjectAsync(new GetObjectRequest { BucketName = "resp0", Key = key }, ct))
                        using (StreamReader reader = new StreamReader(get.ResponseStream))
                        {
                            Check.Equal(_Payload, await reader.ReadToEndAsync(ct), "RESP value via S3");
                        }
                    })
                });
        }

        private static async Task AssertReadableEverywhereAsync(string container, string key, CancellationToken ct)
        {
            RestTestServer server = await SharedServer.GetAsync(ct);

            HttpResponseMessage rest = await server.Client.GetAsync("/v1.0/containers/" + container + "/object?key=" + key, ct);
            Check.Equal(_Payload, await rest.Content.ReadAsStringAsync(ct), "readable via REST");

            using (AmazonS3Client s3 = await NewS3Async(ct))
            using (GetObjectResponse get = await s3.GetObjectAsync(new GetObjectRequest { BucketName = container, Key = key }, ct))
            using (StreamReader reader = new StreamReader(get.ResponseStream))
            {
                Check.Equal(_Payload, await reader.ReadToEndAsync(ct), "readable via S3");
            }

            using (ClientWebSocket ws = new ClientWebSocket())
            {
                await ws.ConnectAsync(new Uri("ws://localhost:" + server.WsPort + "/"), ct);
                string body = _Serializer.SerializeJson(new { RequestId = "r", Operation = "ObjectRead", Container = container, Key = key }, false)!;
                await ws.SendAsync(Encoding.UTF8.GetBytes(body), WebSocketMessageType.Text, true, ct);

                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buffer = new byte[65536];
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(buffer, ct);
                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    WsResponseEnvelope envelope = _Serializer.DeserializeJson<WsResponseEnvelope>(Encoding.UTF8.GetString(ms.ToArray()));
                    Check.NotNull(envelope.DataBase64, "WebSocket payload present");
                    Check.Equal(_Payload, Encoding.UTF8.GetString(Convert.FromBase64String(envelope.DataBase64!)), "readable via WebSockets");
                }
            }

            string mcp = await McpProtocolSuite.ReadObjectAsync(container, key, ct);
            Check.True(mcp.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(_Payload)), StringComparison.Ordinal), "readable via MCP");
        }

        private static async Task<string> NewContainerAsync(CancellationToken ct)
        {
            string name = DbTest.NewContainerName();
            RestTestServer server = await SharedServer.GetAsync(ct);
            await server.Client.PutAsync("/v1.0/containers", new StringContent("{\"Name\":\"" + name + "\"}", Encoding.UTF8, "application/json"), ct);
            return name;
        }

        private static async Task<AmazonS3Client> NewS3Async(CancellationToken ct)
        {
            RestTestServer server = await SharedServer.GetAsync(ct);
            AmazonS3Config config = new AmazonS3Config
            {
                ServiceURL = server.S3ServiceUrl,
                ForcePathStyle = true,
                UseHttp = true,
                AuthenticationRegion = "us-west-1",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            };
            return new AmazonS3Client(new BasicAWSCredentials("pepperx", "pepperx"), config);
        }
    }
}
