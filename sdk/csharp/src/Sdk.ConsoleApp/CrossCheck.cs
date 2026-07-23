namespace PepperX.Sdk.ConsoleApp
{
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using PepperX.Sdk;
    using PepperX.Sdk.Models;

    /// <summary>
    /// Verifies that objects written by the Python and JavaScript SDKs read back identically through
    /// the C# SDK, and writes one for them to read in turn. Run with the <c>crosscheck</c> argument.
    /// </summary>
    public static class CrossCheck
    {
        /// <summary>
        /// Run the cross-SDK contract check.
        /// </summary>
        /// <param name="baseUrl">Node base URL.</param>
        /// <returns>Exit code.</returns>
        public static async Task<int> RunAsync(string baseUrl)
        {
            using (PepperXRestClient client = new PepperXRestClient(baseUrl))
            {
                ObjectReadResult? canonical = await client.ReadObjectAsync("crosssdk", "canonical").ConfigureAwait(false);
                ObjectMetadata? canonicalMeta = await client.ReadObjectMetadataAsync("crosssdk", "canonical").ConfigureAwait(false);
                Console.WriteLine("csharp read python object: " + canonical!.Data.Length + " bytes, labels [" +
                    String.Join(",", canonicalMeta!.Labels) + "], tag lang=" + canonicalMeta.Tags["lang"]);

                ObjectReadResult? fromJs = await client.ReadObjectAsync("crosssdk", "from-js").ConfigureAwait(false);
                Console.WriteLine("csharp read js object: " + fromJs!.Data.Length + " bytes");

                await client.WriteObjectAsync("crosssdk", "from-csharp", Encoding.UTF8.GetBytes("csharp-bytes"),
                    new WriteObjectRequest
                    {
                        ContentType = "text/plain",
                        Labels = new System.Collections.Generic.List<string> { "csharp-label" },
                        Tags = new System.Collections.Generic.Dictionary<string, string> { { "lang", "csharp" } },
                        Object = new { source = "csharp" }
                    }).ConfigureAwait(false);
                Console.WriteLine("csharp wrote from-csharp");
                return 0;
            }
        }
    }
}
