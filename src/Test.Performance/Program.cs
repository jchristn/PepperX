namespace Test.Performance
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Serialization;
    using PepperX.Core.Settings;
    using PepperX.Server;
    using SyslogLogging;

    /// <summary>
    /// PepperX performance and throughput harness. Runs a mix of workloads against a running node (or an
    /// in-process node it starts itself) and prints formatted throughput and latency results.
    /// </summary>
    public static class Program
    {
        #region Public-Methods

        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Exit code: 0 when the error rate is within tolerance, otherwise 1.</returns>
        public static async Task<int> Main(string[] args)
        {
            PerfOptions options = PerfOptions.Parse(args);
            ConsoleReporter reporter = new ConsoleReporter();

            PepperXServer? server = null;
            LoggingModule? logging = null;
            string storageRoot = Path.Combine(Path.GetTempPath(), "pepperx-perf-" + Guid.NewGuid().ToString("N"));

            string baseUrl;
            string? s3Url;
            int respPort;

            try
            {
                if (!string.IsNullOrEmpty(options.Target))
                {
                    baseUrl = options.Target!;
                    s3Url = null;
                    respPort = 0;
                    reporter.Banner(options, baseUrl + " (external)");
                }
                else
                {
                    int restPort = FreePort();
                    int s3Port = FreePort();
                    int rport = FreePort();

                    PepperXSettings settings = BuildSettings(storageRoot, restPort, s3Port, rport);
                    logging = new LoggingModule(Path.Combine(storageRoot, "perf.log"), FileLoggingMode.SingleLogFile, false);
                    Directory.CreateDirectory(storageRoot);

                    server = new PepperXServer(settings, logging);
                    await server.StartAsync().ConfigureAwait(false);

                    baseUrl = "http://127.0.0.1:" + restPort;
                    s3Url = "http://127.0.0.1:" + s3Port;
                    respPort = rport;
                    reporter.Banner(options, baseUrl + " (in-process)");
                }

                List<WorkloadResult> results;
                await using (PerfHarness harness = new PerfHarness(options, reporter, baseUrl, s3Url, respPort))
                {
                    results = await harness.RunAsync(CancellationToken.None).ConfigureAwait(false);
                }

                reporter.Summary(results);

                if (!string.IsNullOrEmpty(options.ResultsPath))
                {
                    PepperXSerializer serializer = new PepperXSerializer();
                    await File.WriteAllTextAsync(options.ResultsPath!, serializer.SerializeJson(results, true) ?? "[]").ConfigureAwait(false);
                    Console.WriteLine("Results written to " + options.ResultsPath);
                }

                long totalOps = 0;
                long totalErrors = 0;
                foreach (WorkloadResult r in results)
                {
                    totalOps += r.Operations;
                    totalErrors += r.Errors;
                }

                double errorRate = totalOps + totalErrors == 0 ? 0 : (double)totalErrors / (totalOps + totalErrors);
                if (errorRate > options.MaxErrorRate)
                {
                    Console.WriteLine("Error rate " + errorRate.ToString("P2") + " exceeds the tolerance of " + options.MaxErrorRate.ToString("P2") + ".");
                    return 1;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Harness failed: " + ex.Message);
                return 1;
            }
            finally
            {
                if (server != null) await server.StopAsync().ConfigureAwait(false);
                logging?.Dispose();
                TryCleanup(storageRoot);
            }
        }

        #endregion

        #region Private-Methods

        private static PepperXSettings BuildSettings(string storageRoot, int restPort, int s3Port, int respPort)
        {
            PepperXSettings settings = new PepperXSettings();

            settings.Database.Hostname = Environment.GetEnvironmentVariable("PEPPERX_TEST_DB_HOST") ?? "localhost";
            settings.Database.Port = int.TryParse(Environment.GetEnvironmentVariable("PEPPERX_TEST_DB_PORT"), out int dbPort) ? dbPort : 5433;
            settings.Database.DatabaseName = Environment.GetEnvironmentVariable("PEPPERX_TEST_DB_NAME") ?? "pepperx_test";
            settings.Database.Username = Environment.GetEnvironmentVariable("PEPPERX_TEST_DB_USER") ?? "pepperx_test";
            settings.Database.Password = Environment.GetEnvironmentVariable("PEPPERX_TEST_DB_PASSWORD") ?? "pepperx_test";

            settings.Storage.Disk.RootDirectory = storageRoot;
            settings.Rest.Hostname = "127.0.0.1";
            settings.Rest.Port = restPort;
            settings.S3.Hostname = "127.0.0.1";
            settings.S3.Port = s3Port;
            settings.Resp.Port = respPort;
            settings.Websocket.Enabled = false;
            settings.Mcp.Enabled = false;
            settings.RequestHistory.Enabled = false;
            settings.Cluster.JanitorIntervalSeconds = 3600;

            return settings;
        }

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void TryCleanup(string root)
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }

        #endregion
    }
}
