namespace Test.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Renders live progress and the final results table. Colors are used only when the output is an
    /// interactive console, so redirected output stays clean.
    /// </summary>
    public sealed class ConsoleReporter
    {
        #region Private-Members

        private readonly bool _Color;
        private bool _ProgressActive;

        private const string _Reset = "[0m";
        private const string _Bold = "[1m";
        private const string _Dim = "[2m";
        private const string _Green = "[32m";
        private const string _Yellow = "[33m";
        private const string _Cyan = "[36m";
        private const string _Red = "[31m";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the reporter.
        /// </summary>
        public ConsoleReporter()
        {
            _Color = !Console.IsOutputRedirected;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Print the harness banner and environment block.
        /// </summary>
        /// <param name="options">Harness options.</param>
        /// <param name="target">Target description.</param>
        public void Banner(PerfOptions options, string target)
        {
            Console.WriteLine();
            Console.WriteLine(Style("PepperX Performance Harness", _Bold + _Cyan));
            Console.WriteLine(Style(new string('=', 78), _Dim));
            Console.WriteLine("  Target        : " + target);
            Console.WriteLine("  Concurrency   : " + options.Concurrency);
            Console.WriteLine("  Object size   : " + FormatBytes(options.ObjectSize));
            Console.WriteLine("  Warmup        : " + options.WarmupSeconds + "s");
            Console.WriteLine("  Measured      : " + options.DurationSeconds + "s per workload");
            Console.WriteLine("  Runtime       : .NET " + Environment.Version);
            Console.WriteLine(Style(new string('=', 78), _Dim));
            Console.WriteLine();
        }

        /// <summary>
        /// Update the live progress line.
        /// </summary>
        /// <param name="workload">Workload name.</param>
        /// <param name="phase">Current phase.</param>
        /// <param name="opsPerSecond">Operations per second so far.</param>
        /// <param name="errors">Error count so far.</param>
        public void Progress(string workload, string phase, double opsPerSecond, long errors)
        {
            if (Console.IsOutputRedirected) return;

            string line = "  " + workload.PadRight(18) + " " + phase.PadRight(10) +
                          opsPerSecond.ToString("F0", CultureInfo.InvariantCulture).PadLeft(9) + " ops/s" +
                          (errors > 0 ? "   errors: " + errors : String.Empty);

            Console.Write("\r" + line.PadRight(78));
            _ProgressActive = true;
        }

        /// <summary>
        /// Clear the live progress line.
        /// </summary>
        public void ProgressComplete()
        {
            if (Console.IsOutputRedirected || !_ProgressActive) return;
            Console.Write("\r" + new string(' ', 78) + "\r");
            _ProgressActive = false;
        }

        /// <summary>
        /// Print one completed workload line.
        /// </summary>
        /// <param name="result">Workload result.</param>
        public void WorkloadComplete(WorkloadResult result)
        {
            string status = result.Errors == 0 ? Style("OK  ", _Green) : Style("WARN", _Yellow);
            Console.WriteLine("  " + status + " " + result.Name.PadRight(18) +
                result.OperationsPerSecond.ToString("F0", CultureInfo.InvariantCulture).PadLeft(9) + " ops/s   " +
                result.MegabytesPerSecond.ToString("F1", CultureInfo.InvariantCulture).PadLeft(8) + " MB/s   p95 " +
                result.LatencyP95Ms.ToString("F1", CultureInfo.InvariantCulture).PadLeft(7) + " ms");
        }

        /// <summary>
        /// Print the final results table.
        /// </summary>
        /// <param name="results">All workload results.</param>
        public void Summary(IReadOnlyList<WorkloadResult> results)
        {
            Console.WriteLine();
            Console.WriteLine(Style("Results", _Bold));
            Console.WriteLine(Style(new string('-', 110), _Dim));
            Console.WriteLine(
                "Workload".PadRight(18) + "Transport".PadRight(10) + "Ops".PadLeft(9) + "Errors".PadLeft(8) +
                "Ops/s".PadLeft(11) + "MB/s".PadLeft(9) + "min".PadLeft(8) + "mean".PadLeft(9) +
                "p50".PadLeft(8) + "p95".PadLeft(9) + "p99".PadLeft(9) + "max".PadLeft(9));
            Console.WriteLine(Style(new string('-', 110), _Dim));

            long totalOps = 0;
            long totalErrors = 0;
            double totalBytes = 0;

            foreach (WorkloadResult r in results)
            {
                totalOps += r.Operations;
                totalErrors += r.Errors;
                totalBytes += r.Bytes;

                Console.WriteLine(
                    r.Name.PadRight(18) +
                    r.Transport.PadRight(10) +
                    r.Operations.ToString(CultureInfo.InvariantCulture).PadLeft(9) +
                    ErrorCell(r.Errors) +
                    r.OperationsPerSecond.ToString("F0", CultureInfo.InvariantCulture).PadLeft(11) +
                    r.MegabytesPerSecond.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9) +
                    r.LatencyMinMs.ToString("F1", CultureInfo.InvariantCulture).PadLeft(8) +
                    r.LatencyMeanMs.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9) +
                    r.LatencyP50Ms.ToString("F1", CultureInfo.InvariantCulture).PadLeft(8) +
                    r.LatencyP95Ms.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9) +
                    r.LatencyP99Ms.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9) +
                    r.LatencyMaxMs.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9));
            }

            Console.WriteLine(Style(new string('-', 110), _Dim));
            Console.WriteLine(
                Style("OVERALL".PadRight(28), _Bold) +
                totalOps.ToString(CultureInfo.InvariantCulture).PadLeft(9) +
                ErrorCell(totalErrors) +
                String.Empty.PadLeft(11) +
                (totalBytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture).PadLeft(9) + " MB total");
            Console.WriteLine();
        }

        #endregion

        #region Private-Methods

        private string ErrorCell(long errors)
        {
            string text = errors.ToString(CultureInfo.InvariantCulture).PadLeft(8);
            return errors == 0 ? text : Style(text, _Red);
        }

        private string Style(string text, string code)
        {
            return _Color ? code + text + _Reset : text;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1048576) return (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MiB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " KiB";
            return bytes + " B";
        }

        #endregion
    }
}
