<div align="center">
  <img src="assets/logo.svg" alt="PepperX" width="160" height="160">
</div>

# PepperX

PepperX is a high-performance, horizontally scalable key-value store with rich
metadata and immutable, self-describing storage. It is **backend infrastructure**:
there is no authentication built in — access control belongs to the application
that sits in front of it.

> This README is a working stub. The complete documentation is produced in
> Phase 15 of `PEPPERX_PLAN.md`. The plan itself is the authoritative build guide.

## What it is

- **Containers** hold immutable **extents**. One extent is one key/value pair: an
  arbitrary binary payload plus metadata in three forms — **labels** (string list),
  **tags** (string→string map), and a freeform JSON **object**.
- **Dual persistence.** PostgreSQL is the authoritative metadata store (labels,
  tags, extent location) for fast search; extent files are self-describing so the
  entire database can be **rehydrated from raw storage**.
- **Five protocols** over one process: native REST (+ OpenAPI/Swagger), S3, Redis
  RESP, WebSockets, and MCP.
- **Stateless scale-out.** Any number of nodes coordinate only through PostgreSQL.

## Repository layout

| Path | Contents |
|------|----------|
| `src/` | .NET solution: core library, server host, test projects |
| `sdk/` | C#, Python, and JavaScript SDKs (REST + WebSockets) |
| `dashboard/` | React admin dashboard |
| `docker/` | Compose files, Dockerfiles, and the resettable factory environment |
| `memory/` | Durable project orientation notes ([overview](memory/pepperx-overview.md)) |

## Documentation

- [`PEPPERX_PLAN.md`](PEPPERX_PLAN.md) — the authoritative implementation plan
- [`REST_API.md`](REST_API.md) · [`S3_API.md`](S3_API.md) · [`RESP_API.md`](RESP_API.md) · [`WEBSOCKETS_API.md`](WEBSOCKETS_API.md) · [`MCP_API.md`](MCP_API.md)

## License

[MIT](LICENSE.md)
