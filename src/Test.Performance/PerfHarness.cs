namespace Test.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;
    using PepperX.Core.Serialization;
    using StackExchange.Redis;

    /// <summary>
    /// Executes the PepperX workload gauntlet against a running server.
    /// </summary>
    public sealed class PerfHarness : IAsyncDisposable
    {
        #region Private-Members

        private readonly PerfOptions _Options;
        private readonly ConsoleReporter _Reporter;
        private readonly WorkloadEngine _Engine;
        private readonly HttpClient _Rest;
        private readonly string _BaseUrl;
        private readonly string? _S3Url;
        private readonly int _RespPort;
        private readonly byte[] _Payload;

        private AmazonS3Client? _S3;
        private ConnectionMultiplexer? _Redis;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the harness.
        /// </summary>
        /// <param name="options">Harness options.</param>
        /// <param name="reporter">Console reporter.</param>
        /// <param name="baseUrl">REST base URL.</param>
        /// <param name="s3Url">S3 service URL, or null when S3 is unavailable.</param>
        /// <param name="respPort">RESP port, or zero when RESP is unavailable.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public PerfHarness(PerfOptions options, ConsoleReporter reporter, string baseUrl, string? s3Url, int respPort)
        {
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            _Reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
            _BaseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
            _S3Url = s3Url;
            _RespPort = respPort;
            _Engine = new WorkloadEngine(options, reporter);

            _Rest = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(5) };
            _Payload = new byte[options.ObjectSize];
            for (int i = 0; i < _Payload.Length; i++) _Payload[i] = (byte)(i % 251);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the configured workloads.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All workload results.</returns>
        public async Task<List<WorkloadResult>> RunAsync(CancellationToken token = default)
        {
            List<WorkloadResult> results = new List<WorkloadResult>();
            HashSet<string> selected = new HashSet<string>(_Options.Workloads, StringComparer.OrdinalIgnoreCase);
            bool all = selected.Count == 0;

            if (all || selected.Contains("write-small")) results.Add(await WriteWorkloadAsync("write-small", token).ConfigureAwait(false));
            if (all || selected.Contains("read-heavy")) results.Add(await ReadWorkloadAsync("read-heavy", token).ConfigureAwait(false));
            if (all || selected.Contains("mixed")) results.Add(await MixedWorkloadAsync("mixed", token).ConfigureAwait(false));
            if (all || selected.Contains("search")) results.Add(await SearchWorkloadAsync("search", token).ConfigureAwait(false));
            if (all || selected.Contains("replace-churn")) results.Add(await ReplaceChurnWorkloadAsync("replace-churn", token).ConfigureAwait(false));
            if (all || selected.Contains("delete-churn")) results.Add(await DeleteChurnWorkloadAsync("delete-churn", token).ConfigureAwait(false));
            if ((all || selected.Contains("s3-ops")) && !String.IsNullOrEmpty(_S3Url)) results.Add(await S3WorkloadAsync("s3-ops", token).ConfigureAwait(false));
            if ((all || selected.Contains("resp-ops")) && _RespPort > 0) results.Add(await RespWorkloadAsync("resp-ops", token).ConfigureAwait(false));

            return results;
        }

        /// <summary>
        /// Dispose harness clients.
        /// </summary>
        /// <returns>Value task.</returns>
        public ValueTask DisposeAsync()
        {
            _Rest.Dispose();
            _S3?.Dispose();
            _Redis?.Dispose();
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Private-Methods-Workloads

        private async Task<WorkloadResult> WriteWorkloadAsync(string name, CancellationToken token)
        {
            string container = await NewContainerAsync(token).ConfigureAwait(false);
            WorkloadResult result = await _Engine.RunAsync(name, "REST", async (worker, iteration, ct) =>
            {
                string key = "w" + worker + "-" + iteration;
                using (ByteArrayContent content = new ByteArrayContent(_Payload))
                using (HttpResponseMessage response = await _Rest.PutAsync(Url(container, key), content, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return _Payload.Length;
                }
            }, token).ConfigureAwait(false);
            _Reporter.WorkloadComplete(result);
            return result;
        }

        private async Task<WorkloadResult> ReadWorkloadAsync(string name, CancellationToken token)
        {
            string container = await SeedContainerAsync(200, token).ConfigureAwait(false);
            WorkloadResult result = await _Engine.RunAsync(name, "REST", async (worker, iteration, ct) =>
            {
                string key = "seed-" + ((worker * 31 + iteration) % 200);
                using (HttpResponseMessage response = await _Rest.GetAsync(Url(container, key), ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    byte[] body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                    return body.Length;
                }
            }, token).ConfigureAwait(false);
            _Reporter.WorkloadComplete(result);
            return result;
        }

        private async Task<WorkloadResult> MixedWorkloadAsync(string name, CancellationToken token)
        {
            string container = await SeedContainerAsync(200, token).ConfigureAwait(false);
            WorkloadResult result = await _Engine.RunAsync(name, "REST", async (worker, iteration, ct) =>
            {
                long slot = (worker * 31 + iteration) % 10;
                if (slot < 7)
                {
                    string key = "seed-" + ((worker * 17 + iteration) % 200);
                    using (HttpResponseMessage response = await _Rest.GetAsync(Url(container, key), ct).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        return (await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false)).Length;
                    }
                }

                if (slot < 9)
                {
                    string key = "m" + worker + "-" + iteration;
                    using (ByteArrayContent content = new ByteArrayContent(_Payload))
                    using (HttpResponseMessage response = await _Rest.PutAsync(Url(container, key), content, ct).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        return _Payload.Length;
                    }
                }

                using (StringContent query = Json("{\"MaxResults\":10}"))
                using (HttpResponseMessage response = await _Rest.PostAsync("/v1.0/containers/" + container + "/objects/enumerate", query, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return 0;
                }
            }, token).ConfigureAwait(false);
            _Reporter.WorkloadComplete(result);
            return result;
        }

        private async Task<WorkloadResult> SearchWorkloadAsync(string name, CancellationToken token)
        {
            string container = await SeedContainerAsync(200, token, true).ConfigureAwait(false);
            WorkloadResult result = await _Engine.RunAsync(name, "REST", async (worker, iteration, ct) =>
            {
                using (StringContent query = Json("{\"MaxResults\":25,\"Labels\":[\"bench\"],\"Tags\":{\"tier\":\"gold\"}}"))
                using (HttpResponseMessage response = await _Rest.PostAsync("/v1.0/containers/" + container + "/objects/enumerate", query, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return 0;
                }
            }, token).ConfigureAwait(false);
            _Reporter.WorkloadComplete(result);
            return result;
        }

        private async Task<WorkloadResult> ReplaceChurnWorkloadAsync(string name, CancellationToken token)
        {
            string container = await NewContainerAsync(token).ConfigureAwait(false);
            WorkloadResult result = await _Engine.RunAsync(name, "REST", async (worker, iteration, ct) =>
            {
                using (ByteArrayContent content = new ByteArrayContent(_Payload))
                using (HttpResponseMessage response = await _Rest.PutAsync(Url(container, "hot-key"), content, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return _Payload.Length;
                }
            }, token).ConfigureAwait(false);
            _Reporter.WorkloadComplete(result);
            return result;
        }

        private async Task<WorkloadResult> DeleteChurnWorkloadAsync(string name, CancellationToken token)
        {
            string container = await NewContainerAsync(token).ConfigureAwait(false);
            WorkloadResult result = await _Engine.RunAsync(name, "REST", async (worker, iteration, ct) =>
            {
                string key = "d" + worker + "-" + iteration;
                using (ByteArrayContent content = new ByteArrayContent(_Payload))
                using (HttpResponseMessage write = await _Rest.PutAsync(Url(container, key), content, ct).ConfigureAwait(false))
                {
                    write.EnsureSuccessStatusCode();
                }
                using (HttpResponseMessage delete = await _Rest.DeleteAsync(Url(container, key), ct).ConfigureAwait(false))
                {
                    delete.EnsureSuccessStatusCode();
                    return _Payload.Length;
                }
            }, token).ConfigureAwait(false);
            _Reporter.WorkloadComplete(result);
            return result;
        }

        private async Task<WorkloadResult> S3WorkloadAsync(string name, CancellationToken token)
        {
            string container = await NewContainerAsync(token).ConfigureAwait(false);
            AmazonS3Client s3 = EnsureS3();

            WorkloadResult result = await _Engine.RunAsync(name, "S3", async (worker, iteration, ct) =>
            {
                string key = "s" + worker + "-" + iteration;
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream(_Payload, false))
                {
                    await s3.PutObjectAsync(new PutObjectRequest { BucketName = container, Key = key, InputStream = ms, ContentType = "application/octet-stream" }, ct).ConfigureAwait(false);
                }
                using (GetObjectResponse get = await s3.GetObjectAsync(new GetObjectRequest { BucketName = container, Key = key }, ct).ConfigureAwait(false))
                using (System.IO.MemoryStream buffer = new System.IO.MemoryStream())
                {
                    await get.ResponseStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
                    return _Payload.Length + buffer.Length;
                }
            }, token).ConfigureAwait(false);
            _Reporter.WorkloadComplete(result);
            return result;
        }

        private async Task<WorkloadResult> RespWorkloadAsync(string name, CancellationToken token)
        {
            IDatabase db = (await EnsureRedisAsync().ConfigureAwait(false)).GetDatabase(0);
            string value = Encoding.Latin1.GetString(_Payload);

            WorkloadResult result = await _Engine.RunAsync(name, "RESP", async (worker, iteration, ct) =>
            {
                string key = "r" + worker + "-" + iteration;
                await db.StringSetAsync(key, value).ConfigureAwait(false);
                RedisValue read = await db.StringGetAsync(key).ConfigureAwait(false);
                return _Payload.Length + (read.IsNull ? 0 : ((byte[]?)read)?.Length ?? 0);
            }, token).ConfigureAwait(false);
            _Reporter.WorkloadComplete(result);
            return result;
        }

        #endregion

        #region Private-Methods-Helpers

        private string Url(string container, string key)
        {
            return "/v1.0/containers/" + container + "/object?key=" + Uri.EscapeDataString(key);
        }

        private async Task<string> NewContainerAsync(CancellationToken token)
        {
            string name = "perf" + Guid.NewGuid().ToString("N").Substring(0, 16);
            using (StringContent body = Json("{\"Name\":\"" + name + "\"}"))
            using (HttpResponseMessage response = await _Rest.PutAsync("/v1.0/containers", body, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
            }
            return name;
        }

        private async Task<string> SeedContainerAsync(int count, CancellationToken token, bool withMetadata = false)
        {
            string container = await NewContainerAsync(token).ConfigureAwait(false);

            for (int i = 0; i < count; i++)
            {
                using (ByteArrayContent content = new ByteArrayContent(_Payload))
                {
                    if (withMetadata)
                    {
                        content.Headers.Add("x-pepperx-labels", "bench");
                        content.Headers.Add("x-pepperx-tags", "tier=gold");
                    }
                    using (HttpResponseMessage response = await _Rest.PutAsync(Url(container, "seed-" + i), content, token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                    }
                }
            }

            return container;
        }

        private AmazonS3Client EnsureS3()
        {
            if (_S3 != null) return _S3;

            AmazonS3Config config = new AmazonS3Config
            {
                ServiceURL = _S3Url,
                ForcePathStyle = true,
                UseHttp = true,
                AuthenticationRegion = "us-west-1",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            };
            _S3 = new AmazonS3Client(new BasicAWSCredentials("pepperx", "pepperx"), config);
            return _S3;
        }

        private async Task<ConnectionMultiplexer> EnsureRedisAsync()
        {
            if (_Redis != null) return _Redis;

            ConfigurationOptions options = ConfigurationOptions.Parse("127.0.0.1:" + _RespPort);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 15000;
            _Redis = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
            return _Redis;
        }

        private static StringContent Json(string json)
        {
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        #endregion
    }
}
