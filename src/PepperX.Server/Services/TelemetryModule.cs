namespace PepperX.Server.Services
{
    using System;
    using PepperX.Core;
    using PepperX.Core.Settings;
    using Radiant;
    using SyslogLogging;

    /// <summary>
    /// Owns the process telemetry pipeline. Translates <see cref="TelemetrySettings"/> into a
    /// <see cref="RadiantSettings"/> and starts a single <see cref="RadiantHost"/>, which subscribes to the
    /// PepperX meter/activity source (by service name) and to Npgsql's built-in instrumentation, then exports
    /// metrics, traces, and logs over OTLP to a collector.
    /// <para>
    /// Emit never depends on this type: <see cref="PepperX.Core.Telemetry.PepperXTelemetry"/> records through
    /// the .NET base class library and stays a no-op until this host subscribes. Failure to start the pipeline
    /// (for example a collector that is down or a scrape-port conflict) is logged and swallowed — telemetry is
    /// observability, not a hard dependency of the data plane.
    /// </para>
    /// </summary>
    public sealed class TelemetryModule : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Whether a live pipeline is running. False when telemetry is disabled or failed to start.
        /// </summary>
        public bool IsEnabled
        {
            get
            {
                return _Host != null && _Host.IsEnabled;
            }
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[TelemetryModule] ";
        private readonly LoggingModule _Logging;
        private readonly RadiantHost? _Host;

        #endregion

        #region Constructors-and-Factories

        private TelemetryModule(LoggingModule logging, RadiantHost? host)
        {
            _Logging = logging;
            _Host = host;
        }

        /// <summary>
        /// Build and start the telemetry pipeline from settings. Always returns a module; when telemetry is
        /// disabled or the pipeline fails to start, the module is inert and emit stays a no-op.
        /// </summary>
        /// <param name="settings">Telemetry settings.</param>
        /// <param name="serviceInstanceId">Stable per-instance identifier (the node id) stamped as service.instance.id.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>A telemetry module.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public static TelemetryModule Start(TelemetrySettings settings, string serviceInstanceId, LoggingModule logging)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            if (!settings.Enabled)
            {
                logging.Debug("[TelemetryModule] telemetry disabled; instruments are inert");
                return new TelemetryModule(logging, null);
            }

            try
            {
                RadiantSettings radiant = BuildRadiantSettings(settings, serviceInstanceId, logging);
                RadiantHost host = RadiantHost.Start(radiant);

                logging.Info("[TelemetryModule] telemetry started; exporting OTLP (" + settings.OtlpProtocol +
                    ") to " + settings.OtlpEndpoint +
                    (settings.PrometheusEnabled ? ", Prometheus scrape on :" + settings.PrometheusPort : String.Empty));

                return new TelemetryModule(logging, host);
            }
            catch (Exception e)
            {
                logging.Warn("[TelemetryModule] failed to start telemetry pipeline; continuing without it: " + e.Message);
                return new TelemetryModule(logging, null);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Flush pending metrics and traces to the exporter. No-op when telemetry is not running.
        /// </summary>
        /// <param name="timeoutMs">Flush timeout in milliseconds.</param>
        public void ForceFlush(int timeoutMs = 5000)
        {
            try { _Host?.ForceFlush(timeoutMs); }
            catch (Exception e) { _Logging.Debug(_Header + "flush raised " + e.GetType().Name); }
        }

        /// <summary>
        /// Flush and dispose the telemetry pipeline.
        /// </summary>
        public void Dispose()
        {
            try { _Host?.Dispose(); }
            catch (Exception e) { _Logging.Debug(_Header + "dispose raised " + e.GetType().Name); }
        }

        #endregion

        #region Private-Methods

        private static RadiantSettings BuildRadiantSettings(TelemetrySettings settings, string serviceInstanceId, LoggingModule logging)
        {
            RadiantSettings radiant = new RadiantSettings(Constants.ProductName);
            radiant.ServiceInstanceId = String.IsNullOrWhiteSpace(serviceInstanceId) ? null : serviceInstanceId;
            radiant.DiagnosticCallback = message => logging.Debug("[Radiant] " + message);

            radiant.Otlp.Enable = true;
            radiant.Otlp.Endpoint = settings.OtlpEndpoint;
            radiant.Otlp.Protocol = ParseProtocol(settings.OtlpProtocol);

            radiant.Metrics.Enable = settings.Metrics;
            radiant.Metrics.IncludeRuntime = settings.IncludeRuntimeMetrics;
            radiant.Metrics.IncludeProcess = settings.IncludeProcessMetrics;
            radiant.Metrics.ExportIntervalMs = settings.ExportIntervalMs;

            radiant.Traces.Enable = settings.Traces;
            radiant.Traces.SamplingRatio = settings.SamplingRatio;

            radiant.Logs.Enable = settings.Logs;

            radiant.Prometheus.Enable = settings.PrometheusEnabled;
            radiant.Prometheus.Port = settings.PrometheusPort;

            // PepperX's own meter/activity source are named after the service and picked up automatically.
            // Npgsql ships built-in OpenTelemetry instrumentation under the "Npgsql" name; subscribe to it so
            // database command spans and connection metrics are collected with zero per-call code.
            radiant.Sources.AddMeter("Npgsql");
            radiant.Sources.AddActivitySource("Npgsql");

            return radiant;
        }

        private static OtlpProtocolEnum ParseProtocol(string protocol)
        {
            if (String.Equals(protocol, "http", StringComparison.OrdinalIgnoreCase)
                || String.Equals(protocol, "httpprotobuf", StringComparison.OrdinalIgnoreCase)
                || String.Equals(protocol, "http/protobuf", StringComparison.OrdinalIgnoreCase))
            {
                return OtlpProtocolEnum.HttpProtobuf;
            }
            return OtlpProtocolEnum.Grpc;
        }

        #endregion
    }
}
