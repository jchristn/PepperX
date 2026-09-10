# PepperX Telemetry

PepperX is fully instrumented for observability. Every protocol surface (REST, S3, RESP, WebSockets,
MCP), every application-layer service, the extent storage layer, the metadata database, and every
background workflow emit **metrics**, **traces**, and **logs** through the .NET base class library
(`System.Diagnostics.Metrics.Meter`, `System.Diagnostics.ActivitySource`, and
`Microsoft.Extensions.Logging.ILogger`).

Emit rides the BCL and is a **cheap no-op until a host subscribes**. PepperX hosts the pipeline with
the [Radiant](https://www.nuget.org/packages/Radiant) telemetry SDK, which turns the settings below
into a wired OpenTelemetry pipeline that pushes **OTLP** to a collector. Point it at the bundled
observability stack, at your own collector, or turn it off entirely.

- [At a glance](#at-a-glance)
- [Enabling telemetry](#enabling-telemetry)
- [The bundled observability stack](#the-bundled-observability-stack)
- [What is emitted](#what-is-emitted)
  - [Metrics](#metrics)
  - [Traces](#traces)
  - [Logs](#logs)
- [Connecting to your own stack](#connecting-to-your-own-observability-stack)
- [Grafana dashboards](#grafana-dashboards)
- [Notes for a DevOps / SRE team](#notes-for-a-devops--sre-team)
- [Troubleshooting](#troubleshooting)

---

## At a glance

```
   PepperX node(s)                     OpenTelemetry Collector                Backends            Grafana
 ┌────────────────┐                   ┌───────────────────────┐          ┌───────────────┐    ┌──────────┐
 │ Meter          │  metrics ─┐       │ receivers: otlp        │  metrics │ Prometheus    │◄───┤          │
 │ ActivitySource │  traces ──┼─OTLP─►│ processors: batch      │─────────►│ (scrape :8889)│    │  reads   │
 │ ILogger        │  logs ────┘ gRPC  │ exporters: prom/tempo/ │  traces  │ Tempo         │◄───┤  all 3   │
 │ (+ Npgsql,     │       :4317       │           loki         │─────────►│               │    │  sources │
 │  runtime)      │                   └───────────────────────┘   logs   │ Loki          │◄───┤          │
 └────────────────┘                                              ────────►└───────────────┘    └──────────┘
```

Three pillars, one egress (OTLP to the collector), fanned out to Prometheus (metrics), Tempo (traces),
and Loki (logs), all visualized in Grafana.

- **Service name** (`service.name`): `PepperX`
- **Service instance** (`service.instance.id`): the node id (`Cluster.NodeId`, or the machine name)
- **Meter / ActivitySource name**: `PepperX` (plus `Npgsql` for database instrumentation)

---

## Enabling telemetry

Telemetry is **disabled by default**. A bare process with no collector must not pay for a background
exporter repeatedly failing to reach a dead endpoint, nor stall on shutdown flushing to nowhere. Turn
it on wherever you have a collector to receive it.

### Settings (`pepperx.json` → `Telemetry`)

```jsonc
{
  "Telemetry": {
    "Enabled": false,                         // master switch
    "OtlpEndpoint": "http://localhost:4317",  // collector endpoint
    "OtlpProtocol": "grpc",                   // "grpc" (4317) or "http" (4318, OTLP/HTTP protobuf)
    "Metrics": true,                          // export the metrics pillar
    "Traces": true,                           // export the traces pillar
    "Logs": true,                             // export the logs pillar (with trace correlation)
    "SamplingRatio": 1.0,                     // head-based trace sampling, 0.0–1.0
    "IncludeRuntimeMetrics": true,            // .NET GC/heap/threads/JIT
    "IncludeProcessMetrics": true,            // working set, uptime, thread count
    "ExportIntervalMs": 15000,                // OTLP metric export cadence
    "PrometheusEnabled": false,               // also serve an in-process /metrics scrape endpoint
    "PrometheusPort": 9464
  }
}
```

### Environment variables (override the file at startup)

| Variable | Effect |
|---|---|
| `PEPPERX_TELEMETRY_ENABLED` | `true`/`1`/`yes`/`on` enables the pipeline |
| `PEPPERX_TELEMETRY_OTLP_ENDPOINT` | OTLP collector endpoint (e.g. `http://otel-collector:4317`) |
| `PEPPERX_TELEMETRY_OTLP_PROTOCOL` | `grpc` or `http` |

Environment variables win over the JSON file. This is how the docker stack enables telemetry without
editing the committed node config — see `docker/compose.yaml`.

---

## The bundled observability stack

`docker/compose.yaml` ships a complete **LGTM** stack (Loki, Grafana, Tempo, Metrics) plus an
OpenTelemetry Collector. Both PepperX nodes are pre-configured (via environment variables) to push OTLP
to the collector.

```bash
cd docker
docker compose up -d
# open Grafana:
open http://localhost:3001      # login admin / admin
```

### Services and ports

| Service | Container | Host URL | Default credentials | Purpose |
|---|---|---|---|---|
| **Grafana** | `pepperx-grafana` | http://localhost:3001 | `admin` / `admin` | Dashboards & exploration (the main UI) |
| **Prometheus** | `pepperx-prometheus` | http://localhost:9090 | none | Metrics store & query |
| **Tempo** | `pepperx-tempo` | http://localhost:3200 | none | Trace store (query via Grafana) |
| **Loki** | `pepperx-loki` | http://localhost:3100 | none | Log store (query via Grafana) |
| **OTel Collector** | `pepperx-otel-collector` | grpc `:4317`, http `:4318` | none | OTLP ingest; fans out to the three backends |

Ports were chosen to avoid every port the data plane already uses (`5442` Postgres; `8000-8004` /
`6379` node1; `8010-8014` / `6380` node2; `3000` dashboard). **Grafana is on `3001`, not its default
`3000`, because the PepperX dashboard already owns `3000`.** The same links, with their credentials, are
listed on the dashboard's **Observability** page.

> **Security:** like the rest of the PepperX docker stack, this observability stack has **no real
> authentication** and is intended for local or trusted-network use. Change the Grafana credentials and
> put everything behind your own access control before exposing any of it.

---

## What is emitted

### Metrics

Metrics are recorded on the `PepperX` meter as BCL instruments. Through the collector's Prometheus
exporter they are exposed with **`add_metric_suffixes: false`**, so the Prometheus name is simply the
OpenTelemetry instrument name with dots turned into underscores — **counters do *not* get a `_total`
suffix**, and histograms expose the usual `_bucket` / `_sum` / `_count` series. Instrument tags become
Prometheus labels (dots → underscores). The resource attributes `service_name` and
`service_instance_id` are attached to every series.

Latency histograms are in **seconds**; size counters/histograms are in **bytes**.

#### HTTP / REST (`pepperx_http_*`)
| Metric | Type | Labels |
|---|---|---|
| `pepperx_http_server_request_duration` | histogram (s) | `http_request_method`, `http_route`, `http_response_status_code` |
| `pepperx_http_server_requests` | counter | same |
| `pepperx_http_server_active_requests` | up/down gauge | — |
| `pepperx_http_server_request_body_size` | histogram (bytes) | same |
| `pepperx_http_server_response_body_size` | histogram (bytes) | same |

`http_route` is normalized to a template (`/v1.0/containers/{container}/object`) to keep label
cardinality bounded.

#### S3 (`pepperx_s3_*`), RESP (`pepperx_resp_*`), WebSockets (`pepperx_ws_*`), MCP (`pepperx_mcp_*`)
| Metric | Type | Labels |
|---|---|---|
| `pepperx_s3_operation_duration` / `pepperx_s3_operations` | histogram / counter | `pepperx_operation`, `pepperx_status` |
| `pepperx_resp_command_duration` / `pepperx_resp_commands` | histogram / counter | `pepperx_operation`, `pepperx_status` |
| `pepperx_resp_connections` | up/down gauge | — |
| `pepperx_ws_operation_duration` / `pepperx_ws_operations` | histogram / counter | `pepperx_operation`, `pepperx_status` |
| `pepperx_ws_messages` | counter | — |
| `pepperx_ws_connections` | up/down gauge | — |
| `pepperx_mcp_tool_duration` / `pepperx_mcp_tool_calls` | histogram / counter | `pepperx_operation`, `pepperx_transport`, `pepperx_status` |

`pepperx_status` is `ok` or `error`; `pepperx_transport` is `http` or `tcp` (MCP).

#### Application layer — objects, containers, multipart, search
| Metric | Type | Labels |
|---|---|---|
| `pepperx_object_operation_duration` / `pepperx_object_operations` | histogram / counter | `pepperx_operation` (`write`,`read`,`read_metadata`,`update_metadata`,`delete`,`exists`,`bulk_delete`), `pepperx_status` |
| `pepperx_object_bytes_written` / `pepperx_object_bytes_read` | counter (bytes) | — |
| `pepperx_object_cache_hits` / `pepperx_object_cache_misses` | counter | — |
| `pepperx_container_operation_duration` / `pepperx_container_operations` | histogram / counter | `pepperx_operation`, `pepperx_status` |
| `pepperx_multipart_operation_duration` / `pepperx_multipart_operations` | histogram / counter | `pepperx_operation`, `pepperx_status` |
| `pepperx_multipart_bytes` | counter (bytes) | — |
| `pepperx_search_duration` / `pepperx_search_operations` | histogram / counter | `pepperx_scope` (`container`/`all`), `pepperx_status` |

#### Storage (physical extent I/O)
| Metric | Type | Labels |
|---|---|---|
| `pepperx_storage_operation_duration` / `pepperx_storage_operations` | histogram / counter | `pepperx_operation`, `pepperx_status` |
| `pepperx_storage_bytes_written` / `pepperx_storage_bytes_read` | counter (bytes) | — |
| `pepperx_storage_capacity_total_bytes` / `pepperx_storage_capacity_free_bytes` / `pepperx_storage_capacity_used_bytes` | gauge (bytes) | — |

#### Background workflows
| Metric | Type | Labels |
|---|---|---|
| `pepperx_janitor_run_duration` / `pepperx_janitor_runs` | histogram / counter | `pepperx_status` |
| `pepperx_janitor_items` | counter | `pepperx_step` (`leases`,`temp_files`,`request_history`, …) |
| `pepperx_heartbeat_beats` | counter | `pepperx_status` |
| `pepperx_rehydration_duration` / `pepperx_rehydration_runs` | histogram / counter | `pepperx_operation` (mode), `pepperx_status` |
| `pepperx_request_history_captured` / `pepperx_request_history_dropped` | counter | — |

#### Runtime & database (subscribed instrumentation)
- **.NET runtime** (`OpenTelemetry.Instrumentation.Runtime`): GC collections/heap/allocations, thread
  pool, lock contention, exceptions — `process_runtime_dotnet_*`.
- **Process** (Radiant): `process_memory_usage`, `process_uptime`, `process_thread_count`.
- **PostgreSQL** (Npgsql's built-in OTel instrumentation, meter `Npgsql`): connection pool and command
  metrics (`db_client_*`), plus per-command **spans** in Tempo.

### Traces

A **server span** is opened at the edge of every protocol surface, tagged with `pepperx.protocol`,
`pepperx.operation`, and (MCP) `pepperx.transport`; exceptions set the span status to error and record
`exception.*`. Nested under it are **internal spans** for each service call (`object.write`,
`container.create`, `search.container`, `janitor.run`, …), storage operations (`storage.write`,
`storage.read`, …), and — via Npgsql — the actual SQL commands. The result is an end-to-end trace from
the protocol request through the service layer, storage, and database.

Span names: `rest <METHOD>`, `s3 <Operation>`, `resp <COMMAND>`, `ws <Operation>`, `mcp <tool>`, and
dot-cased internal names (`object.read`, `multipart.complete`, `rehydration.run`, …). W3C trace context
is propagated; sampling is head-based via `SamplingRatio`.

### Logs

PepperX's log records are shipped over OTLP to Loki with **trace correlation** — each record carries
the active `trace_id` / `span_id`, so in Grafana you can pivot from a trace to its logs and back. Query
logs in Grafana with `{service_name="PepperX"}`.

---

## Connecting to your own observability stack

You do **not** need the bundled stack. The node speaks OTLP; point it anywhere.

### Push to an existing OpenTelemetry Collector

```bash
PEPPERX_TELEMETRY_ENABLED=true \
PEPPERX_TELEMETRY_OTLP_ENDPOINT=http://your-collector.internal:4317 \
PEPPERX_TELEMETRY_OTLP_PROTOCOL=grpc
```

or OTLP/HTTP:

```bash
PEPPERX_TELEMETRY_OTLP_ENDPOINT=http://your-collector.internal:4318
PEPPERX_TELEMETRY_OTLP_PROTOCOL=http
```

Most vendors (Grafana Cloud, Honeycomb, Datadog, New Relic, Dynatrace, Jaeger, …) accept OTLP directly
or via the collector. Route the collector's exporters to your backends of choice; the PepperX side does
not change.

### Let Prometheus scrape the node directly (no collector)

Set `Telemetry.PrometheusEnabled: true` (and a free `PrometheusPort`, default `9464`). The node then
serves an in-process `/metrics` endpoint; add a scrape job:

```yaml
scrape_configs:
  - job_name: pepperx
    static_configs:
      - targets: ["node1:9464", "node2:9464"]
```

Traces and logs still require an OTLP endpoint (or disable those pillars). One node per host may bind a
given scrape port.

### Customize the bundled collector

`docker/otel-collector/config.yaml` is the fan-out point. Add exporters (e.g. `otlphttp` to a vendor,
`prometheusremotewrite` to remote Prometheus) and wire them into the `metrics` / `traces` / `logs`
pipelines. The other config files: `docker/prometheus/prometheus.yml`, `docker/tempo/tempo.yaml`,
`docker/loki/loki.yaml`, and Grafana provisioning under `docker/grafana/provisioning/`.

### Additional resource attributes

Radiant stamps `service.name` and `service.instance.id`. To add `deployment.environment`, `service.
version`, region, etc., set the standard OpenTelemetry env var — the collector and backends honor it:

```bash
OTEL_RESOURCE_ATTRIBUTES=deployment.environment=prod,service.version=0.1.0,cloud.region=us-east-1
```

---

## Grafana dashboards

Dashboards are **provisioned automatically** into a single top-level Grafana folder named **`PepperX`**
(no subfolders — dashboards are separated by domain via their titles):

| Dashboard | Covers |
|---|---|
| `PepperX - Overview` | Cross-protocol request/error/latency, connections, storage, throughput |
| `PepperX - HTTP` | REST: rate by method/route, 4xx/5xx, p50/p95/p99, body sizes |
| `PepperX - S3` | S3 operations, errors, latency |
| `PepperX - RESP` | Redis commands, connections, latency |
| `PepperX - WebSockets` | Operations, messages, connections |
| `PepperX - MCP` | Tool calls by tool & transport, latency |
| `PepperX - Storage` | Capacity, extent I/O rate & latency, throughput |
| `PepperX - Services` | Object/container/multipart/search ops, cache hit ratio |
| `PepperX - Workflows` | Janitor, heartbeat, rehydration, request-history |
| `PepperX - Database` | Npgsql connection/command metrics + DB spans (Tempo) |
| `PepperX - Runtime` | GC, heap, threads, allocations, exceptions, uptime |
| `PepperX - Logs` | Live log stream and volume (Loki) |

A dashboard variable `instance` filters by `service_instance_id` (node). To add your own, drop JSON into
`docker/grafana/dashboards/` — it is picked up within 30s.

---

## Notes for a DevOps / SRE team

- **Cardinality.** Labels are deliberately low-cardinality: `pepperx_operation` is a bounded operation
  name, `http_route` is templated (container names and keys are *not* in labels — they appear on spans
  instead). Safe to keep at full resolution in Prometheus.
- **Sampling.** Metrics and logs are unsampled. Traces are head-sampled by `SamplingRatio` (default
  `1.0`). For high-traffic production, lower it (e.g. `0.05`) or add tail sampling in the collector.
- **Cost of "off".** When `Enabled=false` the instruments are inert no-ops; there is no measurable
  overhead and nothing is exported.
- **Graceful degradation.** If the collector is unreachable, the OTLP exporter retries in the background
  and drops on timeout; the data plane is never blocked. Telemetry is observability, not a hard
  dependency — a failure to start the pipeline is logged and swallowed.
- **Shutdown.** On stop the node flushes exporters (bounded timeout) so the last window of data is not
  lost.
- **Scaling.** Every series carries `service_instance_id`, so multi-node clusters aggregate cleanly
  (`sum by (pepperx_operation)`) or split per node (`... by (service_instance_id)`). The bundled stack
  scales to a handful of nodes on one collector; for larger fleets run the collector as an agent per
  host or a gateway tier and push to durable backends.
- **Retention.** Bundled defaults: Prometheus 15d, Tempo 48h, Loki 168h — all on local docker volumes.
  Raise them and move to real object storage for anything beyond evaluation.
- **Metric naming stability.** `add_metric_suffixes: false` is what keeps the names in this document
  stable (no `_total`/unit suffixes). If you re-enable suffixes in the collector, counters become
  `..._total` and dashboards/alerts must be updated to match.
- **Alerting.** Good SLO signals: HTTP 5xx ratio, p99 of `pepperx_http_server_request_duration`,
  `pepperx_storage_capacity_free_bytes`, `pepperx_janitor_runs{pepperx_status="error"}`,
  `pepperx_heartbeat_beats{pepperx_status="error"}`, and `rate(pepperx_request_history_dropped)`.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| No data in Grafana | Is `Telemetry.Enabled` (or `PEPPERX_TELEMETRY_ENABLED`) true? Does `OtlpEndpoint` resolve from *inside* the node's network (`otel-collector:4317`, not `localhost`)? |
| Metrics but no traces/logs | `Traces` / `Logs` enabled? Collector `traces`/`logs` pipelines wired? For logs, query `{service_name="PepperX"}` in Loki. |
| Some dashboard panels empty | Runtime/Npgsql metric names vary by library version; those panels populate once the underlying instrumentation emits. The `PepperX - *` panels use the exact names above. |
| Slow shutdown when telemetry on | Expected if the collector is unreachable — the flush waits a bounded time. Point at a live collector or disable telemetry. |
| Port already in use | The stack avoids the data-plane ports; if you changed node ports, re-check against the [ports table](#services-and-ports). Grafana is `3001`, not `3000`. |

See also: `docker/README.md` for the stack, and `pepperx.json` for the full settings surface.
