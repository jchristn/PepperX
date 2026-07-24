<div align="center">
  <img src="assets/logo.png" alt="PepperX" width="160" height="160">
</div>

# PepperX

A high-performance, horizontally scalable key-value store with rich metadata and immutable,
self-describing storage — reachable over five protocols at once.

PepperX is **backend infrastructure**. There is no authentication and no authorization: access
control belongs to the application in front of it. See [security](#security) before deploying.

```bash
cd docker && docker compose up -d
open http://localhost:3000     # dashboard, connect to http://localhost:8000
```

---

## What it is

An object is a **key**, a binary **payload**, and metadata in three forms:

| Form | Shape | Use |
|---|---|---|
| **Labels** | `["metric", "cpu"]` | Flat tags. Searchable. |
| **Tags** | `{"resolution": "1m"}` | Key-value pairs. Searchable. |
| **Object** | any JSON | Freeform, arbitrarily nested. Stored, returned, not field-searchable. |

Objects live in **containers**, which map one-to-one onto S3 buckets.

### Immutable extents

An object is stored as one **extent**: a self-describing file holding a header (key, labels, tags,
metadata object, checksum) followed by the payload. Extents are never modified. Writing to an
existing key creates a new extent and atomically repoints the key — last writer wins, and readers
already streaming the old extent finish safely.

### Dual persistence

PostgreSQL holds the metadata index that makes label and tag search fast. The extent files hold
everything needed to reconstruct that index. Lose the database entirely and
`POST /v1.0/admin/rehydrate` with `Mode: Rebuild` rebuilds it by reading extent headers off disk.
The database is an index, not the system of record.

### Five protocols, one dataset

| Protocol | Port | Completeness |
|---|---|---|
| [**REST**](REST_API.md) | 8000 | Everything. OpenAPI + Swagger UI included. |
| [**S3**](S3_API.md) | 8001 | Buckets, objects, tags. Works with the AWS CLI and SDKs. |
| [**WebSockets**](WEBSOCKETS_API.md) | 8002 | REST-equivalent operations over one connection. |
| [**MCP**](MCP_API.md) | 8003 / 8004 | 16 tools, for LLM agents. |
| [**RESP**](RESP_API.md) | 6379 | Redis string commands. Any Redis client works. |

One namespace. Write over RESP, read over S3, search over REST, inspect from an agent over MCP.

### Stateless nodes

Nodes hold no local state and coordinate only through PostgreSQL and shared extent storage. Add or
remove them freely behind a load balancer. Deletes coordinate cluster-wide: an extent is tombstoned,
in-flight read leases on every node are allowed to drain, and only then is the payload destroyed — so
a read that has begun always completes.

---

## Getting started

### Docker (everything)

```bash
cd docker
docker compose up -d
```

Brings up PostgreSQL, two PepperX nodes, and the dashboard. See [`docker/`](docker/) for the layout
and [`docker/factory/`](docker/factory/) for reset-and-seed scripts.

| | |
|---|---|
| Dashboard | http://localhost:3000 |
| node1 REST | http://localhost:8000 |
| node2 REST | http://localhost:8010 |
| Swagger UI | http://localhost:8000/swagger |

### From source

Requires the .NET 8 or .NET 10 SDK and a PostgreSQL instance.

```bash
docker compose -f docker/compose.test.yaml up -d --wait   # PostgreSQL on 5433
dotnet run --project src/PepperX.Server
```

The server reads `pepperx.json` from its working directory. Every setting is documented inline in
that file.

### First object

```bash
curl -X PUT http://localhost:8000/v1.0/containers \
  -H 'Content-Type: application/json' -d '{"Name":"telemetry"}'

curl -X PUT 'http://localhost:8000/v1.0/containers/telemetry/object?key=metrics/cpu.json' \
  -H 'Content-Type: application/json' \
  -H 'x-pepperx-labels: metric,cpu' \
  -H 'x-pepperx-tags: resolution=1m' \
  -d '{"cpu":0.42}'

curl 'http://localhost:8000/v1.0/containers/telemetry/object?key=metrics/cpu.json'

curl -X POST http://localhost:8000/v1.0/objects/enumerate \
  -H 'Content-Type: application/json' -d '{"Labels":["metric"]}'
```

---

## SDKs

| Language | Package | Protocols |
|---|---|---|
| C# | [`sdk/csharp`](sdk/csharp) | REST, WebSockets |
| Python | [`sdk/python`](sdk/python) | REST (sync + async), WebSockets |
| JavaScript / TypeScript | [`sdk/js`](sdk/js) | REST, WebSockets |

For S3, RESP, and MCP, use the standard client for that protocol — there is nothing PepperX-specific
to wrap. See [`sdk/README.md`](sdk/README.md).

```python
from pepperx import PepperXClient

with PepperXClient("http://localhost:8000") as client:
    client.create_container("telemetry")
    client.write_object("telemetry", "metrics/cpu.json", b'{"cpu":0.42}',
                        labels=["metric", "cpu"], tags={"resolution": "1m"})
    print(client.read_object("telemetry", "metrics/cpu.json").data)
```

---

## Dashboard

A React console for operating a node: containers and objects, cross-container metadata search,
capacity and cluster health, request history with a traffic chart, and an API explorer driven by the
node's own OpenAPI document.

Available in English, German, and Japanese, with pseudo-locales for layout and RTL testing. See
[`dashboard/`](dashboard/).

```bash
cd dashboard && npm install && npm run dev
```

---

## Security

There is no authentication anywhere in PepperX. This is a design decision, not an omission — it is
meant to run inside a trusted network behind a service that performs its own access control.

What that means concretely:

- **Every endpoint is reachable by anyone who can reach the port**, including `DELETE` on containers
  and the rehydration endpoint.
- **The S3 static credentials are not a security control.** They exist because most S3 clients refuse
  to send an unsigned request. Signatures are accepted, not verified.
- **Do not publish these ports to the internet.** Bind them to a private network, and put your own
  authenticated service in front.

The one thing PepperX does protect is credentials in its own configuration: `GET /v1.0/admin/settings`
deliberately omits the database password and S3 keys.

---

## Repository layout

| Path | Contents |
|---|---|
| [`src/`](src/) | .NET solution: core library, server host, test projects |
| [`sdk/`](sdk/) | C#, Python, and JavaScript SDKs |
| [`dashboard/`](dashboard/) | React admin dashboard |
| [`docker/`](docker/) | Dockerfiles, compose stack, factory reset scripts |
| [`postman/`](postman/) | Postman collection and environment |
| [`memory/`](memory/) | Project orientation notes ([overview](memory/pepperx-overview.md)) |

---

## Testing

Tests need PostgreSQL. Start the dockerized instance first:

```bash
docker compose -f docker/compose.test.yaml up -d --wait
```

All four runners execute the same [Touchstone](https://www.nuget.org/packages/Touchstone) descriptors,
so a test written once runs everywhere:

```bash
dotnet run --project src/Test.Automated                       # console runner
dotnet run --project src/Test.Automated -- --results out.json # + JSON export
dotnet test src/Test.Xunit                                    # xUnit
dotnet test src/Test.Nunit                                    # NUnit
```

Database-dependent suites are skipped rather than failed when PostgreSQL is unreachable. Override
connection details with `PEPPERX_TEST_DB_HOST`, `PEPPERX_TEST_DB_PORT`, `PEPPERX_TEST_DB_USER`,
`PEPPERX_TEST_DB_PASSWORD`, and `PEPPERX_TEST_DB_NAME`.

The dashboard has its own tests and two Playwright QA sweeps — see [`dashboard/qa/`](dashboard/qa/):

```bash
cd dashboard && npm test
```

---

## Performance

`Test.Performance` starts an in-process node (or targets a running one with `--target`) and runs a
gauntlet of workloads, reporting throughput and latency percentiles:

```bash
dotnet run --project src/Test.Performance -c Release
dotnet run --project src/Test.Performance -c Release -- \
  --duration 10 --concurrency 32 --object-size 65536 --results perf.json
dotnet run --project src/Test.Performance -c Release -- --workload read-heavy,search
```

Workloads: `write-small`, `read-heavy`, `mixed`, `search`, `replace-churn`, `delete-churn`, `s3-ops`,
`resp-ops`. The harness exits non-zero when the error rate exceeds one percent.

Indicative single-node numbers (developer laptop, 16 workers, 4 KiB objects, dockerized PostgreSQL —
your hardware will differ):

| Workload | Ops/s | p95 |
|---|---:|---:|
| read-heavy | ~870 | 31 ms |
| search (label + tag filter) | ~580 | 36 ms |
| mixed (70r / 20w / 10 search) | ~580 | 93 ms |
| replace-churn (16 writers, one key) | ~180 | 128 ms |
| write-small | ~160 | 145 ms |

Writes are slower than reads because every write is a durable extent plus a metadata transaction.
This harness is worth running against your own hardware: it found three production bugs during
development that no functional test caught.

---

## Documentation

| | |
|---|---|
| [`REST_API.md`](REST_API.md) | Native REST API — the complete surface |
| [`S3_API.md`](S3_API.md) | S3-compatible surface |
| [`RESP_API.md`](RESP_API.md) | Redis wire protocol |
| [`WEBSOCKETS_API.md`](WEBSOCKETS_API.md) | WebSocket envelopes and operations |
| [`MCP_API.md`](MCP_API.md) | MCP tools for agents |
| [`sdk/README.md`](sdk/README.md) | Client libraries |
| [`postman/`](postman/) | Postman collection |
| [`CHANGELOG.md`](CHANGELOG.md) | Release history |

---

## License

[MIT](LICENSE.md)
