namespace PepperX.Server
{
    using System;

    /// <summary>
    /// Application entry point. Kept thin per the backend architecture reference; orchestration lives in the server host.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static int Main(string[] args)
        {
            // The full bootstrapper and PepperXServer are wired in Phase 05.
            Console.WriteLine("PepperX server host bootstrap is not yet wired (Phase 05).");
            return 0;
        }
    }
}
