<div align="center">

  <img src="assets/logo.png" alt="PepperX" width="150" height="150">

  # PepperX

  **A protocol-agnostic storage backend: durable, recoverable key-value storage with extensive, searchable metadata, reachable over REST, S3, Redis RESP, WebSockets, and MCP from one namespace.**

  [![Status](https://img.shields.io/badge/status-alpha-orange)](#status)
  [![Version](https://img.shields.io/badge/version-0.1.0-blue)](CHANGELOG.md)
  [![License](https://img.shields.io/badge/license-MIT-green)](LICENSE.md)
  [![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

</div>

---

## Status

**Alpha, v0.1.0.** Everything documented here works and is covered by tests — 165 backend tests
across three runners (console, xUnit, NUnit), on .NET 8 and .NET 10 — but it has not been run in
production. Interfaces and the on-disk extent format may change before 1.0. Pin your versions.

---

## What it is

PepperX is a key-value store where the value is **arbitrary bytes** and every object carries
**extensive metadata** you can search on and extend. Metadata comes in three forms — a flat list of
labels, key/value tags, and a freeform JSON object. Labels and tags give you fine-grained filtering;
the JSON object gives you room to categorize and use the data however your application needs, without
a schema change.

| Form | Example | Searchable |
|---|---|:---:|
| **Labels** | `["invoice", "2026", "paid"]` | yes |
| **Tags** | `{"customer": "acme", "region": "us-west"}` | yes |
| **Object** | any JSON, arbitrarily nested | stored & returned |

Objects live in **containers**, which map one-to-one onto S3 buckets. The same data is reachable over
five protocols at once, against one namespace with no synchronization between them. Which protocol a
client uses is a property of the client, not a copy of the data.

| Protocol | Port | Use it for |
|---|---|---|
| [**REST**](REST_API.md) | 8000 | The complete surface. Application backends, admin tooling, and anything that speaks HTTP and JSON. Ships an OpenAPI document and Swagger UI. |
| [**S3**](S3_API.md) | 8001 | Drop-in object storage for anything already built on S3 — data pipelines, backup and archive targets, existing S3 applications — using the AWS CLI and SDKs unchanged, including multipart upload for large objects. |
| [**Redis RESP**](RESP_API.md) | 6379 | Durable key/value for code that already speaks Redis, when you want persistence and metadata search rather than an in-memory cache. |
| [**WebSockets**](WEBSOCKETS_API.md) | 8002 | REST-equivalent operations over one long-lived connection, for services doing high request volume without per-call HTTP overhead. |
| [**MCP**](MCP_API.md) | 8003 / 8004 | Direct tool access for LLM agents: store, read, and search as discoverable tool calls, each with a full JSON Schema. |

---

## Why you'd use it

**A single backend for many kinds of client.** One namespace answers to five protocols at once, so an
application backend on HTTP, a data pipeline on S3, a service that speaks Redis, and an LLM agent on
MCP all read and write the same objects. You add a protocol by pointing a different client at the same
node, not by standing up another store and keeping it in sync.

**A durable layer for applications that are growing.** Extent files are the system of record. Each one
carries its key, labels, tags, metadata, and a SHA-256 checksum in a header ahead of the payload;
PostgreSQL indexes that for search but holds nothing you cannot regenerate. Capacity and throughput
grow by adding stateless nodes and storage behind them. Nodes keep no local state, so another one is
interchangeable with the rest behind a load balancer — no leader election, no resharding.

**Recoverable storage for data you cannot afford to lose.** Because every extent is self-describing,
the metadata database is rebuildable from storage alone: one API call reads the extents and
reconstructs the index, and that path is tested rather than assumed. Writes are immutable and
atomically repointed, deletes drain in-flight reads across the cluster before destroying anything, and
every payload is checksummed on write and optionally verified on read.

**Fine-grained search and extensibility over your data.** Labels and tags let you filter by what an
object is — `["metric","cpu"]`, `{"host":"web-01"}` — within a container or across all of them. The
freeform JSON object travels with each object for anything the flat metadata does not capture, so you
can categorize and evolve how you use the data without touching a schema.

**The clients you already run.** Speaking S3 and RESP means Python, Go, Java, and .NET reach PepperX
through libraries they already depend on. There is nothing PepperX-specific to adopt for those
surfaces, and there are first-party [SDKs](sdk/) for C#, Python, and JavaScript when you want typed
REST and WebSocket clients.

**A backend to build a platform on.** A larger storage or data platform — protocol front ends,
tenancy, policy, placement — can sit on top of one recoverable namespace instead of reimplementing
durable storage for each protocol it exposes. An enterprise consolidating a fragmented backend can put
the same substrate underneath its existing services. PepperX owns the storage, metadata, and recovery;
the layer above owns access control and business logic.

### What it deliberately isn't

- **Not authenticated.** PepperX is backend infrastructure meant to sit behind a service that does its
  own access control. See [Security](#security).
- **Not a Redis replacement.** The RESP surface is durable storage, not an in-memory cache. It is far
  slower than Redis and always will be.
- **Not a full S3.** Buckets, objects, tags, and multipart uploads (including `UploadPartCopy`). No
  versioning, ACLs, or lifecycle rules.

---

## Capabilities

| | |
|---|---|
| **Immutable extents** | Writes never mutate. A replace writes a new extent and atomically repoints the key, so readers already streaming the old one finish safely. |
| **Metadata search** | Filter by label, tag, key prefix, suffix, and creation window — within a container or across all of them. |
| **Rehydration** | `POST /v1.0/admin/rehydrate` verifies, repairs, or fully rebuilds the metadata database from raw storage. |
| **Safe deletes** | Deletes tombstone, drain in-flight read leases cluster-wide, then destroy. A read that has begun always completes. |
| **Per-container caching** | An optional in-memory read/write-through cache per container (FIFO or LRU, size- and memory-bounded), on by default. Hits are served from memory and stay coherent across nodes via the active-extent check. |
| **Checksums** | SHA-256 on every payload, returned on every read, optionally verified on read. |
| **Observability** | Every request captured with timing, headers, and bodies, plus time-bucketed traffic summaries. |
| **Admin dashboard** | React console in six languages (English, Spanish, French, German, Chinese, Japanese), with light and dark themes. |

---

## Getting started

PepperX is a multi-service platform — PostgreSQL, one or more stateless nodes, shared extent storage,
and the dashboard. Deploy it with the [Docker](https://docs.docker.com/get-docker/) environment in
this repository, which wires those pieces together the way the platform expects. The composition,
networking, and storage layout are part of how PepperX behaves, so Docker is the supported path;
running the server binary against your own PostgreSQL is only appropriate for reading the code.

```bash
git clone https://github.com/jchristn/PepperX.git
cd PepperX/docker
docker compose up -d
```

That brings up PostgreSQL, **two** PepperX nodes, and the dashboard — two nodes because a single-node
stack hides the mistakes that only appear in a cluster.

| | |
|---|---|
| **Dashboard** | http://localhost:3000 — connect it to `http://localhost:8000` |
| node1 REST | http://localhost:8000 · Swagger UI at `/swagger` |
| node2 REST | http://localhost:8010 |

Want sample data to look at? `./factory/reset.sh` (or `reset.bat`) rebuilds the stack from scratch and
seeds containers, objects, and traffic.

### Store and find something

```bash
curl -X PUT http://localhost:8000/v1.0/containers \
  -H 'Content-Type: application/json' -d '{"Name":"telemetry"}'

curl -X PUT 'http://localhost:8000/v1.0/containers/telemetry/object?key=metrics/cpu.json' \
  -H 'Content-Type: application/json' \
  -H 'x-pepperx-labels: metric,cpu' \
  -H 'x-pepperx-tags: resolution=1m&host=web-01' \
  -d '{"cpu":0.42}'

# Find it by metadata, across every container
curl -X POST http://localhost:8000/v1.0/objects/enumerate \
  -H 'Content-Type: application/json' \
  -d '{"Labels":["metric"],"Tags":{"host":"web-01"}}'
```

The same object over the other protocols:

```bash
aws --endpoint-url http://localhost:8001 s3 ls s3://telemetry/
redis-cli -p 6379 SET greeting "hello"        # lands in container resp0
```

### Using an SDK

```python
from pepperx import PepperXClient

with PepperXClient("http://localhost:8000") as client:
    client.create_container("telemetry")
    client.write_object("telemetry", "metrics/cpu.json", b'{"cpu":0.42}',
                        labels=["metric", "cpu"], tags={"resolution": "1m"})
    print(client.read_object("telemetry", "metrics/cpu.json").data)
```

The C#, Python, and JavaScript clients cover REST and WebSockets — see [`sdk/`](sdk/). For S3, RESP,
and MCP, use the standard client for that protocol; there is nothing PepperX-specific to wrap.

### Building the images

`docker/compose.yaml` pulls published images. To build and push your own:

```bat
build-all.bat v0.1.0
```

This produces multi-architecture images — `linux/amd64` and `linux/arm64/v8` — for
`jchristn77/pepperx-server` and `jchristn77/pepperx-dashboard`, tagged with the version and `latest`.
`build-server.bat` and `build-dashboard.bat` do one each. See [`docker/`](docker/) for building a
single-architecture image locally.

---

## Using the dashboard

Open **http://localhost:3000** and enter a node's REST endpoint (`http://localhost:8000`). There is no
login — PepperX is unauthenticated, so the connect screen only establishes *which node* you are
operating. That address stays visible in the header, because with several interchangeable nodes it is
the only thing telling you where a destructive action will land.

| View | What it's for |
|---|---|
| **Home** | Node state at a glance: counts, storage, traffic over time, and anything needing attention. |
| **Containers** | Create, tag, browse, and delete. Deleting a non-empty container makes you type its name. |
| **Objects** | Upload with labels, tags, and freeform JSON; inspect metadata and checksums; filter by prefix, label, or tag. |
| **Search** | The same filters across every container at once. |
| **Capacity** | Where storage is going, per container, and which cluster nodes are alive. Rehydration lives here. |
| **Request History** | Every request served, with full detail. Click a bar in the chart to filter to that moment. |
| **API Explorer** | Run any endpoint against the live node. Operations come from the node's own OpenAPI document, so it cannot drift. |
| **Settings** | How this node is configured and where each protocol is listening. |

Theme and language are in the header. First run against an empty node offers a short setup path; you
can relaunch it from Settings.

---

## Security

**There is no authentication anywhere in PepperX.** This is a design decision, not an omission — it is
meant to run inside a trusted network behind a service that performs its own access control.

- Every endpoint is reachable by anyone who can reach the port, including container deletion and
  rehydration.
- The S3 static credentials are **not** a security control. They exist because most S3 clients refuse
  to send an unsigned request; signatures are accepted, not verified.
- **Do not publish these ports to the internet.** Bind them to a private network and put your own
  authenticated service in front.

The one thing PepperX does protect is credentials in its own configuration:
`GET /v1.0/admin/settings` deliberately omits the database password and S3 keys.

---

## Documentation

| | |
|---|---|
| [REST API](REST_API.md) | The complete surface |
| [S3](S3_API.md) · [RESP](RESP_API.md) · [WebSockets](WEBSOCKETS_API.md) · [MCP](MCP_API.md) | Per-protocol references |
| [`sdk/`](sdk/) | C#, Python, and JavaScript clients |
| [`docker/`](docker/) | Compose stack, images, factory reset |
| [`dashboard/`](dashboard/) | Console architecture and conventions |
| [`postman/`](postman/) | Postman collection and environment |
| [CHANGELOG](CHANGELOG.md) | Release history |

A running node always serves its own contract at `/openapi.json`, with Swagger UI at `/swagger`.

---

## Testing

```bash
docker compose -f docker/compose.test.yaml up -d --wait

dotnet run --project src/Test.Automated   # console runner
dotnet test src/Test.Xunit                # xUnit
dotnet test src/Test.Nunit                # NUnit
```

All three execute the same [Touchstone](https://www.nuget.org/packages/Touchstone) descriptors, so a
test written once runs everywhere. Database-dependent suites skip rather than fail when PostgreSQL is
unreachable.

`Test.Performance` runs a workload gauntlet reporting throughput and latency percentiles — it found
three real bugs during development that no functional test caught.

---

## Contributing

### Filing an issue

Bugs and feature requests: **https://github.com/jchristn/PepperX/issues**

A good report includes the PepperX version (`GET /` returns it), which protocol you were using, what
you expected, and what happened. If a node is involved, `GET /v1.0/admin/settings` gives a
credential-free view of its configuration that is usually the fastest way to see the problem.

### Starting a discussion

Questions, ideas, and "is this the right tool for X" belong in
**https://github.com/jchristn/PepperX/discussions** rather than the issue tracker. Design feedback is
especially welcome while the interfaces are still moving.

### Pull requests

Run the test suite and `dotnet build -c Release` (warnings are errors) before opening one. Dashboard
changes should also pass `npm test` and `npm run lint`.

---

## License

[MIT](LICENSE.md) — © 2026 Joel Christner
