namespace PepperX.Sdk.Test.Automated
{
    using System;
    using System.Threading.Tasks;
    using Touchstone.Cli;

    /// <summary>
    /// Console runner for the PepperX C# SDK test suites.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point. Pass <c>--results &lt;path&gt;</c> to export JSON results.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Exit code: 0 when all tests pass, 1 when any test fails.</returns>
        public static async Task<int> Main(string[] args)
        {
            string? resultsPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--results" && i + 1 < args.Length)
                {
                    resultsPath = args[i + 1];
                    break;
                }
            }

            Console.WriteLine("PepperX C# SDK tests");
            Console.WriteLine("  REST      : " + SdkSuites.RestUrl);
            Console.WriteLine("  WebSockets: " + SdkSuites.WebsocketUrl);
            Console.WriteLine();

            return await ConsoleRunner.RunAsync(SdkSuites.All, resultsPath: resultsPath).ConfigureAwait(false);
        }
    }
}
