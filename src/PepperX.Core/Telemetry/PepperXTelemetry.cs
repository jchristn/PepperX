namespace PepperX.Core.Telemetry
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// The process-wide telemetry surface for PepperX. Owns the single <see cref="Meter"/> and
    /// <see cref="ActivitySource"/> — both named after the product so a subscribing host picks them up by
    /// name — and every instrument PepperX records to. Emit rides the .NET base class library and stays a
    /// cheap no-op until a host (see the server's telemetry module, which starts a Radiant host) subscribes.
    /// <para>
    /// Instruments are created once and shared. All record helpers are safe to call from any thread and never
    /// throw; span helpers return <c>null</c> when nothing is listening. Instrument names carry the domain
    /// (for example <c>pepperx.http.*</c>, <c>pepperx.s3.*</c>) so dashboards can be organized by surface.
    /// </para>
    /// </summary>
    public static class PepperXTelemetry
    {
        #region Public-Members

        /// <summary>
        /// The product name, used as both the meter name and the activity-source name so a host subscribing to
        /// the service name collects PepperX telemetry with no extra source registration.
        /// </summary>
        public const string SourceName = Constants.ProductName;

        /// <summary>
        /// The single meter every PepperX instrument is created on. Named <see cref="SourceName"/>.
        /// </summary>
        public static readonly Meter Meter = new Meter(SourceName, Constants.ProductVersion);

        /// <summary>
        /// The single activity source every PepperX span is started on. Named <see cref="SourceName"/>.
        /// </summary>
        public static readonly ActivitySource ActivitySource = new ActivitySource(SourceName, Constants.ProductVersion);

        #endregion

        #region Private-Members

        // Tag keys. Low-cardinality, dotted-lowercase, aligned with OpenTelemetry semantic conventions where
        // one exists.
        private const string TagHttpMethod = "http.request.method";
        private const string TagHttpRoute = "http.route";
        private const string TagHttpStatus = "http.response.status_code";
        private const string TagStatus = "pepperx.status";        // "ok" | "error"
        private const string TagOperation = "pepperx.operation";
        private const string TagTransport = "pepperx.transport";  // mcp: "http" | "tcp"
        private const string TagStep = "pepperx.step";            // janitor step name
        private const string TagScope = "pepperx.scope";          // search: "container" | "all"

        private const string StatusOk = "ok";
        private const string StatusError = "error";

        // Second-valued latency buckets aligned with the OpenTelemetry HTTP server default set.
        private static readonly double[] _LatencyBuckets =
            new double[] { 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10 };

        // HTTP / REST.
        private static readonly Histogram<double> _HttpDuration = Meter.CreateHistogram<double>(
            "pepperx.http.server.request.duration", "s", "Duration of PepperX REST requests.");
        private static readonly UpDownCounter<long> _HttpActive = Meter.CreateUpDownCounter<long>(
            "pepperx.http.server.active_requests", "{request}", "In-flight PepperX REST requests.");
        private static readonly Counter<long> _HttpRequests = Meter.CreateCounter<long>(
            "pepperx.http.server.requests", "{request}", "Total PepperX REST requests.");
        private static readonly Histogram<long> _HttpRequestSize = Meter.CreateHistogram<long>(
            "pepperx.http.server.request.body.size", "By", "REST request body size.");
        private static readonly Histogram<long> _HttpResponseSize = Meter.CreateHistogram<long>(
            "pepperx.http.server.response.body.size", "By", "REST response body size.");

        // S3.
        private static readonly Histogram<double> _S3Duration = Meter.CreateHistogram<double>(
            "pepperx.s3.operation.duration", "s", "Duration of S3-compatible operations.");
        private static readonly Counter<long> _S3Operations = Meter.CreateCounter<long>(
            "pepperx.s3.operations", "{operation}", "Total S3-compatible operations.");

        // RESP (Redis wire protocol).
        private static readonly Histogram<double> _RespDuration = Meter.CreateHistogram<double>(
            "pepperx.resp.command.duration", "s", "Duration of RESP commands.");
        private static readonly Counter<long> _RespCommands = Meter.CreateCounter<long>(
            "pepperx.resp.commands", "{command}", "Total RESP commands.");
        private static readonly UpDownCounter<long> _RespConnections = Meter.CreateUpDownCounter<long>(
            "pepperx.resp.connections", "{connection}", "Open RESP connections.");

        // WebSockets.
        private static readonly Histogram<double> _WsDuration = Meter.CreateHistogram<double>(
            "pepperx.ws.operation.duration", "s", "Duration of WebSocket operations.");
        private static readonly Counter<long> _WsOperations = Meter.CreateCounter<long>(
            "pepperx.ws.operations", "{operation}", "Total WebSocket operations.");
        private static readonly Counter<long> _WsMessages = Meter.CreateCounter<long>(
            "pepperx.ws.messages", "{message}", "Total WebSocket messages received.");
        private static readonly UpDownCounter<long> _WsConnections = Meter.CreateUpDownCounter<long>(
            "pepperx.ws.connections", "{connection}", "Open WebSocket connections.");

        // MCP.
        private static readonly Histogram<double> _McpDuration = Meter.CreateHistogram<double>(
            "pepperx.mcp.tool.duration", "s", "Duration of MCP tool calls.");
        private static readonly Counter<long> _McpCalls = Meter.CreateCounter<long>(
            "pepperx.mcp.tool.calls", "{call}", "Total MCP tool calls.");

        // Object application-layer paths.
        private static readonly Histogram<double> _ObjectDuration = Meter.CreateHistogram<double>(
            "pepperx.object.operation.duration", "s", "Duration of object operations.");
        private static readonly Counter<long> _ObjectOperations = Meter.CreateCounter<long>(
            "pepperx.object.operations", "{operation}", "Total object operations.");
        private static readonly Counter<long> _ObjectBytesWritten = Meter.CreateCounter<long>(
            "pepperx.object.bytes.written", "By", "Object payload bytes written.");
        private static readonly Counter<long> _ObjectBytesRead = Meter.CreateCounter<long>(
            "pepperx.object.bytes.read", "By", "Object payload bytes read.");
        private static readonly Counter<long> _ObjectCacheHits = Meter.CreateCounter<long>(
            "pepperx.object.cache.hits", "{hit}", "Object read cache hits.");
        private static readonly Counter<long> _ObjectCacheMisses = Meter.CreateCounter<long>(
            "pepperx.object.cache.misses", "{miss}", "Object read cache misses.");

        // Container application-layer paths.
        private static readonly Histogram<double> _ContainerDuration = Meter.CreateHistogram<double>(
            "pepperx.container.operation.duration", "s", "Duration of container operations.");
        private static readonly Counter<long> _ContainerOperations = Meter.CreateCounter<long>(
            "pepperx.container.operations", "{operation}", "Total container operations.");

        // Multipart uploads.
        private static readonly Histogram<double> _MultipartDuration = Meter.CreateHistogram<double>(
            "pepperx.multipart.operation.duration", "s", "Duration of multipart upload operations.");
        private static readonly Counter<long> _MultipartOperations = Meter.CreateCounter<long>(
            "pepperx.multipart.operations", "{operation}", "Total multipart upload operations.");
        private static readonly Counter<long> _MultipartBytes = Meter.CreateCounter<long>(
            "pepperx.multipart.bytes", "By", "Multipart upload part bytes written.");

        // Search.
        private static readonly Histogram<double> _SearchDuration = Meter.CreateHistogram<double>(
            "pepperx.search.duration", "s", "Duration of search and enumeration operations.");
        private static readonly Counter<long> _SearchOperations = Meter.CreateCounter<long>(
            "pepperx.search.operations", "{operation}", "Total search and enumeration operations.");

        // Storage (physical extent I/O).
        private static readonly Histogram<double> _StorageDuration = Meter.CreateHistogram<double>(
            "pepperx.storage.operation.duration", "s", "Duration of extent storage operations.");
        private static readonly Counter<long> _StorageOperations = Meter.CreateCounter<long>(
            "pepperx.storage.operations", "{operation}", "Total extent storage operations.");
        private static readonly Counter<long> _StorageBytesWritten = Meter.CreateCounter<long>(
            "pepperx.storage.bytes.written", "By", "Bytes written to extent storage.");
        private static readonly Counter<long> _StorageBytesRead = Meter.CreateCounter<long>(
            "pepperx.storage.bytes.read", "By", "Bytes read from extent storage.");

        // Background workflows.
        private static readonly Histogram<double> _JanitorDuration = Meter.CreateHistogram<double>(
            "pepperx.janitor.run.duration", "s", "Duration of a janitor maintenance pass.");
        private static readonly Counter<long> _JanitorRuns = Meter.CreateCounter<long>(
            "pepperx.janitor.runs", "{run}", "Total janitor maintenance passes.");
        private static readonly Counter<long> _JanitorItems = Meter.CreateCounter<long>(
            "pepperx.janitor.items", "{item}", "Items processed by the janitor, by step.");
        private static readonly Counter<long> _Heartbeats = Meter.CreateCounter<long>(
            "pepperx.heartbeat.beats", "{beat}", "Node heartbeat writes.");
        private static readonly Histogram<double> _RehydrationDuration = Meter.CreateHistogram<double>(
            "pepperx.rehydration.duration", "s", "Duration of a rehydration pass.");
        private static readonly Counter<long> _RehydrationRuns = Meter.CreateCounter<long>(
            "pepperx.rehydration.runs", "{run}", "Total rehydration passes.");
        private static readonly Counter<long> _RequestHistoryCaptured = Meter.CreateCounter<long>(
            "pepperx.request_history.captured", "{record}", "Request-history records captured.");
        private static readonly Counter<long> _RequestHistoryDropped = Meter.CreateCounter<long>(
            "pepperx.request_history.dropped", "{record}", "Request-history records dropped (capacity or error).");

        private static Func<StorageCapacitySnapshot?>? _CapacityProvider;

        #endregion

        #region Constructors-and-Factories

        static PepperXTelemetry()
        {
            // Reference the shared bucket set so a future OTLP view can be attached by name; the histograms are
            // exported with default boundaries unless the host applies a view. Kept as a field so the value is
            // discoverable and documented in one place.
            _ = _LatencyBuckets;

            Meter.CreateObservableGauge<long>(
                "pepperx.storage.capacity.total.bytes", ObserveTotalCapacity, "By", "Total extent storage capacity.");
            Meter.CreateObservableGauge<long>(
                "pepperx.storage.capacity.free.bytes", ObserveFreeCapacity, "By", "Free extent storage capacity.");
            Meter.CreateObservableGauge<long>(
                "pepperx.storage.capacity.used.bytes", ObserveUsedCapacity, "By", "Used extent storage capacity.");
        }

        #endregion

        #region Public-Methods-Spans

        /// <summary>
        /// Start a span on the PepperX activity source. Returns null when no listener is sampling; callers may
        /// use the result in a <c>using</c> and null-conditional tag calls safely.
        /// </summary>
        /// <param name="name">Span name.</param>
        /// <param name="kind">Span kind. Defaults to <see cref="ActivityKind.Internal"/>.</param>
        /// <returns>The started activity, or null.</returns>
        public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
        {
            return ActivitySource.StartActivity(name, kind);
        }

        /// <summary>
        /// Mark an activity as failed and record the exception on it. No-op when <paramref name="activity"/> is
        /// null.
        /// </summary>
        /// <param name="activity">The activity, or null.</param>
        /// <param name="ex">The exception to record.</param>
        public static void RecordException(Activity? activity, Exception ex)
        {
            if (activity == null || ex == null) return;
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.SetTag("exception.type", ex.GetType().FullName);
            activity.SetTag("exception.message", ex.Message);
            activity.SetTag("exception.stacktrace", ex.StackTrace);
        }

        #endregion

        #region Public-Methods-Http

        /// <summary>Increment the in-flight REST request gauge.</summary>
        public static void HttpRequestStarted()
        {
            _HttpActive.Add(1);
        }

        /// <summary>Decrement the in-flight REST request gauge.</summary>
        public static void HttpRequestEnded()
        {
            _HttpActive.Add(-1);
        }

        /// <summary>
        /// Record a completed REST request: duration, count, and body sizes, tagged with method, route, and
        /// status code.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="route">Route template or path.</param>
        /// <param name="statusCode">HTTP response status code.</param>
        /// <param name="seconds">Elapsed seconds.</param>
        /// <param name="requestBytes">Request body size in bytes, or a negative value if unknown.</param>
        /// <param name="responseBytes">Response body size in bytes, or a negative value if unknown.</param>
        public static void RecordHttp(string method, string route, int statusCode, double seconds, long requestBytes, long responseBytes)
        {
            TagList tags = new TagList
            {
                { TagHttpMethod, method },
                { TagHttpRoute, route },
                { TagHttpStatus, statusCode }
            };
            _HttpDuration.Record(seconds, tags);
            _HttpRequests.Add(1, tags);
            if (requestBytes >= 0) _HttpRequestSize.Record(requestBytes, tags);
            if (responseBytes >= 0) _HttpResponseSize.Record(responseBytes, tags);
        }

        #endregion

        #region Public-Methods-Protocols

        /// <summary>Record a completed S3 operation.</summary>
        public static void RecordS3(string operation, double seconds, bool ok)
        {
            TagList tags = new TagList { { TagOperation, operation }, { TagStatus, Status(ok) } };
            _S3Duration.Record(seconds, tags);
            _S3Operations.Add(1, tags);
        }

        /// <summary>Record a completed RESP command.</summary>
        public static void RecordResp(string command, double seconds, bool ok)
        {
            TagList tags = new TagList { { TagOperation, command }, { TagStatus, Status(ok) } };
            _RespDuration.Record(seconds, tags);
            _RespCommands.Add(1, tags);
        }

        /// <summary>Increment the open RESP connection gauge.</summary>
        public static void RespConnectionOpened()
        {
            _RespConnections.Add(1);
        }

        /// <summary>Decrement the open RESP connection gauge.</summary>
        public static void RespConnectionClosed()
        {
            _RespConnections.Add(-1);
        }

        /// <summary>Record a completed WebSocket operation.</summary>
        public static void RecordWs(string operation, double seconds, bool ok)
        {
            TagList tags = new TagList { { TagOperation, operation }, { TagStatus, Status(ok) } };
            _WsDuration.Record(seconds, tags);
            _WsOperations.Add(1, tags);
        }

        /// <summary>Count a received WebSocket message.</summary>
        public static void WsMessageReceived()
        {
            _WsMessages.Add(1);
        }

        /// <summary>Increment the open WebSocket connection gauge.</summary>
        public static void WsConnectionOpened()
        {
            _WsConnections.Add(1);
        }

        /// <summary>Decrement the open WebSocket connection gauge.</summary>
        public static void WsConnectionClosed()
        {
            _WsConnections.Add(-1);
        }

        /// <summary>Record a completed MCP tool call, tagged with tool name and transport.</summary>
        public static void RecordMcp(string tool, string transport, double seconds, bool ok)
        {
            TagList tags = new TagList
            {
                { TagOperation, tool },
                { TagTransport, transport },
                { TagStatus, Status(ok) }
            };
            _McpDuration.Record(seconds, tags);
            _McpCalls.Add(1, tags);
        }

        #endregion

        #region Public-Methods-Application

        /// <summary>Record a completed object operation (write, read, delete, head, metadata).</summary>
        public static void RecordObject(string operation, double seconds, bool ok)
        {
            TagList tags = new TagList { { TagOperation, operation }, { TagStatus, Status(ok) } };
            _ObjectDuration.Record(seconds, tags);
            _ObjectOperations.Add(1, tags);
        }

        /// <summary>Add to the object bytes-written counter.</summary>
        public static void AddObjectBytesWritten(long bytes)
        {
            if (bytes > 0) _ObjectBytesWritten.Add(bytes);
        }

        /// <summary>Add to the object bytes-read counter.</summary>
        public static void AddObjectBytesRead(long bytes)
        {
            if (bytes > 0) _ObjectBytesRead.Add(bytes);
        }

        /// <summary>Count an object read served from cache.</summary>
        public static void ObjectCacheHit()
        {
            _ObjectCacheHits.Add(1);
        }

        /// <summary>Count an object read that missed cache.</summary>
        public static void ObjectCacheMiss()
        {
            _ObjectCacheMisses.Add(1);
        }

        /// <summary>Record a completed container operation.</summary>
        public static void RecordContainer(string operation, double seconds, bool ok)
        {
            TagList tags = new TagList { { TagOperation, operation }, { TagStatus, Status(ok) } };
            _ContainerDuration.Record(seconds, tags);
            _ContainerOperations.Add(1, tags);
        }

        /// <summary>Record a completed multipart upload operation.</summary>
        public static void RecordMultipart(string operation, double seconds, bool ok)
        {
            TagList tags = new TagList { { TagOperation, operation }, { TagStatus, Status(ok) } };
            _MultipartDuration.Record(seconds, tags);
            _MultipartOperations.Add(1, tags);
        }

        /// <summary>Add to the multipart bytes counter.</summary>
        public static void AddMultipartBytes(long bytes)
        {
            if (bytes > 0) _MultipartBytes.Add(bytes);
        }

        /// <summary>Record a completed search or enumeration.</summary>
        public static void RecordSearch(string scope, double seconds, bool ok)
        {
            TagList tags = new TagList { { TagScope, scope }, { TagStatus, Status(ok) } };
            _SearchDuration.Record(seconds, tags);
            _SearchOperations.Add(1, tags);
        }

        #endregion

        #region Public-Methods-Storage

        /// <summary>Record a completed extent storage operation.</summary>
        public static void RecordStorage(string operation, double seconds, bool ok)
        {
            TagList tags = new TagList { { TagOperation, operation }, { TagStatus, Status(ok) } };
            _StorageDuration.Record(seconds, tags);
            _StorageOperations.Add(1, tags);
        }

        /// <summary>Add to the storage bytes-written counter.</summary>
        public static void AddStorageBytesWritten(long bytes)
        {
            if (bytes > 0) _StorageBytesWritten.Add(bytes);
        }

        /// <summary>Add to the storage bytes-read counter.</summary>
        public static void AddStorageBytesRead(long bytes)
        {
            if (bytes > 0) _StorageBytesRead.Add(bytes);
        }

        /// <summary>
        /// Register the callback the capacity gauges observe. Replaces any previous provider. Pass a callback
        /// that returns null when capacity is momentarily unavailable.
        /// </summary>
        /// <param name="provider">Capacity provider callback.</param>
        public static void SetStorageCapacityProvider(Func<StorageCapacitySnapshot?> provider)
        {
            _CapacityProvider = provider;
        }

        #endregion

        #region Public-Methods-Workflows

        /// <summary>Record a completed janitor maintenance pass.</summary>
        public static void RecordJanitorRun(double seconds, bool ok)
        {
            TagList tags = new TagList { { TagStatus, Status(ok) } };
            _JanitorDuration.Record(seconds, tags);
            _JanitorRuns.Add(1, tags);
        }

        /// <summary>Add to the janitor items counter for a named step.</summary>
        public static void AddJanitorItems(string step, long count)
        {
            if (count <= 0) return;
            _JanitorItems.Add(count, new TagList { { TagStep, step } });
        }

        /// <summary>Record a node heartbeat write.</summary>
        public static void RecordHeartbeat(bool ok)
        {
            _Heartbeats.Add(1, new TagList { { TagStatus, Status(ok) } });
        }

        /// <summary>Record a completed rehydration pass.</summary>
        public static void RecordRehydration(string mode, double seconds, bool ok)
        {
            TagList tags = new TagList { { TagOperation, mode }, { TagStatus, Status(ok) } };
            _RehydrationDuration.Record(seconds, tags);
            _RehydrationRuns.Add(1, tags);
        }

        /// <summary>Count a captured request-history record.</summary>
        public static void RequestHistoryCaptured()
        {
            _RequestHistoryCaptured.Add(1);
        }

        /// <summary>Count a dropped request-history record.</summary>
        public static void RequestHistoryDropped()
        {
            _RequestHistoryDropped.Add(1);
        }

        #endregion

        #region Private-Methods

        private static string Status(bool ok)
        {
            return ok ? StatusOk : StatusError;
        }

        private static long ObserveTotalCapacity()
        {
            StorageCapacitySnapshot? snapshot = ReadCapacity();
            return snapshot.HasValue ? snapshot.Value.TotalBytes : 0L;
        }

        private static long ObserveFreeCapacity()
        {
            StorageCapacitySnapshot? snapshot = ReadCapacity();
            return snapshot.HasValue ? snapshot.Value.FreeBytes : 0L;
        }

        private static long ObserveUsedCapacity()
        {
            StorageCapacitySnapshot? snapshot = ReadCapacity();
            return snapshot.HasValue ? snapshot.Value.UsedBytes : 0L;
        }

        private static StorageCapacitySnapshot? ReadCapacity()
        {
            Func<StorageCapacitySnapshot?>? provider = _CapacityProvider;
            if (provider == null) return null;
            try
            {
                return provider();
            }
            catch
            {
                // A gauge callback runs on the collection thread and must never throw.
                return null;
            }
        }

        #endregion
    }
}
