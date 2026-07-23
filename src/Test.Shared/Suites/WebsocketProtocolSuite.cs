namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Serialization;
    using PepperX.Server.Api.Websockets;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the WebSocket envelope protocol using a raw <see cref="ClientWebSocket"/>.
    /// </summary>
    public static class WebsocketProtocolSuite
    {
        private static RestTestServer? _Server;
        private static readonly PepperXSerializer _Serializer = new PepperXSerializer();

        /// <summary>
        /// Build the WebSocket protocol suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestSuiteDescriptor("WebsocketProtocol", "WebSocket protocol", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("WebsocketProtocol", "Unavailable", "WebSocket protocol (database unavailable)", _ => Task.CompletedTask, skip: true, skipReason: "PostgreSQL test database unavailable")
                });
            }

            return new TestSuiteDescriptor(
                suiteId: "WebsocketProtocol",
                displayName: "WebSocket protocol",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("WebsocketProtocol", "Health", "Health operation responds", async ct =>
                    {
                        using (ClientWebSocket ws = await ConnectAsync(ct))
                        {
                            WsResponseEnvelope response = await SendReceiveAsync(ws, new { RequestId = "h1", Operation = "Health" }, ct);
                            Check.True(response.Success, "health success");
                            Check.Equal("h1", response.RequestId!, "correlated");
                        }
                    }),

                    new TestCaseDescriptor("WebsocketProtocol", "ObjectLifecycle", "Container and object operations over WS", async ct =>
                    {
                        using (ClientWebSocket ws = await ConnectAsync(ct))
                        {
                            string container = DbTest.NewContainerName();
                            WsResponseEnvelope create = await SendReceiveAsync(ws, new { RequestId = "c", Operation = "ContainerCreate", Body = new { Name = container } }, ct);
                            Check.Equal(201, create.StatusCode, "container created");

                            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("ws-payload"));
                            WsResponseEnvelope write = await SendReceiveAsync(ws, new { RequestId = "w", Operation = "ObjectWrite", Container = container, Key = "a/b.bin", Body = new { ContentType = "text/plain", DataBase64 = payload } }, ct);
                            Check.Equal(201, write.StatusCode, "object written");

                            WsResponseEnvelope read = await SendReceiveAsync(ws, new { RequestId = "r", Operation = "ObjectRead", Container = container, Key = "a/b.bin" }, ct);
                            Check.NotNull(read.DataBase64, "payload present");
                            Check.Equal("ws-payload", Encoding.UTF8.GetString(Convert.FromBase64String(read.DataBase64!)), "payload round-trips");

                            WsResponseEnvelope del = await SendReceiveAsync(ws, new { RequestId = "d", Operation = "ObjectDelete", Container = container, Key = "a/b.bin" }, ct);
                            Check.Equal(204, del.StatusCode, "object deleted");
                        }
                    }),

                    new TestCaseDescriptor("WebsocketProtocol", "Concurrency", "Interleaved requests correlate by id", async ct =>
                    {
                        using (ClientWebSocket ws = await ConnectAsync(ct))
                        {
                            for (int i = 0; i < 8; i++)
                            {
                                await SendAsync(ws, new { RequestId = "req" + i, Operation = "Health" }, ct);
                            }

                            HashSet<string> ids = new HashSet<string>();
                            for (int i = 0; i < 8; i++)
                            {
                                WsResponseEnvelope response = await ReceiveAsync(ws, ct);
                                if (response.RequestId != null) ids.Add(response.RequestId);
                            }
                            Check.Equal(8, ids.Count, "all responses correlated");
                        }
                    }),

                    new TestCaseDescriptor("WebsocketProtocol", "MalformedRequest", "A malformed request returns an error envelope", async ct =>
                    {
                        using (ClientWebSocket ws = await ConnectAsync(ct))
                        {
                            byte[] junk = Encoding.UTF8.GetBytes("not json");
                            await ws.SendAsync(junk, WebSocketMessageType.Text, true, ct);
                            WsResponseEnvelope response = await ReceiveAsync(ws, ct);
                            Check.False(response.Success, "malformed rejected");
                            Check.Equal(400, response.StatusCode, "bad request");
                        }
                    }),

                    new TestCaseDescriptor("WebsocketProtocol", "CrossProtocol", "WS-written object readable via REST", async ct =>
                    {
                        string container = DbTest.NewContainerName();
                        using (ClientWebSocket ws = await ConnectAsync(ct))
                        {
                            await SendReceiveAsync(ws, new { RequestId = "c", Operation = "ContainerCreate", Body = new { Name = container } }, ct);
                            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("ws-cross"));
                            await SendReceiveAsync(ws, new { RequestId = "w", Operation = "ObjectWrite", Container = container, Key = "shared", Body = new { DataBase64 = payload } }, ct);
                        }

                        System.Net.Http.HttpResponseMessage read = await _Server!.Client.GetAsync("/v1.0/containers/" + container + "/object?key=shared", ct);
                        Check.Equal("ws-cross", await read.Content.ReadAsStringAsync(ct), "WS value read via REST");
                    })
                },
                beforeSuiteAsync: async ct =>
                {
                    _Server = await RestTestServer.StartAsync(false, false, true, ct).ConfigureAwait(false);
                },
                afterSuiteAsync: async ct =>
                {
                    if (_Server != null)
                    {
                        await _Server.DisposeAsync().ConfigureAwait(false);
                        _Server = null;
                    }
                });
        }

        private static async Task<ClientWebSocket> ConnectAsync(CancellationToken ct)
        {
            ClientWebSocket ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri("ws://localhost:" + _Server!.WsPort + "/"), ct).ConfigureAwait(false);
            return ws;
        }

        private static async Task SendAsync(ClientWebSocket ws, object request, CancellationToken ct)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(_Serializer.SerializeJson(request, false) ?? "{}");
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        private static async Task<WsResponseEnvelope> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
        {
            byte[] buffer = new byte[65536];
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                return _Serializer.DeserializeJson<WsResponseEnvelope>(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }

        private static async Task<WsResponseEnvelope> SendReceiveAsync(ClientWebSocket ws, object request, CancellationToken ct)
        {
            await SendAsync(ws, request, ct).ConfigureAwait(false);
            return await ReceiveAsync(ws, ct).ConfigureAwait(false);
        }
    }
}
