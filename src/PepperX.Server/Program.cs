namespace PepperX.Server
{
    using System.Threading.Tasks;

    /// <summary>
    /// Application entry point. Kept thin; composition and lifecycle live in the bootstrapper and server host.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            return await Bootstrapper.RunAsync(args).ConfigureAwait(false);
        }
    }
}
