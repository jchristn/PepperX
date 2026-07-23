namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using StackExchange.Redis;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the RESP surface using the StackExchange.Redis client against an in-process server.
    /// </summary>
    public static class RespInteropSuite
    {
        private static RestTestServer? _Server;
        private static ConnectionMultiplexer? _Redis;

        /// <summary>
        /// Build the RESP interop suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestSuiteDescriptor("RespInterop", "RESP interop", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("RespInterop", "Unavailable", "RESP interop (database unavailable)", _ => Task.CompletedTask, skip: true, skipReason: "PostgreSQL test database unavailable")
                });
            }

            return new TestSuiteDescriptor(
                suiteId: "RespInterop",
                displayName: "RESP interop",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("RespInterop", "StringSetGet", "StringSet and StringGet round-trip", async ct =>
                    {
                        IDatabase db = _Redis!.GetDatabase(0);
                        await db.StringSetAsync("greeting", "hello-resp");
                        RedisValue value = await db.StringGetAsync("greeting");
                        Check.Equal("hello-resp", value.ToString(), "value round-trips");
                        Check.True(await db.KeyExistsAsync("greeting"), "key exists");
                    }),

                    new TestCaseDescriptor("RespInterop", "DeleteAndExists", "Delete and existence checks", async ct =>
                    {
                        IDatabase db = _Redis!.GetDatabase(0);
                        await db.StringSetAsync("todelete", "x");
                        Check.True(await db.KeyDeleteAsync("todelete"), "key deleted");
                        Check.False(await db.KeyExistsAsync("todelete"), "key gone");
                    }),

                    new TestCaseDescriptor("RespInterop", "MSetMGet", "Multi set and get", async ct =>
                    {
                        IDatabase db = _Redis!.GetDatabase(0);
                        await db.StringSetAsync(new KeyValuePair<RedisKey, RedisValue>[]
                        {
                            new KeyValuePair<RedisKey, RedisValue>("m1", "a"),
                            new KeyValuePair<RedisKey, RedisValue>("m2", "b")
                        });
                        RedisValue[] values = await db.StringGetAsync(new RedisKey[] { "m1", "m2", "missing" });
                        Check.Equal("a", values[0].ToString(), "m1");
                        Check.Equal("b", values[1].ToString(), "m2");
                        Check.True(values[2].IsNull, "missing is null");
                    }),

                    new TestCaseDescriptor("RespInterop", "IncrConcurrency", "Parallel increments are atomic", async ct =>
                    {
                        IDatabase db = _Redis!.GetDatabase(0);
                        await db.KeyDeleteAsync("counter");

                        List<Task> tasks = new List<Task>();
                        for (int i = 0; i < 8; i++)
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                IDatabase local = _Redis!.GetDatabase(0);
                                for (int j = 0; j < 100; j++) await local.StringIncrementAsync("counter");
                            }, ct));
                        }
                        await Task.WhenAll(tasks);

                        RedisValue final = await db.StringGetAsync("counter");
                        Check.Equal(800L, (long)final, "final counter equals 800");
                    }),

                    new TestCaseDescriptor("RespInterop", "CrossProtocol", "RESP-set value readable via REST", async ct =>
                    {
                        IDatabase db = _Redis!.GetDatabase(0);
                        await db.StringSetAsync("crosskey", "resp-cross");
                        System.Net.Http.HttpResponseMessage read = await _Server!.Client.GetAsync("/v1.0/containers/resp0/object?key=crosskey", ct);
                        Check.Equal("resp-cross", await read.Content.ReadAsStringAsync(ct), "RESP value read via REST");
                    })
                },
                beforeSuiteAsync: async ct =>
                {
                    _Server = await RestTestServer.StartAsync(false, true, ct).ConfigureAwait(false);
                    ConfigurationOptions options = ConfigurationOptions.Parse("localhost:" + _Server.RespPort);
                    options.AbortOnConnectFail = false;
                    options.ConnectTimeout = 10000;
                    _Redis = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
                },
                afterSuiteAsync: async ct =>
                {
                    _Redis?.Dispose();
                    _Redis = null;
                    if (_Server != null)
                    {
                        await _Server.DisposeAsync().ConfigureAwait(false);
                        _Server = null;
                    }
                });
        }
    }
}
