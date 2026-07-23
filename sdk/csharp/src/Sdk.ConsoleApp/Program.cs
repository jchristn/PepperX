namespace PepperX.Sdk.ConsoleApp
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Sdk;
    using PepperX.Sdk.Models;

    /// <summary>
    /// Walks through the PepperX C# SDK end to end against a live node, printing each step. Point it at a
    /// node with <c>PEPPERX_URL</c> and <c>PEPPERX_WS_URL</c>.
    /// </summary>
    public static class Program
    {
        #region Public-Methods

        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            string restUrl = Environment.GetEnvironmentVariable("PEPPERX_URL") ?? "http://127.0.0.1:8000";
            string wsUrl = Environment.GetEnvironmentVariable("PEPPERX_WS_URL") ?? "ws://127.0.0.1:8002/";
            string container = "sdkdemo" + Guid.NewGuid().ToString("N").Substring(0, 12);

            Console.WriteLine("PepperX C# SDK walkthrough");
            Console.WriteLine("  REST      : " + restUrl);
            Console.WriteLine("  WebSockets: " + wsUrl);
            Console.WriteLine();

            try
            {
                await RestWalkthroughAsync(restUrl, container).ConfigureAwait(false);
                await WebsocketWalkthroughAsync(wsUrl, container + "ws").ConfigureAwait(false);

                Console.WriteLine();
                Console.WriteLine("Walkthrough complete.");
                return 0;
            }
            catch (PepperXException ex)
            {
                Console.WriteLine();
                Console.WriteLine("PepperX returned an error: " + ex.ErrorType + " (" + ex.StatusCode + ") " + ex.Message);
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Failed: " + ex.Message);
                Console.WriteLine("Is a PepperX node running? Set PEPPERX_URL to point at one.");
                return 1;
            }
        }

        #endregion

        #region Private-Methods

        private static async Task RestWalkthroughAsync(string restUrl, string container)
        {
            using (PepperXRestClient client = new PepperXRestClient(restUrl))
            {
                Step("Checking node health");
                bool healthy = await client.HealthAsync().ConfigureAwait(false);
                if (!healthy) throw new Exception("The node did not report healthy.");
                Detail("healthy");

                Step("Creating container '" + container + "'");
                ContainerResponse created = await client.CreateContainerAsync(container, new Dictionary<string, string> { { "demo", "csharp" } }).ConfigureAwait(false);
                Detail("id " + created.Id);

                Step("Writing an object with labels, tags, and a metadata object");
                ObjectWriteResponse write = await client.WriteObjectAsync(
                    container,
                    "reports/2026/summary.txt",
                    Encoding.UTF8.GetBytes("PepperX stores this payload verbatim."),
                    new WriteObjectRequest
                    {
                        ContentType = "text/plain",
                        Labels = new List<string> { "report", "annual" },
                        Tags = new Dictionary<string, string> { { "year", "2026" }, { "team", "platform" } },
                        Object = new Dictionary<string, object> { { "author", "demo" }, { "revision", 3 } }
                    }).ConfigureAwait(false);
                Detail("extent " + write.ExtentId + ", " + write.SizeBytes + " bytes, sha256 " + write.Sha256.Substring(0, 12) + "...");

                Step("Reading the payload back");
                ObjectReadResult? read = await client.ReadObjectAsync(container, "reports/2026/summary.txt").ConfigureAwait(false);
                Detail("\"" + Encoding.UTF8.GetString(read!.Data) + "\"");

                Step("Reading full metadata");
                ObjectMetadata? metadata = await client.ReadObjectMetadataAsync(container, "reports/2026/summary.txt").ConfigureAwait(false);
                Detail("labels [" + String.Join(", ", metadata!.Labels) + "], tags " + metadata.Tags.Count + ", metadata object present: " + (metadata.Object != null));

                Step("Searching by label and tag");
                EnumerationResult<ObjectMetadata> found = await client.EnumerateObjectsAsync(container, new EnumerationQuery
                {
                    Labels = new List<string> { "report" },
                    Tags = new Dictionary<string, string> { { "year", "2026" } }
                }).ConfigureAwait(false);
                Detail(found.TotalRecords + " match(es)");

                Step("Reading statistics");
                StatisticsResponse stats = await client.StatisticsAsync().ConfigureAwait(false);
                Detail(stats.ContainerCount + " containers, " + stats.ObjectCount + " objects, " + stats.TotalBytes + " bytes");

                Step("Deleting the container and its contents");
                await client.DeleteContainerAsync(container, true).ConfigureAwait(false);
                Detail("deleted");
            }
        }

        private static async Task WebsocketWalkthroughAsync(string wsUrl, string container)
        {
            Console.WriteLine();
            await using (PepperXWebsocketClient client = new PepperXWebsocketClient(wsUrl))
            {
                Step("Connecting over WebSockets");
                await client.ConnectAsync().ConfigureAwait(false);
                Detail("connected");

                Step("Creating container '" + container + "'");
                await client.CreateContainerAsync(container).ConfigureAwait(false);
                Detail("created");

                Step("Writing and reading an object");
                await client.WriteObjectAsync(container, "ws-key", Encoding.UTF8.GetBytes("delivered over a persistent connection"),
                    new WriteObjectRequest { ContentType = "text/plain" }).ConfigureAwait(false);
                byte[]? payload = await client.ReadObjectAsync(container, "ws-key").ConfigureAwait(false);
                Detail("\"" + Encoding.UTF8.GetString(payload!) + "\"");

                Step("Issuing 8 concurrent operations on one connection");
                List<Task<bool>> tasks = new List<Task<bool>>();
                for (int i = 0; i < 8; i++) tasks.Add(client.HealthAsync());
                await Task.WhenAll(tasks).ConfigureAwait(false);
                Detail("all 8 correlated correctly");

                Step("Cleaning up");
                await client.DeleteObjectAsync(container, "ws-key").ConfigureAwait(false);
                await client.DeleteContainerAsync(container).ConfigureAwait(false);
                Detail("deleted");
            }
        }

        private static void Step(string message)
        {
            Console.WriteLine("> " + message);
        }

        private static void Detail(string message)
        {
            Console.WriteLine("    " + message);
        }

        #endregion
    }
}
