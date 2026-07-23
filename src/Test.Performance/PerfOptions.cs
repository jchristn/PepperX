namespace Test.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Command-line options for the performance harness.
    /// </summary>
    public class PerfOptions
    {
        #region Public-Members

        /// <summary>Target server base URL. When null, an in-process server is started.</summary>
        public string? Target { get; set; } = null;

        /// <summary>Measured duration per workload in seconds. Clamped to 1 to 3600. Default 5.</summary>
        public int DurationSeconds
        {
            get
            {
                return _DurationSeconds;
            }
            set
            {
                _DurationSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        /// <summary>Warmup duration per workload in seconds. Clamped to 0 to 600. Default 1.</summary>
        public int WarmupSeconds
        {
            get
            {
                return _WarmupSeconds;
            }
            set
            {
                _WarmupSeconds = Math.Clamp(value, 0, 600);
            }
        }

        /// <summary>Concurrent workers. Clamped to 1 to 1024. Default 16.</summary>
        public int Concurrency
        {
            get
            {
                return _Concurrency;
            }
            set
            {
                _Concurrency = Math.Clamp(value, 1, 1024);
            }
        }

        /// <summary>Object payload size in bytes. Clamped to 1 to 256 MiB. Default 4096.</summary>
        public int ObjectSize
        {
            get
            {
                return _ObjectSize;
            }
            set
            {
                _ObjectSize = Math.Clamp(value, 1, 268435456);
            }
        }

        /// <summary>Workloads to run; empty runs the full gauntlet.</summary>
        public List<string> Workloads
        {
            get
            {
                return _Workloads;
            }
            set
            {
                _Workloads = value ?? new List<string>();
            }
        }

        /// <summary>Optional JSON results path.</summary>
        public string? ResultsPath { get; set; } = null;

        /// <summary>Maximum tolerated error rate before a non-zero exit. Default 0.01 (one percent).</summary>
        public double MaxErrorRate { get; set; } = 0.01;

        #endregion

        #region Private-Members

        private int _DurationSeconds = 5;
        private int _WarmupSeconds = 1;
        private int _Concurrency = 16;
        private int _ObjectSize = 4096;
        private List<string> _Workloads = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate default options.
        /// </summary>
        public PerfOptions()
        {
        }

        /// <summary>
        /// Parse options from command-line arguments.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>Parsed options.</returns>
        public static PerfOptions Parse(string[] args)
        {
            PerfOptions options = new PerfOptions();
            if (args == null) return options;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                string? next = i + 1 < args.Length ? args[i + 1] : null;

                switch (arg)
                {
                    case "--target": if (next != null) { options.Target = next; i++; } break;
                    case "--duration": if (next != null && Int32.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d)) { options.DurationSeconds = d; i++; } break;
                    case "--warmup": if (next != null && Int32.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)) { options.WarmupSeconds = w; i++; } break;
                    case "--concurrency": if (next != null && Int32.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c)) { options.Concurrency = c; i++; } break;
                    case "--object-size": if (next != null && Int32.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)) { options.ObjectSize = s; i++; } break;
                    case "--workload": if (next != null) { options.Workloads.AddRange(next.Split(',')); i++; } break;
                    case "--results": if (next != null) { options.ResultsPath = next; i++; } break;
                }
            }

            return options;
        }

        #endregion
    }
}
