# Changelog

All notable changes to PepperX are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **S3 multipart upload.** The S3 surface now implements `CreateMultipartUpload`, `UploadPart`,
  `UploadPartCopy`, `CompleteMultipartUpload`, `AbortMultipartUpload`, `ListParts`, and
  `ListMultipartUploads`. Parts stage on shared storage and are assembled into a single immutable object
  on completion, so a multipart-assembled object is indistinguishable from one written in a single
  `PutObject`. The AWS CLI and SDKs use multipart automatically for large objects. Parts and their
  metadata are shared cluster-wide, so an upload may be started on one node and completed on another.
  Non-final parts must be at least 5 MiB; uncompleted uploads expire after `S3.MultipartUploadExpiryDays`
  (default 7) and are reclaimed by the janitor. `ListParts` and `ListMultipartUploads` are paginated.
- **Content MD5 on every object.** Objects now record an MD5 alongside the existing SHA-256, computed in
  the same streamed write pass. Exposed over REST as the `Md5` field on object write/metadata responses
  and the `x-pepperx-md5` response header.
- **Full multipart upload over the REST API.** The complete lifecycle is now available natively over
  REST, not just S3: `POST /v1.0/containers/{container}/multipart-uploads?key=` (initiate),
  `PUT …/multipart-uploads/{uploadId}/parts/{partNumber}` (upload a part, or copy one from an existing
  object via `x-pepperx-copy-source`), `GET …/multipart-uploads/{uploadId}/parts` (list parts, paginated),
  `POST …/multipart-uploads/{uploadId}/complete` (assemble), plus `GET …/multipart-uploads` (list
  in-progress) and `DELETE …/multipart-uploads/{uploadId}` (abort). Individual parts can be inspected and
  managed: `GET …/parts/{partNumber}` returns a part's metadata (size, MD5/ETag, SHA-256, timestamp) and
  `DELETE …/parts/{partNumber}` discards a single staged part; the parts list now includes each part's
  SHA-256 alongside its MD5. In-progress uploads — and, per upload, their parts — are surfaced and
  manageable in the dashboard (a dedicated Uploads page and an upload-detail parts modal).
- **S3 ranged / large-object downloads.** `GetObject` now honors `Range` requests, returning `206 Partial
  Content` with a `Content-Range: bytes start-end/total` header (via S3Server 7.3.1's `S3Object.TotalSize`).
  Ranged/multipart downloads over `aws s3 cp`, the AWS SDKs, and `mc cp` now round-trip large objects
  byte-for-byte in both directions.

### Changed

- **S3 ETag is now MD5-based and consistent across operations.** `GetObject`, `HeadObject`, and
  `ListObjects` all report the same ETag for a given object: a plain MD5 hex for a single-`PutObject`
  object, and the standard `hex(MD5(concat of part MD5s))-N` multipart form for a completed multipart
  object. Previously `GetObject` returned an MD5 while `HeadObject`/`ListObjects` returned the SHA-256 —
  a client reading the SHA-256 out of the S3 ETag field will now see MD5.

### Fixed

- **Force-deleting a container with in-progress multipart uploads.** Container deletion now purges any
  in-progress multipart uploads (rows and staged parts) before removing the container. Previously the
  `multipart_uploads` → `containers` foreign key caused the delete to fail with a 500.

## [0.1.0] - 2026-07-23

First alpha. Everything below works and is tested, but interfaces and the on-disk extent
format may still change before 1.0.

### Storage

- Containers holding immutable **extents**. An object is a key, a binary payload, and metadata in
  three forms: labels (flat list), tags (key-value), and a freeform JSON object.
- **PXE1 self-describing extent format.** Each file carries its own key, labels, tags, metadata
  object, and SHA-256 checksum in a header ahead of the payload, so raw storage is sufficient to
  reconstruct the metadata database.
- **Atomic replace.** Writing an existing key creates a new extent and repoints the key; last writer
  wins and in-flight readers finish safely against the old extent.
- **Dual persistence** — PostgreSQL indexes metadata for search; extent files are the system of
  record. `POST /v1.0/admin/rehydrate` verifies, repairs, or fully rebuilds the database from storage.
- **Per-container caching.** An optional in-memory read/write-through cache per container (FIFO or
  LRU, bounded by object count and memory, with a per-object admission ceiling), enabled by default
  on new containers. Reads are served from memory on a hit and validated against the active extent so
  the cache stays coherent across nodes; writes are write-through and deletes evict first. Configured
  per container via `PUT /v1.0/containers/{container}/cache`, the dashboard, or the create request;
  settings persist in the metadata database via a startup migration.

### Protocols

- **REST** (8000) — the complete surface, with a generated OpenAPI document and Swagger UI.
- **S3** (8001) — buckets, objects, and tags. Compatible with the AWS CLI and SDKs, including the
  AWS streaming-signature upload format.
- **WebSockets** (8002) — REST-equivalent operations over one connection, correlated by `RequestId`.
- **MCP** (8003 HTTP / 8004 TCP) — 16 tools for LLM agents, each publishing a full JSON Schema.
- **RESP** (6379) — Redis string commands over durable storage. Any Redis client works. A database
  index maps to the container `resp{n}` by default; a container may also claim a specific RESP database
  index (`PUT /v1.0/containers/{container}/resp-index`, unique across containers) so a numeric `SELECT`
  reaches an arbitrarily named container.

All five share one namespace: an object written over any protocol is readable over the others.

### Clustering

- Stateless nodes coordinating only through PostgreSQL and shared extent storage.
- Node heartbeats with liveness tracking and janitor-based reaping.
- **Cluster-wide delete coordination.** Deletes tombstone the extent, drain in-flight read leases
  across every node, and only then destroy the payload — so a read that has begun always completes.

### Operations

- React admin dashboard: containers and objects, cross-container metadata search, capacity and
  cluster health, request history with a traffic chart, and an API explorer driven by the node's own
  OpenAPI document. Available in English, Spanish, French, German, Chinese, and Japanese.
- Request history capture with time-bucketed summaries, body truncation limits, and path exclusions.
- `GET /v1.0/admin/settings` — non-secret configuration view, deliberately omitting the database
  password and S3 static keys.
- Docker images for the server and dashboard, a two-node compose stack, and factory reset scripts for
  Windows and POSIX.

### Clients

- SDKs for C#, Python (sync and async), and JavaScript/TypeScript, covering REST and WebSockets.
- Postman collection and environment.
- Cross-SDK verification: each SDK writes a canonical object and the other two read it back
  byte-for-byte.

### Testing

- Runner-agnostic [Touchstone](https://www.nuget.org/packages/Touchstone) suites executing
  identically under a console runner, xUnit, and NUnit.
- `Test.Performance` harness with eight workloads reporting throughput and latency percentiles.
- Dashboard component tests plus two Playwright sweeps: every route at three widths in both themes,
  and four locales checked for overflow and text direction.

### Notes

PepperX is **unauthenticated by design**. It is backend infrastructure meant to sit behind a service
that performs its own access control. See [Security](README.md#security) before deploying.

[0.1.0]: https://github.com/jchristn/PepperX/releases/tag/v0.1.0
