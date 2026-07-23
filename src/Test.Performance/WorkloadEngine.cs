namespace Test.Performance
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Drives a fixed number of concurrent workers against an operation for a warmup period followed by a
    /// measured period, collecting throughput and latency statistics.
    /// </summary>
    public sealed class WorkloadEngine
    {
        #region Private-Members

        private readonly PerfOptions _Options;
        private readonly ConsoleReporter _Reporter;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the workload engine.
        /// </summary>
        /// <param name="options">Harness options.</param>
        /// <param name="reporter">Console reporter for live progress.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public WorkloadEngine(PerfOptions options, ConsoleReporter reporter)
        {
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            _Reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one workload.
        /// </summary>
        /// <param name="name">Workload name.</param>
        /// <param name="transport">Transport label.</param>
        /// <param name="operation">
        /// The operation to execute per iteration. It receives the worker index and iteration number and
        /// returns the number of bytes transferred.
        /// </param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The workload result.</returns>
        public async Task<WorkloadResult> RunAsync(string name, string transport, Func<int, long, CancellationToken, Task<long>> operation, CancellationToken token = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            LatencyRecorder latency = new LatencyRecorder();
            long operations = 0;
            long errors = 0;
            long bytes = 0;
            long measuring = 0;

            using (CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task[] workers = new Task[_Options.Concurrency];
                for (int i = 0; i < _Options.Concurrency; i++)
                {
                    int workerIndex = i;
                    workers[i] = Task.Run(async () =>
                    {
                        long iteration = 0;
                        while (!stop.IsCancellationRequested)
                        {
                            long current = Interlocked.Read(ref iteration);
                            Stopwatch sw = Stopwatch.StartNew();
                            try
                            {
                                long transferred = await operation(workerIndex, current, stop.Token).ConfigureAwait(false);
                                sw.Stop();

                                if (Interlocked.Read(ref measuring) == 1)
                                {
                                    latency.Record(sw.Elapsed.TotalMilliseconds);
                                    Interlocked.Increment(ref operations);
                                    Interlocked.Add(ref bytes, transferred);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                            catch (Exception)
                            {
                                sw.Stop();
                                if (Interlocked.Read(ref measuring) == 1) Interlocked.Increment(ref errors);
                            }

                            iteration++;
                        }
                    }, stop.Token);
                }

                if (_Options.WarmupSeconds > 0)
                {
                    _Reporter.Progress(name, "warmup", 0, 0);
                    await Task.Delay(TimeSpan.FromSeconds(_Options.WarmupSeconds), token).ConfigureAwait(false);
                }

                Interlocked.Exchange(ref measuring, 1);
                Stopwatch measured = Stopwatch.StartNew();

                for (int elapsed = 0; elapsed < _Options.DurationSeconds; elapsed++)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                    double seconds = measured.Elapsed.TotalSeconds;
                    _Reporter.Progress(name, "measuring", Interlocked.Read(ref operations) / Math.Max(seconds, 0.001), Interlocked.Read(ref errors));
                }

                measured.Stop();
                Interlocked.Exchange(ref measuring, 0);
                stop.Cancel();

                try
                {
                    await Task.WhenAll(workers).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when workers observe cancellation.
                }

                _Reporter.ProgressComplete();

                return new WorkloadResult
                {
                    Name = name,
                    Transport = transport,
                    Operations = Interlocked.Read(ref operations),
                    Errors = Interlocked.Read(ref errors),
                    Bytes = Interlocked.Read(ref bytes),
                    DurationSeconds = measured.Elapsed.TotalSeconds,
                    Concurrency = _Options.Concurrency,
                    LatencyMinMs = latency.Min(),
                    LatencyMeanMs = latency.Mean(),
                    LatencyP50Ms = latency.Percentile(50),
                    LatencyP95Ms = latency.Percentile(95),
                    LatencyP99Ms = latency.Percentile(99),
                    LatencyMaxMs = latency.Max()
                };
            }
        }

        #endregion
    }
}
