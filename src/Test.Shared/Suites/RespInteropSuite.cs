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
        private static readonly SemaphoreSlim _RedisGate = new SemaphoreSlim(1, 1);
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
                        IDatabase db = (await RedisAsync(ct)).GetDatabase(0);
                        string key = NewKey("greeting");
                        await db.StringSetAsync(key, "hello-resp");
                        RedisValue value = await db.StringGetAsync(key);
                        Check.Equal("hello-resp", value.ToString(), "value round-trips");
                        Check.True(await db.KeyExistsAsync(key), "key exists");
                    }),

                    new TestCaseDescriptor("RespInterop", "DeleteAndExists", "Delete and existence checks", async ct =>
                    {
                        IDatabase db = (await RedisAsync(ct)).GetDatabase(0);
                        string key = NewKey("todelete");
                        await db.StringSetAsync(key, "x");
                        Check.True(await db.KeyDeleteAsync(key), "key deleted");
                        Check.False(await db.KeyExistsAsync(key), "key gone");
                    }),

                    new TestCaseDescriptor("RespInterop", "MSetMGet", "Multi set and get", async ct =>
                    {
                        IDatabase db = (await RedisAsync(ct)).GetDatabase(0);
                        string k1 = NewKey("m1");
                        string k2 = NewKey("m2");
                        await db.StringSetAsync(new KeyValuePair<RedisKey, RedisValue>[]
                        {
                            new KeyValuePair<RedisKey, RedisValue>(k1, "a"),
                            new KeyValuePair<RedisKey, RedisValue>(k2, "b")
                        });
                        RedisValue[] values = await db.StringGetAsync(new RedisKey[] { k1, k2, NewKey("missing") });
                        Check.Equal("a", values[0].ToString(), "m1");
                        Check.Equal("b", values[1].ToString(), "m2");
                        Check.True(values[2].IsNull, "missing is null");
                    }),

                    new TestCaseDescriptor("RespInterop", "IncrConcurrency", "Parallel increments are atomic", async ct =>
                    {
                        IDatabase db = (await RedisAsync(ct)).GetDatabase(0);
                        string counter = NewKey("counter");
                        await db.KeyDeleteAsync(counter);

                        List<Task> tasks = new List<Task>();
                        for (int i = 0; i < 8; i++)
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                IDatabase local = (await RedisAsync(ct)).GetDatabase(0);
                                for (int j = 0; j < 100; j++) await local.StringIncrementAsync(counter);
                            }, ct));
                        }
                        await Task.WhenAll(tasks);

                        RedisValue final = await db.StringGetAsync(counter);
                        Check.Equal(800L, (long)final, "final counter equals 800");
                    }),

                    new TestCaseDescriptor("RespInterop", "CrossProtocol", "RESP-set value readable via REST", async ct =>
                    {
                        IDatabase db = (await RedisAsync(ct)).GetDatabase(0);
                        string key = NewKey("crosskey");
                        await db.StringSetAsync(key, "resp-cross");
                        System.Net.Http.HttpResponseMessage read = await (await SharedServer.GetAsync(ct)).Client.GetAsync("/v1.0/containers/resp0/object?key=" + key, ct);
                        Check.Equal("resp-cross", await read.Content.ReadAsStringAsync(ct), "RESP value read via REST");
                    })
                });
        }
    
        private static string NewKey(string prefix)
        {
            return prefix + "-" + Guid.NewGuid().ToString("N");
        }

        private static async Task<ConnectionMultiplexer> RedisAsync(CancellationToken ct)
        {
            if (_Redis != null) return _Redis;

            await _RedisGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_Redis == null)
                {
                    RestTestServer server = await SharedServer.GetAsync(ct).ConfigureAwait(false);
                    ConfigurationOptions options = ConfigurationOptions.Parse("localhost:" + server.RespPort);
                    options.AbortOnConnectFail = false;
                    options.ConnectTimeout = 15000;
                    _Redis = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
                }
            }
            finally
            {
                _RedisGate.Release();
            }

            return _Redis;
        }
    }
}
