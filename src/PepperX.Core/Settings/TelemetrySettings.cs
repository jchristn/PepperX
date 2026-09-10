namespace PepperX.Core.Settings
{
    using System;

    /// <summary>
    /// Observability configuration: OpenTelemetry metrics, traces, and logs export. PepperX emits through the
    /// .NET base class library (<c>Meter</c>, <c>ActivitySource</c>, <c>ILogger</c>) and stays a no-op until a
    /// host subscribes; these settings describe the host and its OTLP exporters.
    /// <para>
    /// Disabled by default. A bare process with no collector must not pay the cost of a background exporter
    /// repeatedly failing to reach a dead endpoint, nor the flush stall on shutdown. Turn this on (and point
    /// <see cref="OtlpEndpoint"/> at a collector) in any deployment wired to an observability stack; the docker
    /// compose stack does exactly that.
    /// </para>
    /// </summary>
    public class TelemetrySettings
    {
        #region Public-Members

        /// <summary>
        /// Master switch for the telemetry pipeline. Default false. When false the instruments still exist but
        /// nothing is subscribed or exported, so emit is a cheap no-op.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// OTLP exporter endpoint the metrics, traces, and logs are pushed to. Points at an OpenTelemetry
        /// Collector. Use the gRPC port (4317) with <see cref="OtlpProtocol"/> "grpc" or the HTTP port (4318)
        /// with "http". Default "http://localhost:4317".
        /// </summary>
        public string OtlpEndpoint
        {
            get
            {
                return _OtlpEndpoint;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(OtlpEndpoint));
                _OtlpEndpoint = value;
            }
        }

        /// <summary>
        /// OTLP wire protocol: "grpc" (default, port 4317) or "http" (OTLP/HTTP protobuf, port 4318). Any other
        /// value is rejected when the pipeline is built.
        /// </summary>
        public string OtlpProtocol
        {
            get
            {
                return _OtlpProtocol;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(OtlpProtocol));
                _OtlpProtocol = value;
            }
        }

        /// <summary>
        /// Whether the metrics pillar is exported. Default true. Instruments are recorded regardless; this
        /// gates the OTLP metric reader.
        /// </summary>
        public bool Metrics { get; set; } = true;

        /// <summary>
        /// Whether the traces pillar is exported. Default true.
        /// </summary>
        public bool Traces { get; set; } = true;

        /// <summary>
        /// Whether the logs pillar is exported. Default true. When true, PepperX log records are shipped over
        /// OTLP (to the collector, and on to Loki) with trace correlation.
        /// </summary>
        public bool Logs { get; set; } = true;

        /// <summary>
        /// Head-based trace sampling ratio in the range 0.0 to 1.0. 1.0 samples every trace. Clamped on set.
        /// Default 1.0.
        /// </summary>
        public double SamplingRatio
        {
            get
            {
                return _SamplingRatio;
            }
            set
            {
                _SamplingRatio = Math.Clamp(value, 0.0, 1.0);
            }
        }

        /// <summary>
        /// Whether .NET runtime metrics (GC, heap, threads, JIT) are collected. Default true.
        /// </summary>
        public bool IncludeRuntimeMetrics { get; set; } = true;

        /// <summary>
        /// Whether process metrics (working set, uptime, thread count) are collected. Default true.
        /// </summary>
        public bool IncludeProcessMetrics { get; set; } = true;

        /// <summary>
        /// OTLP metric export interval in milliseconds. Clamped to the range 1000 to 300000. Default 15000.
        /// </summary>
        public int ExportIntervalMs
        {
            get
            {
                return _ExportIntervalMs;
            }
            set
            {
                _ExportIntervalMs = Math.Clamp(value, 1000, 300000);
            }
        }

        /// <summary>
        /// Whether an in-process Prometheus scrape endpoint is served in addition to OTLP push. Default false;
        /// the compose stack scrapes the collector, not the node. Enable this to let Prometheus scrape a node
        /// directly. Requires a free <see cref="PrometheusPort"/>.
        /// </summary>
        public bool PrometheusEnabled { get; set; } = false;

        /// <summary>
        /// Port for the in-process Prometheus scrape endpoint when <see cref="PrometheusEnabled"/> is true.
        /// Clamped to the range 1 to 65535. Default 9464.
        /// </summary>
        public int PrometheusPort
        {
            get
            {
                return _PrometheusPort;
            }
            set
            {
                _PrometheusPort = Math.Clamp(value, 1, 65535);
            }
        }

        #endregion

        #region Private-Members

        private string _OtlpEndpoint = "http://localhost:4317";
        private string _OtlpProtocol = "grpc";
        private double _SamplingRatio = 1.0;
        private int _ExportIntervalMs = 15000;
        private int _PrometheusPort = 9464;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate telemetry settings.
        /// </summary>
        public TelemetrySettings()
        {
        }

        #endregion
    }
}
