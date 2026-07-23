namespace Test.Performance
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Records operation latencies and computes percentiles. Samples are captured under a lock so many
    /// worker tasks can record concurrently. This type is thread-safe.
    /// </summary>
    public sealed class LatencyRecorder
    {
        #region Private-Members

        private readonly object _Lock = new object();
        private readonly List<double> _Samples = new List<double>();
        private double _Min = double.MaxValue;
        private double _Max = 0;
        private double _Total = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a latency recorder.
        /// </summary>
        public LatencyRecorder()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record one latency sample.
        /// </summary>
        /// <param name="milliseconds">Latency in milliseconds.</param>
        public void Record(double milliseconds)
        {
            lock (_Lock)
            {
                _Samples.Add(milliseconds);
                if (milliseconds < _Min) _Min = milliseconds;
                if (milliseconds > _Max) _Max = milliseconds;
                _Total += milliseconds;
            }
        }

        /// <summary>
        /// Number of recorded samples.
        /// </summary>
        /// <returns>Sample count.</returns>
        public int Count()
        {
            lock (_Lock) { return _Samples.Count; }
        }

        /// <summary>
        /// Minimum latency in milliseconds, or zero when no samples exist.
        /// </summary>
        /// <returns>Minimum latency.</returns>
        public double Min()
        {
            lock (_Lock) { return _Samples.Count == 0 ? 0 : _Min; }
        }

        /// <summary>
        /// Maximum latency in milliseconds.
        /// </summary>
        /// <returns>Maximum latency.</returns>
        public double Max()
        {
            lock (_Lock) { return _Max; }
        }

        /// <summary>
        /// Mean latency in milliseconds.
        /// </summary>
        /// <returns>Mean latency.</returns>
        public double Mean()
        {
            lock (_Lock) { return _Samples.Count == 0 ? 0 : _Total / _Samples.Count; }
        }

        /// <summary>
        /// Latency at a percentile.
        /// </summary>
        /// <param name="percentile">Percentile in the range 0 to 100.</param>
        /// <returns>Latency in milliseconds.</returns>
        public double Percentile(double percentile)
        {
            lock (_Lock)
            {
                if (_Samples.Count == 0) return 0;
                List<double> sorted = new List<double>(_Samples);
                sorted.Sort();
                int index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
                if (index < 0) index = 0;
                if (index >= sorted.Count) index = sorted.Count - 1;
                return sorted[index];
            }
        }

        #endregion
    }
}
