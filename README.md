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

## Testing

Tests need a PostgreSQL instance. Start the dockerized one first:

```bash
docker compose -f docker/compose.test.yaml up -d --wait
```

Then run any of the four runners — they all execute the same Touchstone descriptors:

```bash
dotnet run --project src/Test.Automated                      # console runner
dotnet run --project src/Test.Automated -- --results out.json # + JSON export
dotnet test src/Test.Xunit                                    # xUnit (fact + per-descriptor theories)
dotnet test src/Test.Nunit                                    # NUnit (fact + per-descriptor cases)
```

Suites that need a database are skipped (not failed) when PostgreSQL is unreachable. Connection details
can be overridden with `PEPPERX_TEST_DB_HOST`, `PEPPERX_TEST_DB_PORT`, `PEPPERX_TEST_DB_USER`,
`PEPPERX_TEST_DB_PASSWORD`, and `PEPPERX_TEST_DB_NAME`.

## Performance

`Test.Performance` starts an in-process node (or targets a running one with `--target`) and runs a
gauntlet of workloads, printing throughput and latency percentiles:

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
| mixed (70r/20w/10 search) | ~580 | 93 ms |
| replace-churn (16 writers, one key) | ~180 | 128 ms |
| write-small | ~160 | 145 ms |

## Documentation

- [`PEPPERX_PLAN.md`](PEPPERX_PLAN.md) — the authoritative implementation plan
- [`REST_API.md`](REST_API.md) · [`S3_API.md`](S3_API.md) · [`RESP_API.md`](RESP_API.md) · [`WEBSOCKETS_API.md`](WEBSOCKETS_API.md) · [`MCP_API.md`](MCP_API.md)

## License

[MIT](LICENSE.md)
