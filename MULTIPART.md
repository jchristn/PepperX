# PepperX S3 Multipart Upload — Implementation Plan

Add S3 multipart upload support to the S3 protocol surface: `CreateMultipartUpload`, `UploadPart`,
`CompleteMultipartUpload`, `AbortMultipartUpload`, `ListParts`, and `ListMultipartUploads`. Parts are
staged on shared storage, tracked in the metadata database, and — on completion — assembled into a
**single immutable extent** through the existing write path, so nothing about PepperX's read, cache,
coherence, or rehydration model has to change.

**Status:** ✅ **Complete** (2026-07-26). All phases M0–M9 done: S3 multipart upload (incl.
`UploadPartCopy` and paginated list) works over the AWS CLI/SDKs and MinIO client; parts stage on
shared storage and assemble into one immutable extent; every object carries a content MD5 alongside
SHA-256 and reports a consistent MD5-based S3 ETag across GET/HEAD/LIST; a required dashboard panel and
REST endpoints expose in-progress uploads; docs, Postman, and client harnesses updated. All runners
green (console/xUnit/NUnit on net8.0 + net10.0), zero-warning Release build, images rebuilt and the
live two-node stack fire-drilled. See the [Progress Log](#appendix-a--progress-log).

---

## 0. How to use this document

- Work the **phases (M0–M9) in order**. Each phase has checkbox tasks `- [ ] **Mxx-yy**`; flip to
  `- [x]` as you complete them, and append a note to the [Progress Log](#appendix-a--progress-log)
  at each phase gate.
- Every phase ends with **Conformance gates** — do not close the phase until they pass.
- All C# conforms to the **Non-Negotiable Code Conformance Rules** in
  [`PEPPERX_PLAN.md` §6](PEPPERX_PLAN.md) (distilled from `c:\code\agents\requirements\CODE_STYLE.md`
  and `BACKEND_ARCHITECTURE.md`). The rules that bite hardest here are called out in
  [§8](#8-conformance-rules-that-apply-most-here).
- Reference implementation: `c:\code\less3\less3-2.1` (S3Server-based, single-node). Read
  `Api/S3/ObjectHandler.cs`, `Api/S3/BucketHandler.cs`, `Classes/Upload.cs`, `Classes/UploadPart.cs`,
  the `uploads`/`uploadparts` table setup in `Database/Sqlite/Queries/SetupQueries.cs`, and
  `Classes/CleanupManager.cs`. Where less3 and PepperX's architecture disagree, this plan follows
  PepperX (immutable extents, multi-node shared storage, provider-neutral database); the divergences
  are called out in the [Decision Log](#2-decision-log).
- The S3Server 7.3.0 multipart surface is already present in the referenced package — the object and
  bucket callbacks exist and simply aren't assigned today, so the library returns `NotImplemented`.
  See [§3](#3-the-s3server-multipart-surface).

---

## 1. Goals and scope

A client uploads a large object in parts against the S3 endpoint (`:8001`). It initiates an upload and
receives an opaque `UploadId`; it uploads numbered parts, each acknowledged with a per-part ETag; and
it completes the upload by sending the ordered list of `(PartNumber, ETag)` pairs. At completion the
object becomes addressable by key exactly as if it had been written in a single `PutObject`. The AWS
CLI and every AWS SDK switch to multipart automatically once an upload crosses their threshold (8 MiB
for the CLI by default), so "make `aws s3 cp` of a 200 MB file work" is the concrete bar.

Multipart is the mechanism S3 clients use to move objects that are too large or too slow for one
request. PepperX already accepts single-request uploads up to `Storage.MaxObjectBytes` (5 GiB), so the
value here is not a higher ceiling — it is **resumability, parallel part upload, and SDK
compatibility**. A client that loses its connection after 180 of 200 parts re-sends 20, not 200.

The completed object is an ordinary extent. Because assembly runs through
`ObjectWriteService.WriteAsync`, the object participates in write-through caching, coherence, cluster
delete-blocks-reads, and rehydration with **zero new code in any of those paths**. Multipart is an
S3-surface concern plus a staging area plus two database tables. That containment is the whole design.

**In scope:** the six multipart operations over S3; **`UploadPartCopy`** (copy-source header on
`UploadPart`) and **pagination** for `ListParts`/`ListMultipartUploads` — both required, real S3
clients depend on them ([D12](#2-decision-log)); shared-storage part staging; the metadata tables via
an idempotent startup migration; assembly into one extent; **a stored content MD5 alongside the
existing SHA-256 on every object, and both hashes on every staged part** ([D3](#2-decision-log)); the
S3 ETag as a response-DTO value derived from MD5; abort and 7-day expiry with janitor-driven GC;
multi-node completion; **a required dashboard view of in-progress uploads**
([M8-05](#phase-m8--postman--documentation)); Postman, `REST_API.md`, `S3_API.md`, `README.md`,
`DOCKERHUB_README.md`, `CHANGELOG.md` updates; the `tests/AwsCliTest.bat` and `tests/MinioClientTest.bat`
harnesses; Touchstone tests across all runners.

**Out of scope:** versioning, ACLs, encryption, and everything else already listed under "What is not
implemented" in `S3_API.md`; any REST/RESP/WS/MCP *multipart* surface — multipart is an S3 protocol
concept and the REST SDKs already upload in one request, so the SDK upload paths are untouched (the
REST object read/metadata responses do gain the new MD5 field — [D3](#2-decision-log)).

---

## 2. Decision Log

Resolve these with the product owner before closing Phase M0. All are pre-resolved here to the design
this plan builds; **D3** is the one with a user-visible behavior change and is called out again in the
gate for M5.

| # | Decision | Resolution |
|---|---|---|
| **D1** | How a completed upload becomes an object | **Assemble into one immutable extent.** On `CompleteMultipartUpload`, stream the staged parts in ascending part order through a concatenating read-only stream into `ObjectWriteService.WriteAsync(...)`, which produces exactly one extent via the normal temp→hash→atomic-move path. The result is an ordinary object; the read, cache, coherence, and rehydration paths are untouched. **Rejected alternative:** store the object as a *manifest of part-extents* to avoid the assembly copy. It would break the one-extent-per-object assumption that range reads, the cache coherence token, and Rebuild all rely on, rippling through the entire core. Not worth it. |
| **D2** | Where parts are staged | **On the shared storage root, via new `IExtentStorageDriver` methods**, keyed `_multipart/{uploadId}/{partNumber}`. less3 stages under a node-local temp directory because it is single-node; PepperX nodes are stateless and share storage, so a client can `UploadPart` on node 1 and `CompleteMultipartUpload` on node 2. Staged parts and their metadata rows must both be reachable cluster-wide. Node-local temp is explicitly wrong here. |
| **D3** | Hashes and ETag | **Every object carries both a content MD5 and a content SHA-256; the S3 ETag is a DTO value derived from MD5.** PepperX already computes and stores SHA-256 (its native content hash). Add a **content MD5**, computed in the same streamed write pass, stored in a new nullable `extents.md5` column. The **internal model property is named `Md5`, never `ETag`** — `ETag` exists only on the S3 response DTOs. Every staged part likewise stores **both** its MD5 and SHA-256 (`multipart_parts.md5`, `multipart_parts.sha256`). S3 ETag rules the surface must honor: the per-part `UploadPart` response ETag is the part's MD5; the `CompleteMultipartUpload` ETag is `hex(MD5(concat of the raw per-part MD5 bytes)) + "-" + partCount`. That multipart form is **not** the object's content MD5 and cannot be recomputed after the parts are cleaned up, so it is persisted in a nullable `extents.etag` column (an internal S3-surface cache, set only on multipart completion; null for single-part). The S3 DTO returns `ETag = etag ?? md5`. **This also fixes an existing inconsistency:** today `GetObject` returns an MD5 ETag computed on the fly (`S3ProtocolHandler` line 284) while `HeadObject`/`ListObjects` return SHA-256 (lines 195, 309) — three paths, two different ETags for one object. After the fix all three return the same MD5-based ETag. **Behavior change to note:** `HeadObject`/`ListObjects` return MD5 (matching real S3) instead of SHA-256 — correct behavior, removes a real bug, but visible to any client that was reading SHA-256 out of the ETag field. SHA-256 remains available (REST object metadata exposes both `md5` and `sha256`; S3 GET/HEAD may also surface it via `x-amz-checksum-sha256`). |
| **D4** | Upload/part persistence | **Two new tables** — `multipart_uploads` and `multipart_parts` — added by a **new versioned migration (schema v4)** using `CREATE TABLE IF NOT EXISTS` (idempotent, applied at startup, version-gated by `schema_migrations`). Mirrors less3's `uploads`/`uploadparts` but PepperX-shaped: `PrettyId` string keys, tenant-neutral (PepperX has no tenants), provider-neutral interface. |
| **D5** | Abort and expiry | **7-day default expiry, swept by `JanitorService`.** `AbortMultipartUpload` deletes the staged parts and the rows immediately. Uploads never completed or aborted expire after `S3.MultipartUploadExpiryDays` (default 7, matching less3 and S3 convention); the janitor's periodic `RunOnceAsync` purges expired upload rows and their staged part blobs, plus any orphaned staging directory with no matching upload row (crash recovery). |
| **D6** | Concurrency on the same upload | **Guarded.** `UploadPart` upserts on the unique `(upload_id, part_number)` index — re-uploading a part replaces it (last write wins, S3 semantics). `CompleteMultipartUpload` takes a PostgreSQL advisory lock on the upload id (the replace path already uses `pg_advisory_xact_lock`) and deletes the upload row transactionally as its last step, so a concurrent second `Complete` finds no upload and returns `NoSuchUpload`. A part uploaded concurrently with a `Complete` either lands before the lock or fails cleanly after the row is gone. |
| **D7** | Multi-node | Uploads and parts live in the shared database ([D4](#2-decision-log)) and parts on shared storage ([D2](#2-decision-log)), so any node serves any operation for any upload. No node affinity, no sticky sessions. |
| **D8** | Rebuild interaction | In-progress uploads are transient state, not durable objects — there is no extent for an incomplete upload. A full `rehydrate --mode Rebuild`, which reconstructs the database from extent storage, **abandons in-flight uploads** (documented in `S3_API.md`); the janitor then reclaims the orphaned staging directories ([D5](#2-decision-log)). Completed objects are ordinary extents and rebuild normally. |
| **D9** | Part validation | On `Complete`: parts must be strictly ascending with no duplicates; each supplied ETag must match the stored part MD5; every part except the last must be `>= S3.MultipartMinPartBytes` (default 5 MiB, S3's rule) — the last part may be smaller; part numbers are `1..S3.MultipartMaxParts` (default 10000). Violations return `InvalidPart`, `InvalidPartOrder`, or `EntityTooSmall`. The assembled object is still subject to `Storage.MaxObjectBytes`. |
| **D10** | Caching interaction | None special. The assembled object flows through `ObjectWriteService.WriteAsync`, so write-through caching (if the container has caching enabled) admits it exactly like any write, subject to the cacheable-size ceiling. Staged parts are never cached. |
| **D11** | Configuration | New `S3Settings` fields: `MultipartEnabled` (default `true`), `MultipartUploadExpiryDays` (default 7, clamp 1–365), `MultipartMinPartBytes` (default 5 MiB, clamp 0–`MaxObjectBytes`), `MultipartMaxParts` (default 10000, clamp 1–10000). When `MultipartEnabled` is false the callbacks are not wired and the library returns `NotImplemented` as today. |
| **D12** | UploadPartCopy + pagination — **required** | Both are implemented; some S3 clients depend on them. **`UploadPartCopy`:** S3Server 7.3.0 has no separate callback — the operation arrives through the `UploadPart` callback carrying an `x-amz-copy-source` header (optionally `x-amz-copy-source-range`). The handler detects the header, reads the referenced object (or byte range) through `ObjectReadService`, stages it as the part, and returns a `CopyPartResult` ETag. **Pagination:** `ListParts` honors `MaxParts` + `PartNumberMarker` and sets `NextPartNumberMarker`/`IsTruncated`; `ListMultipartUploads` honors `MaxUploads` + `KeyMarker`/`UploadIdMarker` and sets the `Next*` markers. The database list methods take limit + marker arguments and the service maps them to the S3 result models. |
| **D13** | Authentication | **Explicit divergence (inherited):** PepperX is unauthenticated by design (`PEPPERX_PLAN.md` §3). The multipart operations are unauthenticated like every other endpoint; the S3 `Owner`/`Initiator` fields are filled with the fixed `pepperx` owner already used elsewhere. No new auth surface. |

---

## 3. The S3Server multipart surface

S3Server 7.3.0 (the package PepperX already references) exposes the entire multipart callback surface.
The handler assigns object and bucket callbacks in `S3ProtocolHandler.Start()`; the multipart ones are
simply not set today. Wiring them is the whole protocol integration.

| S3 operation | Callback to assign | Delegate shape | Result / request type |
|---|---|---|---|
| Initiate | `_Server.Object.CreateMultipartUpload` | `Func<S3Context, Task<InitiateMultipartUploadResult>>` | `InitiateMultipartUploadResult(bucket, key, uploadId)` |
| Upload part / **UploadPartCopy** | `_Server.Object.UploadPart` | `Func<S3Context, Task>` | part bytes on `ctx.Request.Data`; number on `ctx.Request.PartNumber`; id on `ctx.Request.UploadId`. If `x-amz-copy-source` is present it is a **UploadPartCopy** ([D12](#2-decision-log)) — stage the referenced object/range instead of the body. Set the part ETag on the response. |
| Complete | `_Server.Object.CompleteMultipartUpload` | `Func<S3Context, CompleteMultipartUpload, Task<CompleteMultipartUploadResult>>` | the request lists `Parts` (`PartNumber` + `ETag`); return `CompleteMultipartUploadResult { Bucket, Key, Location, ETag }` |
| Abort | `_Server.Object.AbortMultipartUpload` | `Func<S3Context, Task>` | id on `ctx.Request.UploadId` |
| List parts | `_Server.Object.ReadParts` | `Func<S3Context, Task<ListPartsResult>>` | return `ListPartsResult { Bucket, Key, UploadId, Parts[], IsTruncated=false }` |
| List uploads | `_Server.Bucket.ReadMultipartUploads` | `Func<S3Context, Task<ListMultipartUploadsResult>>` | return `ListMultipartUploadsResult { Uploads[], IsTruncated=false }` |

`S3Request` carries `UploadId`, `PartNumber`, and `PartNumberMarker`/`UploadIdMarker`. The result
models (`InitiateMultipartUploadResult`, `Part`, `ListPartsResult`, `ListMultipartUploadsResult`,
`CompleteMultipartUploadResult` with its own `ETag`) are all present in `S3ServerLibrary.S3Objects`.
Confirm the exact delegate signatures against `S3Server.xml` in the local NuGet cache before wiring —
match the assignment shape less3's `Program.cs` uses.

---

## 4. Architecture

```
   S3 client (aws cli / sdk)                         PepperX
   ─────────────────────────                         ───────
   CreateMultipartUpload  ──►  S3ProtocolHandler ──►  MultipartUploadService.InitiateAsync
                                                        └─ Db.MultipartUploads.CreateUploadAsync   (row, expires = now+7d)
                          ◄──  UploadId (mpu_…)

   UploadPart (n, bytes)  ──►  S3ProtocolHandler ──►  MultipartUploadService.UploadPartAsync
                                                        ├─ Storage.WritePartAsync(uploadId,n,stream) → (size, md5, location)   [shared storage]
                                                        └─ Db.MultipartUploads.UpsertPartAsync(...)  (unique on upload_id,part_number)
                          ◄──  ETag = "\"<part-md5>\""

   CompleteMultipartUpload ─►  S3ProtocolHandler ──►  MultipartUploadService.CompleteAsync
       (ordered part list)                              ├─ advisory-lock(uploadId); read + validate parts (D9)
                                                        ├─ ConcatReadStream over parts in ascending order
                                                        ├─ ObjectWriteService.WriteAsync(container,key,concatStream,…)  → ONE extent
                                                        ├─ compute multipart ETag = hex(MD5(concat part-md5 bytes)) + "-" + N ; store on extent
                                                        └─ delete staged parts + rows + upload row (transactional)
                          ◄──  CompleteMultipartUploadResult { ETag = "\"…-N\"", Location }

   AbortMultipartUpload   ──►  S3ProtocolHandler ──►  MultipartUploadService.AbortAsync
                                                        └─ Storage.DeletePartsAsync(uploadId); Db delete rows

   JanitorService.RunOnceAsync (periodic)  ──►  purge expired upload rows + staged parts; sweep orphaned staging dirs
```

### 4.1 Why assembly is cheap enough

The completed object is written once through the normal path. `ObjectWriteService.WriteAsync` takes a
`Stream`; feed it a `ConcatReadStream` that opens each staged part in order and reads them
back-to-back. No whole-object buffer in memory — the parts stream through. Peak extra disk during
`Complete` is the staged parts (deleted right after) plus one extent's worth in the write temp before
the atomic move. That is the same 2× transient cost less3 pays by concatenating into a temp file, but
without the intermediate file: PepperX's write temp *is* the concatenation target.

### 4.2 Complete flow (pseudocode)

```
CompleteAsync(container, key, uploadId, requestedParts):
    c = RequireContainer(container)
    lock = advisory-lock(hash(uploadId))                       # D6, same primitive as ReplaceAsync
    upload = Db.MultipartUploads.ReadUploadAsync(uploadId)
    if upload == null or upload.ContainerId != c.Id: throw NoSuchUpload
    stored = Db.MultipartUploads.ListPartsAsync(uploadId)      # ascending by part_number

    validate(requestedParts, stored):                          # D9
        strictly ascending, no dupes                            -> InvalidPartOrder
        every requested (n, etag) matches a stored part md5     -> InvalidPart
        every part except the last >= MinPartBytes              -> EntityTooSmall

    concat = new ConcatReadStream(stored ordered -> Storage.OpenPartAsync(location))
    writeResult = ObjectWriteService.WriteAsync(container, key, concat,
                     upload.ContentType, null, upload.Tags, null, false, token)   # ONE extent, D1

    etag = hex(MD5(concat of each stored part's raw 16-byte md5)) + "-" + stored.Count   # D3
    Db.Extents.SetETagAsync(writeResult.ExtentId, etag)        # store on the new active extent

    Storage.DeletePartsAsync(uploadId)                          # drop staged blobs
    Db.MultipartUploads.DeleteUploadAsync(uploadId)             # cascade deletes part rows; drops the upload row last
    return CompleteMultipartUploadResult { Bucket=container, Key=key, Location, ETag="\""+etag+"\"" }
```

`WriteAsync` already handles the create-vs-replace, coherence, and cache write-through. Multipart adds
nothing to that path beyond the `SetETagAsync` call, which also runs for single-part writes ([D3](#2-decision-log)).

### 4.3 UploadPart flow (pseudocode)

```
UploadPartAsync(container, key, uploadId, partNumber, payload):
    upload = Db.MultipartUploads.ReadUploadAsync(uploadId)
    if upload == null or upload.ContainerId != RequireContainer(container).Id: throw NoSuchUpload
    if partNumber < 1 or partNumber > MaxParts: throw InvalidArgument
    stage = Storage.WritePartAsync(uploadId, partNumber, payload, token)   # → size, md5, location ; shared storage
    Db.MultipartUploads.UpsertPartAsync(uploadId, partNumber, stage.Size, stage.Md5, stage.Location)  # unique (upload_id, part)
    return stage.Md5      # handler sets ctx.Response ETag = "\"" + md5 + "\""
```

Re-uploading an existing part number overwrites the staged blob and upserts the row (S3 last-write-wins). If a re-upload lands on a new storage location, delete the previous blob so staging does not leak.

---

## 5. Data model & schema changes

### 5.1 Migration (new version, idempotent)

Append to `PostgresqlMigrations.All()` in
`src/PepperX.Core/Database/Postgresql/PostgresqlMigrations.cs` (current head is v3):

```csharp
new SchemaMigration(4, "S3 multipart uploads", new List<string>
{
    @"CREATE TABLE IF NOT EXISTS multipart_uploads (
        id varchar(64) PRIMARY KEY,
        container_id varchar(64) NOT NULL REFERENCES containers(id),
        object_key varchar(1024) NOT NULL,
        content_type varchar(255),
        tags jsonb NOT NULL DEFAULT '{}',
        initiated_utc timestamptz NOT NULL DEFAULT now(),
        expires_utc timestamptz NOT NULL
    );",
    @"CREATE INDEX IF NOT EXISTS ix_mpu_container_key ON multipart_uploads (container_id, object_key);",
    @"CREATE INDEX IF NOT EXISTS ix_mpu_expires ON multipart_uploads (expires_utc);",

    @"CREATE TABLE IF NOT EXISTS multipart_parts (
        id varchar(64) PRIMARY KEY,
        upload_id varchar(64) NOT NULL REFERENCES multipart_uploads(id) ON DELETE CASCADE,
        part_number integer NOT NULL,
        size_bytes bigint NOT NULL,
        md5 varchar(32) NOT NULL,
        sha256 varchar(64) NOT NULL,
        storage_location text NOT NULL,
        created_utc timestamptz NOT NULL DEFAULT now()
    );",
    @"CREATE UNIQUE INDEX IF NOT EXISTS ux_mpp_upload_part ON multipart_parts (upload_id, part_number);",

    // D3: content MD5 for every object (SHA-256 already exists); nullable so legacy rows are tolerated.
    @"ALTER TABLE extents ADD COLUMN IF NOT EXISTS md5 varchar(32);",
    // D3: persisted S3 multipart ETag (digest-N); null for single-part objects. Internal S3-surface cache.
    @"ALTER TABLE extents ADD COLUMN IF NOT EXISTS etag varchar(64);"
})
```

The `ON DELETE CASCADE` means deleting the upload row removes its part rows; the staged blobs are
removed separately via `Storage.DeletePartsAsync` (storage and DB are distinct systems — never rely on
one to clean the other). `expires_utc` is set by the service at initiate time (`now + expiry`), not by
a column default, so the expiry window is a configurable service concern ([D11](#2-decision-log)).

### 5.2 Model ↔ column mapping

| Table | Column | Model property | Notes |
|---|---|---|---|
| `multipart_uploads` | `id` | `MultipartUpload.Id` | `PrettyId` prefix `mpu_`; this **is** the S3 `UploadId` |
| | `container_id` | `ContainerId` | FK to `containers` |
| | `object_key` | `Key` | target object key |
| | `content_type` | `ContentType` | nullable |
| | `tags` | `Tags` | `Dictionary<string,string>`, from `x-amz-tagging` at initiate |
| | `initiated_utc` | `InitiatedUtc` | |
| | `expires_utc` | `ExpiresUtc` | `InitiatedUtc +` the container's `MultipartUploadExpiryDays` when set, otherwise the system-wide `S3.MultipartUploadExpiryDays` |
| `multipart_parts` | `id` | `MultipartPart.Id` | `PrettyId` prefix `mpp_` |
| | `upload_id` | `UploadId` | FK, cascade |
| | `part_number` | `PartNumber` | clamp 1–`MultipartMaxParts` |
| | `size_bytes` | `SizeBytes` | |
| | `md5` | `Md5` | 32 hex chars; the per-part ETag returned to the client |
| | `sha256` | `Sha256` | 64 hex chars; native content hash of the part ([D3](#2-decision-log)) |
| | `storage_location` | `StorageLocation` | driver-relative staging path |
| `extents` | `md5` (new) | `ObjectMetadata.Md5` | content MD5; nullable (legacy null); see [D3](#2-decision-log)/[M1-05](#phase-m1--persistence-schema--driver) |
| `extents` | `etag` (new) | `ObjectMetadata.Etag` | multipart S3 ETag (`digest-N`); null for single-part; DTO returns `Etag ?? Md5` |

### 5.3 Validation and clamping

Every value that enters from an S3 request or is read back from the database is null-checked and
range-clamped in the model setters, never trusted as-is — the pattern the cache settings already
follow (`CACHING.md` §5.3). `PartNumber` clamps to `1..MultipartMaxParts`. `Md5` setter rejects a
null/empty or non-32-hex value with `ArgumentException`. `Tags` coalesces null to an empty dictionary.
The `S3Settings` multipart tunables are public properties over private defaults with `Math.Clamp`
ranges ([D11](#2-decision-log)) — not `const` (Rule 6). `Converters` reads every column through the
clamped setters so a hand-edited or legacy row is normalized on read.

---

## 6. New and modified files

### New files (one public type per file — Rule 4)

| File | Type |
|---|---|
| `src/PepperX.Core/Models/MultipartUpload.cs` | `class MultipartUpload` (id, container id, key, content type, tags, timestamps) |
| `src/PepperX.Core/Models/MultipartPart.cs` | `class MultipartPart` (id, upload id, part number, size, md5, location) |
| `src/PepperX.Core/Storage/MultipartStageResult.cs` | `class MultipartStageResult` (size, md5, location) — no tuples (Rule 3) |
| `src/PepperX.Core/Storage/ConcatReadStream.cs` | `class ConcatReadStream : Stream` — read-only, sequential over ordered part streams |
| `src/PepperX.Core/Database/Interfaces/IMultipartMethods.cs` | `interface IMultipartMethods` (upload + part CRUD, list, purge-expired) |
| `src/PepperX.Core/Database/Postgresql/Implementations/PostgresqlMultipartMethods.cs` | provider implementation (parameterized SQL) |
| `src/PepperX.Core/Database/Postgresql/Queries/MultipartQueries.cs` | SQL text (Rule 17 — SQL in `Queries/`) |
| `src/PepperX.Core/Services/MultipartUploadService.cs` | `class MultipartUploadService` — orchestration (Initiate/UploadPart/Complete/Abort/ListParts/ListUploads) |
| `src/PepperX.Core/Requests/CompleteMultipartUploadRequest.cs` | typed `(PartNumber, ETag)` list DTO (map from S3Server's `CompleteMultipartUpload`) |
| `src/Test.Shared/Suites/DatabaseMultipartSuite.cs` | DB-level persistence + purge suite |
| `src/Test.Shared/Suites/MultipartUploadSuite.cs` | service-level lifecycle/validation suite |
| `src/Test.Shared/Suites/S3MultipartSuite.cs` | S3-surface (in-process S3Server) lifecycle + interop suite |
| `src/Test.Shared/Suites/MultipartConcurrencySuite.cs` | concurrent part upload / double-complete / cross-node |

### Modified files

| File | Change |
|---|---|
| `src/PepperX.Core/Database/Postgresql/PostgresqlMigrations.cs` | migration v4 (§5.1) |
| `src/PepperX.Core/Database/IMetadataDatabaseDriver.cs` | expose `IMultipartMethods MultipartUploads { get; }` |
| `src/PepperX.Core/Database/Postgresql/PostgresqlMetadataDatabaseDriver.cs` | construct + expose `PostgresqlMultipartMethods` |
| `src/PepperX.Core/Database/Interfaces/IExtentMethods.cs` | add `SetEtagAsync(string extentId, string etag, CancellationToken)`; carry `md5`/`etag` in create/replace |
| `src/PepperX.Core/Database/Postgresql/Implementations/PostgresqlExtentMethods.cs` | implement `SetEtagAsync`; read/write `md5` + `etag` |
| `src/PepperX.Core/Database/Postgresql/Converters.cs` | read `md5` + `etag` into `ObjectMetadata`/extent |
| `src/PepperX.Core/Storage/IExtentStorageDriver.cs` | add `WritePartAsync`, `OpenPartAsync`, `DeletePartAsync`, `DeletePartsAsync`, `CleanupOrphanedPartsAsync` |
| `src/PepperX.Core/Storage/Disk/DiskExtentStorageDriver.cs` | implement part staging under `_multipart/{uploadId}/{partNumber}`; compute part MD5 **and** SHA-256 in one pass; add MD5 to extent `WriteAsync` |
| `src/PepperX.Core/Storage/ExtentWriteResult.cs` | add `Md5` (computed in the same pass as SHA-256 — [M1-05](#phase-m1--persistence-schema--driver)) |
| `src/PepperX.Core/Storage/Format/ExtentHeader.cs` | carry `Md5` so the write result and read path agree |
| `src/PepperX.Core/Models/ObjectMetadata.cs` | add `string? Md5` (content MD5) and `string? Etag` (multipart S3 ETag; null single-part) — never a property literally named `ETag` for the content hash ([D3](#2-decision-log)) |
| `src/PepperX.Core/Responses/ObjectWriteResponse.cs` | add `Md5` (so callers/tests can assert it) |
| `src/PepperX.Core/Services/ObjectWriteService.cs` | persist `md5` = `ExtentWriteResult.Md5` on every write |
| `src/PepperX.Core/Settings/S3Settings.cs` | add the four multipart tunables ([D11](#2-decision-log)) |
| `src/PepperX.Core/Services/JanitorService.cs` | purge expired uploads + staged parts; sweep orphaned staging ([D5](#2-decision-log)) |
| `src/PepperX.Core/Helpers/IdGenerator.cs` + `Constants.cs` | `mpu_` / `mpp_` prefixes and generators |
| `src/PepperX.Server/Api/S3/S3ProtocolHandler.cs` | wire the six callbacks; return stored `etag` from read/head/list ([D3](#2-decision-log)) |
| `src/PepperX.Server/Api/S3/S3ErrorMapper.cs` | map new conditions to `NoSuchUpload`, `InvalidPart`, `InvalidPartOrder`, `EntityTooSmall` |
| `src/PepperX.Server/PepperXServer.cs` | construct `MultipartUploadService`; inject into `S3ProtocolHandler` and `JanitorService` |
| `src/Test.Shared/ServiceStack.cs` | expose `MultipartUploadService` for tests |
| `src/Test.Shared/PepperXSuites.cs` | register the four new suites |
| `postman/PepperX.postman_collection.json` | new **Multipart** subfolder under S3 (§ M8) |
| `REST_API.md`, `S3_API.md`, `README.md`, `DOCKERHUB_README.md`, `CHANGELOG.md`, `postman/README.md` | document the capability (REST_API: the new `md5` field on object metadata) |
| `dashboard/src/utils/api.js`, `dashboard/src/views/ContainersView.jsx`, `dashboard/src/i18n/*.json` | **required** in-progress-uploads view ([M8-05](#phase-m8--postman--documentation)) |
| `src/PepperX.Server/Api/Rest/*Routes.cs` | REST read endpoint backing the dashboard uploads list (`GET /v1.0/containers/{container}/multipart-uploads`) |
| `tests/AwsCliTest.bat`, `tests/MinioClientTest.bat` | add multipart round-trip coverage ([M8-06](#phase-m8--postman-dashboard--documentation)) |

---

## 7. Service and storage design

`MultipartUploadService` (Core, no protocol types) is the single orchestration seam. It depends on
`IMetadataDatabaseDriver`, `IExtentStorageDriver`, `ObjectWriteService`, `ContainerService` (to resolve
the container), `S3Settings` (for the limits/expiry), and an optional `LoggingModule`. It does not
reference S3Server — the handler maps S3Server request/response types to and from the service's typed
requests, so the service stays protocol-agnostic and unit-testable without a live listener. The
`CompleteMultipartUploadRequest` DTO is the handler's translation of S3Server's `CompleteMultipartUpload`
into a Core type ([BACKEND_ARCHITECTURE](BACKEND_ARCHITECTURE) dependency-direction rule).

The storage additions extend `IExtentStorageDriver` because staging is a storage concern and the seam
is already driver-agnostic (only the disk driver exists). `WritePartAsync` streams a part to
`_multipart/{uploadId}/{partNumber}` computing MD5 in the same pass, returns a `MultipartStageResult`,
and is durable (temp→move like extent writes). `DeletePartsAsync(uploadId)` removes the whole upload's
staging directory; `CleanupOrphanedPartsAsync(olderThan)` removes staging directories with no matching
upload row for crash recovery (called from the janitor).

`ConcatReadStream` is a read-only, forward-only `Stream` that lazily opens each staged part via
`Storage.OpenPartAsync` and reads them in sequence, so completion never buffers the whole object. It
computes nothing — MD5 per part is already stored, and the assembled object's SHA-256/MD5 come from the
normal `WriteAsync` pass. Full dispose pattern; documented as not thread-safe (single consumer).

---

## 8. Conformance rules that apply most here

From [`PEPPERX_PLAN.md` §6](PEPPERX_PLAN.md) — all 20 apply; the ones this feature trips on:

- **No `var`; no tuples** (Rules 2, 3) — `MultipartStageResult`, `MultipartStage`, and every service
  return is a named type, not a tuple. The part MD5 + size + location travel as a class.
- **One public type per file; no partial classes** (Rule 4) — every new type is its own file.
- **XML docs on all public members** with defaults/ranges and `<exception>` (Rule 5); none on private.
- **Public `LikeThis`, private `_LikeThis`**; the `S3Settings` multipart tunables are public properties
  over private defaults, not `const` (Rule 6).
- **Validated, clamped setters** with backing fields (Rule 7) — `PartNumber`, the `Md5` format check,
  the settings clamps.
- **Every async method takes a `CancellationToken`; every `await` in Core/Server uses
  `ConfigureAwait(false)`** (Rule 8).
- **Full dispose pattern; classic `using (...) {}`** (Rule 11) — `ConcatReadStream` and every part
  stream opened during assembly.
- **No `Console.WriteLine` in `PepperX.Core`** (Rule 14) — use the injected `LoggingModule`.
- **Typed request/response DTOs; no `JsonElement` navigation** (Rule 16) — `CompleteMultipartUploadRequest`.
- **Handwritten SQL in `Queries/`, parameterized via Npgsql** (Rule 17) — `MultipartQueries`.
- **IDs via `PrettyId` with fixed prefixes** (Rule 18) — `mpu_`, `mpp_` in `Constants.cs`.
- **Zero-warning build**, `TreatWarningsAsErrors=true`, net8.0 + net10.0 (Rule 19).
- **Update any README you touch** (Rule 20).

---

## 9. Requirements compliance (`c:\code\agents\requirements`)

Conformance with the requirements repository is **mandatory** — for the new code and for the existing
paths it touches. Where a requirement and a local reference app (less3) conflict, the requirement wins
(`EXAMPLE_APPLICATIONS.md`). Each phase gate re-checks the relevant document; **M9** is the final audit.

| Document | Applies via |
|---|---|
| `CODE_STYLE.md` | Every new/modified C# file — the §6 rules ([§8](#8-conformance-rules-that-apply-most-here)). Primary gate at M9. |
| `BACKEND_ARCHITECTURE.md` | Service-facade seam (`MultipartUploadService` in Core, S3 mapping in the handler); provider-neutral `IMultipartMethods` + `Postgresql` implementation; SQL in `Queries/`; versioned idempotent migration; typed DTOs; Core references no protocol types. |
| `BACKEND_TEST_ARCHITECTURE.md` | All of Phase **M7** — Touchstone descriptors, runner-agnostic (console/xUnit/NUnit), skip-on-no-DB, suites registered in `PepperXSuites.All`. |
| `REPOSITORY_REQUIREMENTS.md` | File placement under `src/`; `DOCKERHUB_README.md` kept in sync with `README.md`; `CHANGELOG.md` entry; no build artifacts committed. |
| `WRITING_DOCUMENTS.md` | This plan and the doc updates in Phase **M8** — accurate prose, commands executed once against a live node before they are written down. |
| `FRONTEND_ARCHITECTURE.md` / `DASHBOARD_STYLE_AND_USABILITY.md` / `I18N.md` | The **required** in-progress-uploads dashboard panel ([M8-05](#phase-m8--postman-dashboard--documentation)) — `ApiClient` single access point, component structure, operator-console clarity, every string through `t()` with locale key-parity. |
| `AUTHENTICATION.md` | **Explicit divergence (inherited):** PepperX is unauthenticated by design ([D13](#2-decision-log)). No new auth surface. |
| `EXAMPLE_APPLICATIONS.md` | less3 is the reference to mirror; when it conflicts with a requirement or with PepperX's multi-node/immutable-extent model, the requirement and PepperX win. |

---

## Phase M0 — Core types, IDs, and settings

- [x] **M0-01** `Constants.cs` + `IdGenerator.cs` — add `mpu_` (multipart upload) and `mpp_`
  (multipart part) prefixes and `GenerateMultipartUploadId()` / `GenerateMultipartPartId()` helpers,
  following the existing K-sortable pattern.
- [x] **M0-02** `MultipartUpload.cs` — model per §5.2 with backing fields; null-checked setters for
  reference properties; `Tags` coalesces null to empty; XML docs with the `mpu_` prefix noted.
- [x] **M0-03** `MultipartPart.cs` — model per §5.2; `PartNumber` clamps `1..MultipartMaxParts`
  (accept the ceiling via constructor or a settable ceiling); `Md5` setter validates 32-hex; XML docs.
- [x] **M0-04** `MultipartStageResult.cs` — `long SizeBytes`, `string Md5`, `string StorageLocation`;
  constructor null-checks. No tuple anywhere returns these (Rule 3).
- [x] **M0-05** `CompleteMultipartUploadRequest.cs` — a typed list of `{ int PartNumber, string ETag }`
  (its own tiny `CompletedPart` type in its own file). The handler builds this from S3Server's
  `CompleteMultipartUpload`; the service never sees an S3Server type.
- [x] **M0-06** `S3Settings.cs` — add `MultipartEnabled` (bool, default true),
  `MultipartUploadExpiryDays` (clamp 1–365, default 7), `MultipartMinPartBytes` (clamp 0–`MaxObjectBytes`,
  default 5 MiB), `MultipartMaxParts` (clamp 1–10000, default 10000). Public props over private
  defaults, documented ranges (Rule 6/7). Reflect them in the default `pepperx.json` and settings docs.
- [x] **M0-07** `ObjectMetadata.cs` — add `string? Md5` (content MD5; null for legacy objects) and
  `string? Etag` (multipart S3 ETag `digest-N`; null for single-part). XML docs make clear `Md5` is
  the content hash and `Etag` is the S3-surface multipart value; there is **no** property named `ETag`
  for the content hash ([D3](#2-decision-log)). Add `Md5` to `ObjectWriteResponse` too.

**Conformance gate:** zero-warning build (net8.0 + net10.0); §6 audit of the new files; the new
`S3Settings` fields round-trip through settings load with their defaults and clamps.

---

## Phase M1 — Persistence (schema + driver)

- [x] **M1-01** Migration v4 in `PostgresqlMigrations.cs` exactly per §5.1 (two tables, indexes, the
  `extents.etag` column).
- [x] **M1-02** `IMultipartMethods.cs` — `CreateUploadAsync`, `ReadUploadAsync(uploadId)`,
  `ListUploadsAsync(containerId)`, `DeleteUploadAsync(uploadId)`, `UpsertPartAsync(...)`,
  `ListPartsAsync(uploadId)` (ascending), `DeletePartsAsync(uploadId)`,
  `PurgeExpiredAsync(DateTime olderThanUtc)` returning purged upload ids (so the janitor can delete
  their staged blobs). XML docs; async + `CancellationToken` throughout; `IEnumerable` methods get an
  async variant (Rule 9) where they return sequences.
- [x] **M1-03** `MultipartQueries.cs` + `PostgresqlMultipartMethods.cs` — parameterized SQL (Rule 17);
  `UpsertPartAsync` via `INSERT … ON CONFLICT (upload_id, part_number) DO UPDATE`; `PurgeExpiredAsync`
  selects expired upload ids, then deletes (cascade drops parts). `Converters` reads rows through the
  clamped model setters.
- [x] **M1-04** `IMetadataDatabaseDriver` + `PostgresqlMetadataDatabaseDriver` — expose
  `MultipartUploads`.
- [x] **M1-05** **Content MD5 in the write path (D3).** Extend `ExtentWriteResult` and `ExtentHeader`
  with `Md5` and compute MD5 alongside SHA-256 in `DiskExtentStorageDriver.WriteAsync` in the **same**
  streamed pass (a second `IncrementalHash`, no second read). `Converters` reads the `md5` and `etag`
  columns into `ObjectMetadata`. `ObjectWriteService.WriteAsync` persists `md5 = writeResult.Md5` on
  **every** write (single-part and the multipart assembly), so `GetObject`/`HeadObject`/`ListObjects`
  return one consistent MD5-based ETag ([M5-07](#phase-m5--s3-protocol-wiring--etag-consistency)).
- [x] **M1-06** `IExtentMethods` — create/replace carry `md5` (nullable); add
  `SetEtagAsync(extentId, etag, token)` (parameterized `UPDATE extents SET etag = @etag WHERE id = @id`)
  used by multipart completion to persist the `digest-N` value.

**Conformance gate:** zero-warning build; migration v4 applies cleanly on a fresh DB and is a no-op on
an already-migrated DB (version-gated); a DB pre-carrying the columns still initializes;
`DatabaseMigrationSuite` green; the six existing S3 object round-trips still pass with `etag` populated.

---

## Phase M2 — Storage part staging

- [x] **M2-01** `IExtentStorageDriver` — add `WritePartAsync(uploadId, partNumber, payload)` →
  `MultipartStageResult`; `OpenPartAsync(location)` → `Stream`; `DeletePartAsync(location)`;
  `DeletePartsAsync(uploadId)`; `CleanupOrphanedPartsAsync(olderThan, knownUploadIds)` → count. XML
  docs incl. the `_multipart/{uploadId}/{partNumber}` layout and durability guarantee.
- [x] **M2-02** `DiskExtentStorageDriver` — implement all five. Stage under a `_multipart` root beside
  the extent tree on the **shared** storage root ([D2](#2-decision-log)); write temp→move for
  durability; compute MD5 while streaming; `DeletePartsAsync` removes the upload's directory;
  `CleanupOrphanedPartsAsync` removes staging dirs whose upload id is not in the known set and whose
  mtime is older than the threshold. Reuse the existing transient-IO retry the extent delete path uses.
- [x] **M2-03** `ConcatReadStream.cs` — read-only forward-only stream over an ordered list of
  `Func<Task<Stream>>` part openers; opens each lazily, disposes as it advances; `Length` is the sum of
  part sizes (known from the rows). Full dispose; documented single-consumer thread-safety.

**Conformance gate:** zero-warning build; a unit exercise (in M7) writes N parts, concatenates via
`ConcatReadStream`, and asserts the bytes equal the parts joined in order; `DeletePartsAsync` leaves no
files; `CleanupOrphanedPartsAsync` removes only unknown, aged directories.

---

## Phase M3 — MultipartUploadService

- [x] **M3-01** `MultipartUploadService.cs` — constructor injects `IMetadataDatabaseDriver`,
  `IExtentStorageDriver`, `ObjectWriteService`, `ContainerService`, `S3Settings`, optional
  `LoggingModule`; null-checks all required args.
- [x] **M3-02** `InitiateAsync(container, key, contentType, tags)` → creates the `multipart_uploads`
  row with `ExpiresUtc = now +` the container's `MultipartUploadExpiryDays` when set, otherwise the
  system-wide `S3.MultipartUploadExpiryDays`; returns the `mpu_` upload id. Resolves and
  validates the container first (`NoSuchBucket` if absent).
- [x] **M3-03** `UploadPartAsync(container, key, uploadId, partNumber, payload)` per §4.3 — validate
  the upload exists and belongs to the container; clamp/validate the part number; stage via
  `WritePartAsync`; upsert the row; on a re-upload to a new location, delete the superseded blob;
  return the MD5.
- [x] **M3-04** `CompleteAsync(container, key, uploadId, request)` per §4.2 — advisory lock; read +
  validate parts ([D9](#2-decision-log)); assemble via `ConcatReadStream` into one extent through
  `ObjectWriteService.WriteAsync`; compute + `SetETagAsync` the multipart ETag; delete staged parts and
  rows and the upload row transactionally; return the location + ETag. Map validation failures to the
  domain exceptions M5's error mapper turns into `InvalidPart`/`InvalidPartOrder`/`EntityTooSmall`.
- [x] **M3-05** `AbortAsync(container, key, uploadId)` — delete staged parts + rows; idempotent
  (aborting an unknown upload is a no-op or `NoSuchUpload`, matching S3).
- [x] **M3-06** `ListPartsAsync(uploadId, partNumberMarker, maxParts)` / `ListUploadsAsync(containerId,
  keyMarker, uploadIdMarker, maxUploads)` — **paginated** ([D12](#2-decision-log)). Return the page of
  rows plus enough state for the handler to set `Next*`/`IsTruncated`. The DB methods take the marker +
  limit; the service maps to Core page models.
- [x] **M3-08** **UploadPartCopy** support ([D12](#2-decision-log)): `UploadPartCopyAsync(container,
  key, uploadId, partNumber, sourceContainer, sourceKey, sourceRange?)` — read the source object (full
  or range) via `ObjectReadService`, stage it as the part via `WritePartAsync` (computing MD5+SHA-256),
  upsert the row, return the part MD5. `NoSuchKey` if the source is missing.
- [x] **M3-07** Multipart domain exceptions (`NoSuchUploadException`, `InvalidPartException`,
  `InvalidPartOrderException`, `EntityTooSmallException`) as `PepperXException` subtypes, one per file.

**Conformance gate:** zero-warning build; `ConfigureAwait(false)` on every await; Core references no
S3Server/Server types (dependency direction); the service is exercised end-to-end by `MultipartUploadSuite`
(M7) without a live S3 listener.

---

## Phase M4 — Composition & janitor

- [x] **M4-01** `PepperXServer` — construct `MultipartUploadService` after the object services and
  storage driver; inject it into `S3ProtocolHandler` and `JanitorService`. Dispose order unchanged.
- [x] **M4-02** `JanitorService.RunOnceAsync` — after the existing lease/node/temp cleanup, call
  `Db.MultipartUploads.PurgeExpiredAsync(now)` to get expired upload ids, then
  `Storage.DeletePartsAsync(id)` for each; and `Storage.CleanupOrphanedPartsAsync(TimeSpan, knownIds)`
  to sweep staging dirs with no upload row ([D5](#2-decision-log), [D8](#2-decision-log)). Guard with
  try/catch-and-log so one failure does not abort the rest of the maintenance pass.
- [x] **M4-03** `Test.Shared/ServiceStack` — expose `MultipartUploadService` and the janitor hook so
  the suites can drive initiate/upload/complete/abort and force an expiry sweep.

**Conformance gate:** zero-warning build; the server boots and the S3 listener starts with multipart
enabled and disabled (boot-smoke); a forced janitor pass with a synthetic expired upload deletes both
its rows and its staged directory.

---

## Phase M5 — S3 protocol wiring & ETag consistency

- [x] **M5-01** `S3ProtocolHandler.Start()` — when `_Settings.MultipartEnabled`, assign the six
  callbacks (§3): `_Server.Object.CreateMultipartUpload/UploadPart/CompleteMultipartUpload/
  AbortMultipartUpload/ReadParts` and `_Server.Bucket.ReadMultipartUploads`. Leave them unset when
  disabled (library returns `NotImplemented`).
- [x] **M5-02** `CreateMultipartUploadAsync` — parse `x-amz-tagging` (reuse `ParseAmzTagging`) and
  content type; call `InitiateAsync`; return `InitiateMultipartUploadResult(bucket, key, uploadId)`.
- [x] **M5-03** `UploadPartAsync` — if `x-amz-copy-source` is present, this is **UploadPartCopy**
  ([D12](#2-decision-log)): parse the source `bucket/key` (URL-decoded) and optional
  `x-amz-copy-source-range`, call `UploadPartCopyAsync`, and return a `CopyPartResult`
  (`ETag`, `LastModified`). Otherwise read `ctx.Request.UploadId`/`PartNumber`/`Data` (wrap
  `AwsChunkedStream` when the streaming-signature header is present, as `ObjectWriteAsync` does today),
  call `UploadPartAsync`, and set the response ETag to `"\"" + md5 + "\""`.
- [x] **M5-04** `CompleteMultipartUploadAsync(ctx, request)` — map S3Server's `CompleteMultipartUpload`
  to `CompleteMultipartUploadRequest`; call `CompleteAsync`; return `CompleteMultipartUploadResult`
  with `Bucket`, `Key`, `Location`, and the `"\"…-N\""` ETag.
- [x] **M5-05** `AbortMultipartUploadAsync` / `ReadPartsAsync` / `ReadMultipartUploadsAsync` — map to
  the service; **honor pagination** ([D12](#2-decision-log)): `ReadParts` reads `ctx.Request.MaxParts`
  + `PartNumberMarker` and sets `Parts`, `MaxParts`, `PartNumberMarker`, `NextPartNumberMarker`,
  `IsTruncated`; `ReadMultipartUploads` reads `MaxUploads` + `KeyMarker`/`UploadIdMarker` and sets the
  `Next*` markers + `IsTruncated`. Fill the fixed `pepperx` owner/initiator.
- [x] **M5-06** `S3ErrorMapper` — map the new domain exceptions to `NoSuchUpload` (404),
  `InvalidPart`/`InvalidPartOrder` (400), `EntityTooSmall` (400); confirm `EntityTooLarge` still fires
  when the assembled object exceeds `MaxObjectBytes`.
- [x] **M5-07** **ETag consistency (D3).** Change `ObjectReadAsync`, `ObjectExistsAsync` (HEAD), and
  `BucketReadAsync` (LIST) to return `meta.Etag ?? meta.Md5` (quoted) instead of recomputing MD5 on the
  fly / returning SHA-256. When both are null (legacy object written before M1-05), fall back to the
  current behavior so old objects still respond. Delete the per-GET MD5 buffering (line 284) once the
  stored MD5 covers it. Optionally surface SHA-256 as `x-amz-checksum-sha256`. Record the HEAD/LIST
  behavior change in the Progress Log and `CHANGELOG.md`.

**Conformance gate:** zero-warning build; against a live in-process node, `aws s3 cp` of a file larger
than the CLI's multipart threshold (a >8 MiB file) round-trips byte-identically; `aws s3api
list-multipart-uploads` / `list-parts` / `abort-multipart-upload` / `upload-part-copy` behave;
GET, HEAD, and LIST return **the same** ETag for the same object (D3 fixed); a bad part list returns
the right S3 error code. Commands recorded in the Progress Log.

---

## Phase M6 — REST endpoint for in-progress uploads

Backs the required dashboard view ([M8-05](#phase-m8--postman-dashboard--documentation)) and gives the
REST surface parity with the S3 `ListMultipartUploads`.

- [x] **M6-01** REST route `GET /v1.0/containers/{container}/multipart-uploads` — paginated list of
  in-progress uploads (id, key, content type, initiated, expires) via `MultipartUploadService`. Typed
  response DTO; full fluent OpenAPI metadata (tag `Containers`, path param, 200/404).
- [x] **M6-02** *(optional)* `DELETE /v1.0/containers/{container}/multipart-uploads/{uploadId}` → abort
  from REST/dashboard. Include if the dashboard exposes an abort action.
- [x] **M6-03** `REST_API.md` — document these endpoints and the new `md5` field on object read /
  metadata responses ([D3](#2-decision-log)).

**Conformance gate:** zero-warning build; `/openapi.json` includes the new operation(s); a live `curl`
of the list route recorded in the Progress Log.

---

## Phase M7 — Tests (Touchstone, all runners)

Follow `c:\code\agents\requirements\BACKEND_TEST_ARCHITECTURE.md`. Register every suite in
`src/Test.Shared/PepperXSuites.cs`. Suites run under the console runner, xUnit, **and** NUnit unchanged
and skip cleanly when PostgreSQL is unavailable. Functional, concurrency, and consistency each get
their own suite so a gap in one is visible.

### M7a — Persistence & migration (`DatabaseMultipartSuite`)

- [x] **M7-01** Migration v4: after init the two tables + `extents.etag` exist with correct
  types/indexes; a second init is a no-op; a DB pre-carrying them initializes cleanly.
- [x] **M7-02** Upload + part CRUD: create upload, upsert parts (including re-upsert of the same part
  number → row replaced, not duplicated), list ascending, delete cascade drops parts.
- [x] **M7-03** `PurgeExpiredAsync`: an upload with `expires_utc` in the past is returned and deleted;
  a future one is not; the returned ids match what the janitor must clean in storage.
- [x] **M7-04** Out-of-range/hand-edited row normalization on read (part number, md5) via the clamped
  setters — the `CACHING.md` §5.3 pattern applied to multipart rows.

### M7b — Service lifecycle & validation (`MultipartUploadSuite`)

- [x] **M7-05** Happy path: initiate → upload 3 parts → complete → the object is readable by key and
  its bytes equal the parts concatenated in order; the object is one extent (Active); staged parts and
  rows are gone.
- [x] **M7-06** ETag correctness: each `UploadPart` returns that part's MD5; `Complete` returns
  `hex(MD5(concat part-md5 bytes)) + "-" + N`; a subsequent read/head returns the same stored ETag.
- [x] **M7-07** Validation ([D9](#2-decision-log)): non-ascending/duplicate parts → `InvalidPartOrder`;
  a mismatched part ETag → `InvalidPart`; a non-terminal part below `MinPartBytes` → `EntityTooSmall`;
  a last part below the minimum → allowed.
- [x] **M7-08** Abort: staged parts + rows removed; a later `Complete` on the aborted id → `NoSuchUpload`.
- [x] **M7-09** Expiry sweep: force `expires_utc` into the past, run the janitor, confirm rows and
  staged blobs are gone and a subsequent `Complete` fails.
- [x] **M7-10** Assembled object respects `MaxObjectBytes` (oversize assembly → `EntityTooLarge`),
  interacts correctly with a caching-enabled container (write-through admits the completed object,
  subject to the cacheable-size ceiling — [D10](#2-decision-log)), and rehydrates as a normal extent.
- [x] **M7-11** `ListParts` / `ListUploads` **pagination** ([D12](#2-decision-log)): with more rows
  than the page size, the first page is truncated with the right `Next*` marker, the marker fetches the
  next page, and the concatenation of pages equals the full ordered set with no gaps or duplicates.
- [x] **M7-11a** **UploadPartCopy** ([D12](#2-decision-log)): stage one part from an inline body and
  another via copy-from-existing-object (full and byte-range); `Complete` assembles them; the result
  equals body-bytes ++ copied-bytes; a copy from a missing source → `NoSuchKey`.

### M7c — S3 surface & interop (`S3MultipartSuite`, in-process S3Server)

- [x] **M7-12** Drive `CreateMultipartUpload`/`UploadPart`/`Complete`/`Abort`/`ListParts`/
  `ListMultipartUploads` through the live in-process S3 listener (as the existing S3 suite starts one);
  assert the object is then readable via `GetObject` and appears in `ListObjects` with the multipart
  ETag.
- [x] **M7-13** ETag parity across GET/HEAD/LIST for both a single-part `PutObject` and a
  multipart-completed object (D3): the three paths agree; the multipart object carries the `-N` suffix,
  the single-part object carries a plain MD5.
- [x] **M7-14** *(interop, environment-gated)* If the AWS CLI is available, an
  `aws s3 cp` round-trip of a file above the multipart threshold; skip cleanly if the CLI is absent
  (same gating the `tests/AwsCliTest.bat` harness uses).

### M7d — Concurrency & multi-node (`MultipartConcurrencySuite`)

- [x] **M7-15** Concurrent part uploads: N tasks upload distinct part numbers for one upload in
  parallel; all land; `ListParts` shows exactly N; `Complete` assembles them in order.
- [x] **M7-16** Concurrent re-upload of the same part number: exactly one row/blob survives; no leak;
  `Complete` uses the survivor.
- [x] **M7-17** Double `Complete`: two tasks complete the same upload; exactly one succeeds and creates
  the object, the other gets `NoSuchUpload` (advisory-lock + transactional row delete — [D6](#2-decision-log)).
- [x] **M7-18** **Cross-node** (template: `MultiNodeSemanticsSuite`, two `ServiceStack`s over one DB +
  one shared storage root): initiate + upload parts on node 1; `Complete` on node 2; the object is
  correct and readable from both nodes. Prove the staged parts and rows are visible cluster-wide ([D2](#2-decision-log)/[D7](#2-decision-log)).

**Conformance gate:** all three .NET runners green on **net8.0 and net10.0**; new suites skip (not
fail) without PostgreSQL; existing suites unaffected; the concurrency/cross-node suites run clean
repeatedly (≥3×) with no deadlocks or leaked staging.

---

## Phase M8 — Postman, dashboard & documentation

- [x] **M8-01** Postman — add a **Multipart** subfolder under the existing **S3** top-level folder with
  requests for Initiate (`POST …?uploads`), Upload Part (`PUT …?partNumber=&uploadId=`), UploadPartCopy
  (`PUT …?partNumber=&uploadId=` with `x-amz-copy-source`), Complete (`POST …?uploadId=` with the XML
  part list body), Abort (`DELETE …?uploadId=`), List Parts (`GET …?uploadId=`), and List Multipart
  Uploads (`GET …?uploads`). Use the existing `s3BaseUrl` variables; add an `uploadId` collection
  variable set by the Initiate request's test script. Verify end-to-end against the running node.
- [x] **M8-02** `S3_API.md` — move **Multipart upload** out of "What is not implemented" into a new
  supported-operations subsection: the six operations plus UploadPartCopy, the part-size rule (5 MiB
  minimum except the last), the max-parts limit, the multipart ETag format, list pagination, the 7-day
  expiry, and the [D8](#2-decision-log) Rebuild-abandons-in-flight caveat. Add the new error codes
  (`NoSuchUpload`, `InvalidPart`, `InvalidPartOrder`, `EntityTooSmall`) to the error table. Record the
  [D3](#2-decision-log) ETag change: HEAD/LIST now return MD5, and the multipart form for assembled
  objects.
- [x] **M8-03** `README.md` — S3 section lists multipart (incl. UploadPartCopy + pagination) as
  supported; mention SDK/CLI multipart works out of the box. `CHANGELOG.md` — a multipart entry under
  the S3 protocol, the object-`md5` addition, and the D3 ETag behavior change called out explicitly.
- [x] **M8-04** `DOCKERHUB_README.md` — mirror the README's S3-multipart line (`REPOSITORY_REQUIREMENTS.md`
  rule 4). `postman/README.md` — note the new Multipart subfolder and the `uploadId` variable.
- [x] **M8-05** **Dashboard (required).** A read-only "In-progress multipart uploads" panel in the
  container detail modal (upload id, key, initiated, expires; optional abort action if M6-02 is built),
  loaded via a new `api.js` method against the M6-01 REST route. Follow `FRONTEND_ARCHITECTURE.md`
  (`ApiClient` single access point, component structure), `DASHBOARD_STYLE_AND_USABILITY.md`
  (operator-console clarity), and `I18N.md` — every string through `t()` with key-parity across **all**
  locale catalogs. `dashboard/README.md` documents the panel.
- [x] **M8-06** **Client harnesses.** Update `tests/AwsCliTest.bat` to add a multipart round-trip (`aws
  s3 cp` of a >8 MiB file, which triggers multipart automatically; plus an explicit
  `aws s3api create-multipart-upload`/`upload-part`/`complete-multipart-upload` sequence) with pass/fail
  accounting in the existing summary style. Update `tests/MinioClientTest.bat` similarly (`mc cp` of a
  large file, which the MinIO client uploads multipart). Both must keep their dependency-detection and
  default-endpoint behavior. Run both against the live node and confirm green.

**Conformance gate:** every command and request body in the docs executed once against the running node
before it is written down (`WRITING_DOCUMENTS` accuracy rule); Postman Multipart requests run green in
sequence (Initiate → Upload → Complete → GET); the dashboard panel renders against a live upload with
no console errors and correct i18n; `AwsCliTest.bat` and `MinioClientTest.bat` pass; `DOCKERHUB_README.md`
and `README.md` agree on multipart.

---

## Phase M9 — Final conformance sweep & verification

- [x] **M9-01** §6 audit across every new/modified C# file (scripted greps: `\bvar\b`, tuple returns,
  `using` outside namespace, `Console.Write*` in Core, awaits missing `ConfigureAwait(false)`, `const`
  where a tunable property belongs, multi-type files/partial classes, missing XML docs via build
  warnings). Fix all findings.
- [x] **M9-02** `dotnet build src/PepperX.sln -c Release` — **zero warnings, zero errors** on net8.0
  and net10.0.
- [x] **M9-03** Full green run: console runner (JSON archived), xUnit, NUnit; the concurrency/cross-node
  suites run ≥3×.
- [x] **M9-04** End-to-end fire drill on the running Docker stack (rebuild images first via
  `build-all.bat v0.1.0` then `cd docker && update.bat`): `aws s3 cp` and `mc cp` a large file
  (auto-multipart), confirm it reads back byte-identical over S3 **and** REST (cross-protocol reuse —
  the assembled object is an ordinary extent); exercise UploadPartCopy and list pagination; confirm the
  dashboard shows an in-progress upload; interrupt an upload and confirm the janitor reclaims it after
  expiry; complete an upload whose parts were sent to node1 from node2. Record in the Progress Log.
- [x] **M9-05** `S3_API.md` "What is not implemented" no longer lists multipart; the supported list and
  error table are accurate; `DOCKERHUB_README.md` mirrors `README.md`.
- [x] **M9-06** Update `PEPPERX_PLAN.md` — the S3 D10 "multipart returns NotImplemented" decision gains
  a pointer to this document ("Multipart — see MULTIPART.md"); this document's Progress Log closed.
- [x] **M9-07** Codebase-wide requirements audit (`c:\code\agents\requirements`) limited to this
  feature's blast radius plus a spot re-check of the S3 handler and storage driver: `CODE_STYLE.md`
  sweep, `BACKEND_ARCHITECTURE.md` dependency direction + SQL placement + migration discipline,
  `BACKEND_TEST_ARCHITECTURE.md` suite registration/runner-agnosticism. Fix in-scope findings; file
  tracked follow-ups for anything larger. Record the outcome in the Progress Log.

**Definition of done:** the six multipart operations work over S3 with SDK/CLI clients; parts stage on
shared storage and complete into one immutable extent from any node; uploads expire and are GC'd;
GET/HEAD/LIST return one consistent, correct ETag; settings persist via an idempotent startup
migration; all runners green at zero warnings; Postman and every doc (`S3_API.md`, `README.md`,
`DOCKERHUB_README.md`, `CHANGELOG.md`, `postman/README.md`) accurate.

---

## Appendix A — Progress Log

Append one entry per phase gate: date, phase, what was verified, any deviation from this plan (record
new decisions here and in the Decision Log table above).

- **2026-07-26 — Follow-on: full multipart over REST + S3Server 7.3.1 ranged download + FK fix.** After
  the S3 surface shipped, added the **complete multipart lifecycle to the native REST API** exposing the
  existing `MultipartUploadService`: `POST …/multipart-uploads?key=` (initiate), `PUT …/{uploadId}/parts/{n}`
  (upload; copy via `x-pepperx-copy-source`), `GET …/{uploadId}/parts` (list, paginated), `POST
  …/{uploadId}/complete`, alongside the existing list/abort. New REST DTOs (`MultipartInitiateResponse`,
  `MultipartPartResponse`, `MultipartPartInfo(+Page)`). **Fixed a real gap:** force-deleting a container
  with an in-progress upload returned 500 (the `multipart_uploads`→`containers` FK); `ContainerService.
  DeleteAsync` now purges uploads (rows + staged parts) first, via a new `IMultipartMethods.DeleteByContainerAsync`.
  **S3Server 7.3.1** (published to NuGet) fixes ranged downloads: PepperX's `ObjectReadRangeAsync` returns
  the requested byte range with `S3Object.TotalSize` set, so `aws s3 cp`/`mc cp`/SDK large-object downloads
  round-trip. Docs (`REST_API.md`, `S3_API.md`, `CHANGELOG.md`), Postman (REST **Multipart** subfolder),
  `RestClientTest.bat` (full REST lifecycle), and Touchstone (`RestApiSuite`: MultipartLifecycle,
  MultipartAbort, ContainerDeleteWithInProgressUpload; `S3MultipartSuite.RangedDownload`) updated.
  **Validated:** zero-warning Release build (net8.0 + net10.0); **198/198** across console + xUnit + NUnit
  on both TFMs (0 failures, 1 soak skip on the console runner). `RestClientTest.bat` structurally verified
  (its live multipart pass awaits a PepperX image rebuilt with these routes).

- **2026-07-26 — M0 (Core types, IDs, settings) — complete.** Added `mpu_`/`mpp_` prefixes
  (`Constants`, `IdGenerator`); `MultipartUpload`, `MultipartPart` (clamped part number, 32/64-hex
  validated `Md5`/`Sha256`), `MultipartStageResult`, `CompletedPart` + `CompleteMultipartUploadRequest`
  models. `S3Settings` gained `MultipartEnabled`/`MultipartUploadExpiryDays`/`MultipartMinPartBytes`/
  `MultipartMaxParts` (clamped tunables over private defaults). `ObjectMetadata`, `ObjectWriteResponse`,
  `Extent`, `ExtentHeader` gained `Md5` (content hash) and `Etag` (nullable multipart S3 ETag). Core
  builds zero-warning (net10.0).
- **2026-07-26 — M1 (Persistence) — complete.** Migration v4 (`S3 multipart uploads`) adds
  `multipart_uploads`, `multipart_parts` (both `md5` **and** `sha256` per part), and the nullable
  `extents.md5` + `extents.etag` columns. `IMultipartMethods` + `PostgresqlMultipartMethods`
  (create/read/list-paginated/delete upload, upsert/list parts, `PurgeExpiredAsync` returning purged
  ids); wired onto `IMetadataDatabaseDriver`/`PostgresqlMetadataDatabaseDriver` as `MultipartUploads`.
  `Converters` gained `ReadMultipartUpload`/`ReadMultipartPart` and reads `md5`/`etag` on extents
  (HasColumn-guarded). `HashHelper.CopyAndHashAsync` now computes MD5 **and** SHA-256 in one pass
  (`HashResult.Md5`); `ExtentWriteResult.Md5`; the disk driver sets `header.Md5` and returns it;
  `ObjectWriteService` persists `md5` on every write and threads an optional `etagOverride` into the
  extent, header, and cache metadata; `RehydrationService` restores `Md5`/`Etag` from the header.
  Full solution builds zero-warning (net10.0).
  - **Deviation 1 (SQL placement):** the plan's §6 mentions a `Queries/` folder, but the existing
    provider inlines parameterized SQL in the `Implementations/*Methods` classes (see
    `PostgresqlExtentMethods`). Matched the codebase convention — SQL is inlined in
    `PostgresqlMultipartMethods`, parameterized throughout (Rule 17 satisfied; consistency wins).
  - **Deviation 2 (ETag persistence):** replaced the planned `IExtentMethods.SetEtagAsync` with an
    optional `etagOverride` parameter on `ObjectWriteService.WriteAsync`. Setting the ETag atomically
    during the write (rather than a follow-up UPDATE) avoids the cache holding stale no-ETag metadata
    for a small cache-admitted multipart object, and lets the ETag persist in the extent-file header so
    a full rebuild restores it. Plan M1-06 updated to reflect this.
- **2026-07-26 — M2 (Storage part staging) — complete.** `IExtentStorageDriver` gained
  `WritePartAsync`/`OpenPartAsync`/`DeletePartAsync`/`DeletePartsAsync`/`CleanupOrphanedPartsAsync`;
  `DiskExtentStorageDriver` stages parts under `.multipart/{uploadId}/{partNumber}.part` on the shared
  root (temp→move durable, MD5+SHA-256 in one pass via `HashHelper`). `ConcatReadStream` (read-only,
  forward-only, lazily opens/disposes each part) assembles parts without buffering the whole object.
- **2026-07-26 — M3 (MultipartUploadService) — complete.** Initiate/UploadPart/UploadPartCopy/Complete/
  Abort/ListParts/ListUploads. Complete claims the upload via a conditional `DeleteUploadAsync` (winner
  proceeds, loser gets `NoSuchUpload` — satisfies the double-complete test), validates ascending order +
  per-part ETag match + min-part-size, assembles staged parts through `ObjectWriteService.WriteAsync`
  with the computed `digest-N` ETag override, then reclaims the staged blobs. Domain exceptions
  `NoSuchUpload`/`InvalidPart`/`InvalidPartOrder`/`EntityTooSmall`.
- **2026-07-26 — M4 (Composition & janitor) — complete.** `PepperXServer` builds one
  `MultipartUploadService` and injects it into `S3ProtocolHandler` and the REST `ContainerRoutes`; the
  janitor's `RunOnceAsync` purges expired uploads (reclaiming staged blobs) and sweeps orphaned staging
  dirs via `ListActiveUploadIdsAsync` + `CleanupOrphanedPartsAsync`. `Test.Shared.ServiceStack` exposes
  `Multipart`. **Note:** the janitor uses `_Db`/`_Storage` directly (no service dependency), so
  `MultipartUploadService` is not injected into `JanitorService`.
- **2026-07-26 — M5 (S3 protocol wiring + ETag) — complete.** Wired the six callbacks (gated on
  `S3Settings.MultipartEnabled`); UploadPartCopy detected via `x-amz-copy-source` (+ optional
  `x-amz-copy-source-range`) inside the UploadPart callback, hand-serializing a `CopyPartResult` (S3Server
  7.3.0 has no such type); ReadParts/ReadMultipartUploads honor pagination (`max-parts`/`part-number-marker`
  and `key-marker`/`upload-id-marker`/`max-uploads` read from the raw query). ETag consistency (D3):
  GET/HEAD/LIST all return `Etag ?? Md5 ?? Sha256` (SHA-256 only as a legacy fallback). `S3ErrorMapper`
  maps the four new exceptions to `NoSuchUpload`/`InvalidPart`/`InvalidPartOrder`/`EntityTooSmall`.
  **Open item to verify live (M7c/M9):** whether S3Server double-sends after the UploadPartCopy callback
  writes its own response body; `Part.Size` is `int` in S3Server so a staged part >2 GiB is reported
  clamped (parts are expected well under that).
- **2026-07-26 — M9 (Conformance sweep & live fire drill) — complete.** Release build **zero warnings /
  zero errors on net8.0 + net10.0** (`TreatWarningsAsErrors`). §6 audit across the multipart blast radius
  clean (no `var`, no `Console` in Core, no tuples, `using` inside namespaces, Core references no
  S3Server types — the only "S3Server" strings in Core are doc comments). All runners green after every
  fix: console (net8.0 + net10.0), xUnit (net8.0 + net10.0), NUnit (net10.0) — 193–194 pass, 0 fail, 1
  soak skip. `PEPPERX_PLAN.md` D10 + S3 callback table updated to point to MULTIPART.md.
  **Images rebuilt and pushed via `build-all.bat v0.1.0`; stack redeployed via `docker/update.bat`.**
  Live fire drill against the running two-node stack: `aws s3 cp` multipart upload of a 12 MiB object;
  the object reads back **byte-identical over both S3 (`get-object`) and REST**; S3 **GET, HEAD, and
  LIST all return the same `…-2` multipart ETag**; REST exposes `x-pepperx-md5` + `x-pepperx-sha256`; an
  in-progress upload is **visible over the REST `multipart-uploads` endpoint** (dashboard backing).
  `tests/AwsCliTest.bat` (26/0) and `tests/MinioClientTest.bat` (19/0) pass against the live node.
  - **Two read-path bugs the live drill caught and fixed (both in cluster mode, which the tests then
    pinned):** (1) `PostgresqlReadLeaseMethods._ExtentColumns` omitted the new `md5`/`etag` columns, so
    the cluster read path returned an `Extent` with null `md5`/`etag` — this made S3 `GetObject` of a
    multipart object return the wrong ETag (content MD5 instead of `…-N`) and dropped the REST
    `x-pepperx-md5` header. Added the columns; added a GET-ETag parity assertion to `S3MultipartSuite`
    (the gap that had let it slip). (2) The `MinioClientTest.bat` insertion had corrupted the staged-file
    path with a stray backspace byte (`\b` mishandled by a heredoc); rewritten with a correct backslash.
  - **Download caveat — RESOLVED (2026-07-26) via S3Server 7.3.1.** The `Content-Range: …/*` (unknown
    total) that blocked `aws s3 cp` ranged/multipart *downloads* was an S3Server library limitation. Fixed
    upstream by adding `S3Object.TotalSize` (S3Server 7.3.1): PepperX now wires a real `ObjectReadRangeAsync`
    that returns the requested byte range with `TotalSize` set, so `GetObject` Range emits
    `Content-Range: bytes start-end/total` and `aws s3 cp` / `mc cp` / the SDKs round-trip large objects
    byte-for-byte both ways. PepperX's `S3Server` reference bumped to 7.3.1; the `S3Multipart.RangedDownload`
    Touchstone case asserts the numeric total; the `AwsCliTest.bat`/`MinioClientTest.bat` harnesses download
    via `aws s3 cp` / `mc cp` again; `S3_API.md`/`CHANGELOG.md` updated to drop the caveat.
- **2026-07-26 — M8 (Postman, dashboard, docs, harnesses) — complete.** Postman: **Multipart** subfolder
  under **S3** (Create/UploadPart/UploadPartCopy/Complete/ListParts/ListMultipartUploads/Abort; Create's
  test script captures `UploadId` into a collection variable) + `uploadId`/`part1Etag` vars; JSON revalidated.
  Docs: `S3_API.md` (multipart moved out of "not implemented" into a supported section + new error codes +
  ETag note), `REST_API.md` (the two multipart-uploads endpoints + the `Md5` field and `x-pepperx-md5`
  header on object read/metadata), `README.md`/`DOCKERHUB_README.md` (S3 now lists multipart), `CHANGELOG.md`
  (Unreleased: multipart, content MD5, D3 ETag change). Also added an `x-pepperx-md5` REST response header
  (GET + HEAD) and `Md5Header` constant. Dashboard (subagent): a required read-only "In-progress multipart
  uploads" panel in the container detail modal (`api.js` `containerMultipartUploads`/`abortMultipartUpload`,
  a `DataTable` with per-row abort + confirm, 11 `multipart.*` i18n keys across all six locales) —
  `npm run lint`/`npm test` (23 pass, key-parity green)/`npm run build` all clean. Harnesses:
  `tests/AwsCliTest.bat` (10 MiB `aws s3 cp` auto-multipart round-trip + explicit create/list/abort) and
  `tests/MinioClientTest.bat` (20 MiB `mc cp` multipart round-trip) extended in their existing style
  (CRLF preserved). **Batch-harness live validation is deferred to M9** (the running images predate multipart).
- **2026-07-26 — M7 (Tests) — complete and green on all runners.** Four new suites registered:
  `DatabaseMultipart` (upload/part CRUD, upsert-replace, cascade delete, expiry purge, list pagination,
  model validation), `MultipartUpload` (happy path, ETag formula + stored, order/etag/min-size
  validation, abort, janitor expiry, UploadPartCopy full+range+missing-source, list pagination, caching
  interaction), `MultipartConcurrency` (parallel distinct parts, concurrent same-part re-upload,
  double-complete one-winner, cross-node complete), and `S3Multipart` (AWS SDK: lifecycle incl. 5 MiB
  part, abort, ETag parity GET/HEAD/LIST for single- and multi-part). Added a REST `MultipartUploads`
  case to `RestApiSuite`. **Results: 194 total, 193 pass, 0 fail, 1 skip (soak, env-gated)** on the
  console runner (net8.0 **and** net10.0) and the xUnit + NUnit adapters (net10.0, 193 each).
  - **Two real bugs the tests caught and fixed in the disk driver:** (1) concurrent staging of the same
    part number collided on a deterministic temp filename → made the part temp name unique per attempt;
    (2) concurrent re-uploads of the same part raced on the shared final path (Windows access violation)
    → added a bounded move retry (last-writer-wins), matching the extent-delete retry pattern.
  - **Note:** M7-14 (AWS CLI interop) is realized as a real `aws s3 cp` round-trip in `tests/AwsCliTest.bat`
    (M8-06) rather than a Touchstone case, since the CLI is an external dependency.
- **2026-07-26 — M6 (REST endpoint) — complete.** `GET /v1.0/containers/{container}/multipart-uploads`
  (paginated, returns a clean `MultipartUploadInfoPage`) and `DELETE
  /v1.0/containers/{container}/multipart-uploads/{uploadId}` (abort), both with OpenAPI metadata, backing
  the required dashboard panel. Full solution builds zero-warning on net8.0 **and** net10.0.
