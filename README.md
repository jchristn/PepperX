<div align="center">

  <img src="assets/logo.png" alt="PepperX" width="150" height="150">

  # PepperX

  **PepperX is a backend storage platform enabling scalable metadata and data storage via REST, WebSockets, Redis RESP, and MCP.**

  [![Status](https://img.shields.io/badge/status-alpha-orange)](#status)
  [![Version](https://img.shields.io/badge/version-0.1.0-blue)](CHANGELOG.md)
  [![License](https://img.shields.io/badge/license-MIT-green)](LICENSE.md)
  [![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

</div>

---

## Status

**Alpha, v0.1.0.** Everything documented here works and is covered by tests — 115 backend tests
across three runners, on .NET 8 and .NET 10 — but this has not been run in production. Interfaces
and the on-disk extent format may change before 1.0. Pin your versions.

---

## What it is

A key-value store where the value is **arbitrary bytes** and the key carries **metadata worth
searching on**.

Every object is a key, a binary payload, and metadata in three forms:

| Form | Example | Searchable |
|---|---|:---:|
| **Labels** | `["invoice", "2026", "paid"]` | yes |
| **Tags** | `{"customer": "acme", "region": "us-west"}` | yes |
| **Object** | any JSON, arbitrarily nested | stored & returned |

Objects live in **containers**, which map one-to-one onto S3 buckets.

The same data is reachable over five protocols at once. Write a value with `redis-cli`, read it with
the AWS CLI, search it over REST, and let an LLM agent inspect it over MCP — one namespace, no
synchronization.

| Protocol | Port | Coverage |
|---|---|---|
| [**REST**](REST_API.md) | 8000 | Everything. OpenAPI document + Swagger UI included. |
| [**S3**](S3_API.md) | 8001 | Buckets, objects, tags. Works with the AWS CLI and SDKs unchanged. |
| [**WebSockets**](WEBSOCKETS_API.md) | 8002 | REST-equivalent operations over one connection. |
| [**MCP**](MCP_API.md) | 8003 / 8004 | 16 tools with full JSON Schemas, for LLM agents. |
| [**RESP**](RESP_API.md) | 6379 | Redis string commands. Any Redis client works. |

---

## Why you'd use it

**You have blobs, and you need to find them by what they are.** Object stores give you a key and a
prefix. Databases give you rich queries but are a poor fit for payloads. PepperX gives you both:
store the bytes, attach labels and tags, and query across every container by metadata.

**Your clients already exist.** Speaking S3 and RESP means Python, Go, Java, Rust, and .NET can talk
to PepperX today using libraries they already depend on. No SDK adoption required — though there are
[first-party SDKs](sdk/) for C#, Python, and JavaScript if you want typed clients.

**Your agents can use it directly.** The MCP surface is not an afterthought: every tool publishes a
complete JSON Schema with per-argument descriptions, so an agent discovers how to store and search
from `tools/list` alone.

**Losing the database is not losing the data.** Extents are self-describing — every file carries its
own key, labels, tags, metadata, and checksum in a header ahead of the payload. PostgreSQL is an
*index*, not the system of record. Drop it entirely and one API call rebuilds it by reading storage.
This is tested, not aspirational.

**Scaling out is adding a process.** Nodes hold no local state. Point another one at the same
database and the same extent storage, put a load balancer in front, and it's interchangeable with the
others — no leader election, no resharding.

### What it deliberately isn't

- **Not authenticated.** PepperX is backend infrastructure meant to sit behind a service that does
  its own access control. See [Security](#security) — this matters.
- **Not a Redis replacement.** The RESP surface is durable storage, not an in-memory cache. It is far
  slower than Redis and always will be.
- **Not a full S3.** Buckets, objects, and tags only. No multipart upload, versioning, ACLs, or
  lifecycle rules.

---

## Benefits at a glance

| | |
|---|---|
| **Immutable extents** | Writes never mutate. A replace writes a new extent and atomically repoints the key, so readers already streaming the old one finish safely. |
| **Metadata search** | Filter by label, tag, key prefix, suffix, and creation window — within a container or across all of them. |
| **Rehydration** | `POST /v1.0/admin/rehydrate` verifies, repairs, or fully rebuilds the metadata database from raw storage. |
| **Safe deletes** | Deletes tombstone, drain in-flight read leases cluster-wide, then destroy. A read that has begun always completes. |
| **Checksums** | SHA-256 on every payload, returned on every read, optionally verified on read. |
| **Observability** | Every request captured with timing, headers, and bodies, plus time-bucketed traffic summaries. |
| **Admin dashboard** | React console in English, German, and Japanese, with light and dark themes. |

---

## Getting started

Requires [Docker](https://docs.docker.com/get-docker/). Nothing else.

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

Want sample data to look at? `./factory/reset.sh` (or `reset.bat`) rebuilds the stack from scratch
and seeds containers, objects, and traffic.

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

The same store over the other protocols:

```bash
aws --endpoint-url http://localhost:8001 s3 ls s3://telemetry/
redis-cli -p 6379 SET greeting "hello"        # lands in container resp0
```

### From source

Needs the .NET 8 or .NET 10 SDK and a PostgreSQL instance.

```bash
docker compose -f docker/compose.test.yaml up -d --wait   # PostgreSQL on 5433
dotnet run --project src/PepperX.Server
```

The server reads `pepperx.json` from its working directory; every setting is documented inline.

### SDKs

```python
from pepperx import PepperXClient

with PepperXClient("http://localhost:8000") as client:
    client.create_container("telemetry")
    client.write_object("telemetry", "metrics/cpu.json", b'{"cpu":0.42}',
                        labels=["metric", "cpu"], tags={"resolution": "1m"})
    print(client.read_object("telemetry", "metrics/cpu.json").data)
```

C#, Python, and JavaScript clients cover REST and WebSockets — see [`sdk/`](sdk/). For S3, RESP, and
MCP, use the standard client for that protocol; there is nothing PepperX-specific to wrap.

### Building the images

`docker/compose.yaml` pulls published images. To build and push your own:

```bat
build-all.bat v0.1.0
```

Produces multi-architecture images — `linux/amd64` and `linux/arm64/v8` — for
`jchristn77/pepperx-server` and `jchristn77/pepperx-dashboard`, tagged with the version and `latest`.
`build-server.bat` and `build-dashboard.bat` do one each. See [`docker/`](docker/) for building a
single-architecture image locally instead.

---

## Using the dashboard

Open **http://localhost:3000** and enter a node's REST endpoint (`http://localhost:8000`). There is
no login — PepperX is unauthenticated, so the connect screen only establishes *which node* you are
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
| **Settings** | How this node is configured and where each protocol is listening. Read-only, as the server is. |

Theme and language are in the header. First run against an empty node offers a short setup path; you
can relaunch it from Settings.

---

## Security

**There is no authentication anywhere in PepperX.** This is a design decision, not an omission — it
is meant to run inside a trusted network behind a service that performs its own access control.

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

Since this is alpha, please say whether you hit it from Docker or from source.

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
