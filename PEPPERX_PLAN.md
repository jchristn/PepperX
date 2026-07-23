# PepperX Implementation Plan

PepperX is a high-performance, scalable, **unauthenticated** key-value store with rich metadata (labels, tags, and a freeform JSON metadata object), immutable extent-based storage, a shared PostgreSQL metadata database, and five protocol surfaces: native REST, S3, Redis RESP, WebSockets, and MCP (JSON-RPC). Server nodes are stateless protocol handlers; PostgreSQL is authoritative; extents are self-describing so the database can be fully rehydrated from raw storage.

This plan is the single tracking document for the build. Every task is a checkbox a developer marks as work proceeds.

---

## 0. How To Use This Document

- Every task has a stable ID (`P03-07` = phase 3, task 7). Do not renumber; append new tasks at the end of a phase (`P03-07a` for insertions).
- Mark progress by editing the checkbox and appending an annotation:
  - `- [x] **P03-07** ... ✅ 2026-07-24 (jsc) note if useful`
  - `- [x] **P03-07** ... 🚧 in progress` / `⛔ blocked: reason`
- Each phase ends with **Conformance Gates**. A phase is not complete until its gates pass. Gates are not optional.
- The **Decision Log** (§2) records resolved design questions. If implementation reveals a conflict, add a new decision entry — do not silently diverge.
- Requirements in `C:\code\agents\requirements\*` are non-negotiable. Where PEPPERX.md explicitly overrides one (e.g., no authentication), the override is recorded in §3 with justification, as `BACKEND_ARCHITECTURE.md` requires ("divergence should be explicit and justified").

### Project context (`memory/`)

Durable orientation notes live in the repo under [`memory/`](memory/). Read them first when resuming work:

| File | Purpose |
|---|---|
| [`memory/pepperx-overview.md`](memory/pepperx-overview.md) | What PepperX is, the resolved decisions (D1–D10), and how the pieces fit together. |
| [`memory/agents-requirements-and-reference-apps.md`](memory/agents-requirements-and-reference-apps.md) | Where the non-negotiable requirements docs live, which `C:\Code` app to imitate per pattern, and pinned package versions. |
| [`memory/MEMORY.md`](memory/MEMORY.md) | Index of the above. |

### Normative inputs

| Document | Governs |
|---|---|
| `C:\Code\PepperX\PEPPERX.md` | Product definition (wins on product scope) |
| `C:\code\agents\requirements\CODE_STYLE.md` | C# style — every file |
| `C:\code\agents\requirements\BACKEND_ARCHITECTURE.md` | Server layout, Watson 7, DB layer, request history, OpenAPI |
| `C:\code\agents\requirements\BACKEND_TEST_ARCHITECTURE.md` | Touchstone test projects |
| `C:\code\agents\requirements\FRONTEND_ARCHITECTURE.md` | Dashboard stack, required views, i18n |
| `C:\code\agents\requirements\DASHBOARD_STYLE_AND_USABILITY.md` | Dashboard usability bar, route inventory, visual QA |
| `C:\code\agents\requirements\REPOSITORY_REQUIREMENTS.md` | Repo layout, docs, SDK layout, Docker file conventions |
| `C:\code\agents\requirements\I18N.md` | Dashboard internationalization |
| `C:\code\agents\requirements\EXAMPLE_APPLICATIONS.md` | Which reference app to imitate for which pattern |

### Primary reference applications (study before coding the matching phase)

| Pattern | Reference |
|---|---|
| Watson 7 direct hosting, OpenAPI fluent annotation, route registration, Preflight/PreRouting/PostRouting, request-history capture | `C:\Code\RecallDB\RecallDB\src\RecallDb.Server\RecallDbServer.cs` |
| `EnumerationQuery` / `EnumerationResult<T>` (including Labels/Tags filters) | `C:\Code\LiteGraph\HnswLite\src\HnswIndex.Server\Classes\EnumerationQuery.cs`, `C:\Code\LiteGraph\litegraph\src\LiteGraph\EnumerationResult.cs` |
| Storage-driver-per-backend project split | `C:\Code\LiteGraph\HnswLite\src\HnswIndex.RamStorage|SqliteStorage|PostgresqlStorage` |
| Database driver base + provider folders + Implementations/ + Queries/ | `C:\Code\Verbex\Verbex\src\Verbex\Database\`, `C:\Code\NetLedger\src\NetLedger\Database\` |
| Touchstone Test.Shared/Automated/Xunit/Nunit | `C:\Code\Conductor\Conductor\src\Test.*`, `C:\Code\Tempo\Tempo\src\Test.Shared\TempoSuites.cs` |
| S3 protocol serving (callback surface) | `C:\Code\Less3\S3Server-7.0\src\S3Server\Callbacks\*.cs`, Less3 server implementation |
| RESP protocol serving | `C:\Code\RedisRespServer\src\Redish.Server`, `Sample.RedisInterface`, `Test.StackExchangeRedis` |
| MCP via Voltaic | `C:\Code\LiteGraph\litegraph\src\LiteGraph.McpServer\LiteGraphMcpServer.cs`, `C:\Code\Tablix\src\Tablix.Server\Mcp\McpToolRegistrar.cs`, `C:\Code\Voltaic\README.md` |
| WebSockets server | `C:\Code\Watson\WatsonWebsocket-4.0\src\Test.Server` |
| Dashboard shell/tables/pagination | Tempo `dashboard\src\components\TableFrame.jsx`, `Topbar.jsx`, `Sidebar.jsx` |
| Dashboard request history + inspector | Hydra `admin-dashboard\src\views\RequestHistoryView.jsx`, `RequestDetailsModal.jsx` |
| Dashboard API explorer | Hydra `ApiExplorerView.jsx`, Conductor `ApiExplorer.jsx`, LiteGraph/HnswLite `ApiExplorer.tsx` |
| Dashboard i18n runtime | Hydra `admin-dashboard\src\i18n\runtime.js`, Tempo/Armada i18n subtree |
| Docker factory (resettable seed data) | AssistantHub `docker\factory`, LiteGraph `docker\factory` |
| SDK test harness | SharpAI `sdk\csharp\src\Test.Automated`, Verbex `sdk\csharp|python|js` |

---

## 1. Product Summary

- **Entities**: a **Container** holds **Extents**. One extent = one stored object = one key/value pair plus its metadata. Keys are caller-chosen strings, unique per container (among Active extents).
- **Value**: arbitrary binary payload (S3-compatible).
- **Metadata** (three forms): `Labels` (list of strings), `Tags` (string→string dictionary), `Object` (a single unstructured JSON object or array).
- **Dual persistence**:
  - PostgreSQL stores containers, extent records, labels, tags, leases, nodes, request history — everything needed for fast label/tag search and extent location. The JSON metadata `Object` is **not** stored in the database.
  - Extent files store *everything* (labels + tags + object + payload) in a self-describing format so a lost database can be fully **rehydrated** from raw storage.
- **Immutability**: extents are write-once. Overwrite of a key is an **atomic replace** (new extent inserted, old extent transitioned to `Deleting` and destroyed). Deletes **block new reads immediately (cluster-wide)** and wait for **all in-flight reads on every node** to finish via database read leases before physical deletion.
- **Scale-out**: stateless nodes, any number, all pointing at the same PostgreSQL database and the same (shared) extent storage. No node-to-node communication; PostgreSQL is the only coordination point.
- **No authentication anywhere.** Gating is the consuming application's job.
- **Protocols** (one server process hosts all, each independently enable/disable-able):

| Protocol | Default port | Scope |
|---|---|---|
| Native REST (Watson 7) + OpenAPI/Swagger | 8000 | Complete surface: CRUD, enumeration/search, existence, metadata, admin, request history |
| S3 (S3Server 7.3.0) | 8001 | Buckets, objects, tags only |
| Redis RESP (RedisRespServer 0.1.1) | 6379 | RESP2/RESP3, PING/PONG/HELLO/GET/SET and the command set in §11.3 |
| WebSockets (WatsonWebsocket 4.1.8) | 8002 | REST-equivalent operations over a persistent connection (JSON envelope) |
| MCP (Voltaic 0.4.0) | 8003 (HTTP `/mcp`), 8004 (TCP JSON-RPC) | REST-equivalent operations as MCP tools |

---

## 2. Decision Log

Decisions resolved with the product owner on 2026-07-23. These are settled — do not relitigate during implementation.

| # | Decision | Resolution |
|---|---|---|
| D1 | Value shape | **Binary payload + metadata sidecar.** Extent = header (key, labels, tags, JSON object, checksums) + raw payload bytes. |
| D2 | Tenancy / auth | **No tenants, no authentication.** Containers are the top-level scope. Explicit divergence from `AUTHENTICATION.md` (§3). |
| D3 | Overwrite semantics | **Atomic replace.** New extent written; DB record swapped transactionally; old extent destroyed under the delete discipline. Preserves S3 PUT / Redis SET conventions while each extent stays immutable. |
| D4 | Database providers | **Postgresql only**, behind `IMetadataDatabaseDriver` with the provider-folder pattern so more providers can be added later. All tests run against dockerized PostgreSQL. |
| D5 | Delete-blocks-reads scope | **Full cross-node coordination.** Per-read leases in PostgreSQL with TTL heartbeats; delete provably waits for every in-flight read cluster-wide (§10.4). A `Local` coordination mode exists as an explicit opt-out for single-node deployments. |
| D6 | SDK protocol scope | **REST + WebSockets** clients in C#, Python, JavaScript. S3 and RESP consumers use standard AWS/Redis SDKs (interop verified by our test suites instead). |
| D7 | Driver naming | PEPPERX.md says `PostgresMetadataDatabaseDriver`; `BACKEND_ARCHITECTURE.md` mandates the `Postgresql` spelling. Class is **`PostgresqlMetadataDatabaseDriver`** in folder `Database/Postgresql/`. |
| D8 | S3 bucket tagging | Containers carry a `Tags` dictionary (DB + REST + S3 bucket tagging) so the S3 "tags" scope is fully honored. Containers do not have labels or a metadata object. |
| D9 | Tag/metadata mutation vs. immutability | Changing labels/tags/object on an existing key (native REST metadata update, S3 PutObjectTagging) is implemented as an **extent rewrite**: read payload → write new extent with new metadata → atomic replace. Keeps extents authoritative for rehydration. Documented as a heavyweight operation. |
| D10 | Versioning, S3 multipart upload, ACLs, TTL/expiry | Out of scope for v1. S3 returns `NotImplemented`; RESP expiry options return errors (§11.3). |
| D11 | Native REST object key transport | Watson 7's parameter router (UrlMatcher 2.0.1) cannot match a greedy multi-segment path parameter, and dynamic regex routes bypass OpenAPI introspection (which the dashboard API Explorer depends on). So single-object operations use a **singular `/v1.0/containers/{container}/object` path with the key as a `?key=` query parameter** (URL-encoded, arbitrary keys). Plural `/objects` remains for list/enumerate. This keeps every native REST route OpenAPI-introspectable. The S3 protocol surface still offers the S3 path style for S3 clients (handled by S3Server's own parser). Verified empirically 2026-07-23. |

---

## 3. Explicit Divergences From Requirements Docs

Recorded per `BACKEND_ARCHITECTURE.md` ("If a future project needs to diverge from this document, that divergence should be explicit and justified").

| Requirement | Divergence | Justification |
|---|---|---|
| `AUTHENTICATION.md` (full AAA), `BACKEND_ARCHITECTURE.md` multi-tenancy (TenantId everywhere, RequestContext auth fields) | No tenants, users, credentials, sessions, RBAC, or auth pipeline. No `TenantId` columns. | PEPPERX.md states twice the system is fully unauthenticated backend infrastructure; gating belongs to the consuming application. Confirmed by owner (D2). |
| `BACKEND_ARCHITECTURE.md` four DB providers (Sqlite/Mysql/Postgresql/SqlServer) | Postgresql only. | PEPPERX.md prescribes Postgres as the shared authoritative store; interface/factory pattern preserved for future providers. Confirmed (D4). |
| `BACKEND_ARCHITECTURE.md` request capture "every HTTP request" | Captured on the native REST plane only; S3/RESP/WS/MCP planes are separate listeners and are not captured in v1. Object read/write payload bodies are excluded from body capture (path exclusion list) to protect the data path. | Performance-critical storage data plane; capture stays enabled by default for the REST/admin plane which is what the dashboard inspects. |
| `FRONTEND_ARCHITECTURE.md` login with API token | Dashboard "connect" screen takes server URL only (validated via health check); no token field. | There is no authentication to log into. All other login-screen requirements (branding, validation, error states, persistence) still apply. |
| `DASHBOARD_STYLE_AND_USABILITY.md` role-aware UI | Single implicit operator role; role badges/gating omitted. Environment/endpoint context chips still required. | No principals exist. |

Everything else in the requirements documents applies in full.

---

## 4. Technology Stack (pinned)

| Package | Version | Used by |
|---|---|---|
| `Watson` | 7.0.15 | REST hosting, typed `ApiRequest` routes, OpenAPI + Swagger UI |
| `S3Server` | 7.3.0 | S3 protocol listener (anonymous mode + optional static keypair) |
| `RedisRespServer` | 0.1.1 | RESP2/RESP3 listener/parser |
| `WatsonWebsocket` | 4.1.8 | WebSockets listener |
| `Voltaic` | 0.4.0 | MCP over Streamable HTTP + TCP JSON-RPC |
| `Npgsql` | 9.0.x | PostgreSQL access |
| `PrettyId` | 2.0.1 | K-sortable prefixed IDs |
| `SerializationHelper` | 2.0.3 | JSON serializer (Watson `ISerializationHelper`) |
| `SyslogLogging` | 2.1.0 | Console + file logging |
| `Touchstone.Core` / `.Cli` / `.XunitAdapter` / `.NunitAdapter` | 0.1.12 | Test projects |
| React / Vite / React Router | 19.x / 6.x / 7.x | Dashboard |
| i18next, react-i18next, i18next-browser-languagedetector | latest | Dashboard i18n |
| PostgreSQL (docker) | 17.x | Metadata database |

.NET: all C# projects target `net8.0;net10.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>disable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

No charting libraries, no axios, no UI kits in the dashboard (hand-rolled SVG + fetch `ApiClient` per `FRONTEND_ARCHITECTURE.md`).

---

## 5. Repository Layout

```text
PepperX/
├── src/
│   ├── PepperX.sln
│   ├── PepperX.Core/                  # domain, database, storage, services
│   ├── PepperX.Server/                # host + all five protocol surfaces
│   ├── Test.Shared/                   # Touchstone descriptors (no console output)
│   ├── Test.Automated/                # Touchstone.Cli console runner
│   ├── Test.Xunit/                    # Touchstone xUnit adapter
│   ├── Test.Nunit/                    # Touchstone NUnit adapter
│   └── Test.Performance/              # docker-backed stress/perf console app
├── sdk/
│   ├── csharp/                        # PepperX.Sdk (+ its own sln, tests, console app, README.md)
│   ├── python/                        # pepperx package (+ tests, console app, README.md)
│   └── js/                            # @pepperx/sdk (+ tests, console app, README.md)
├── dashboard/                         # React admin dashboard (Vite)
├── docker/
│   ├── compose.yaml                   # postgres + pepperx node(s) + dashboard
│   ├── compose.test.yaml              # postgres only, for local test runs
│   ├── server/Dockerfile
│   ├── dashboard/Dockerfile
│   └── factory/                       # factory settings + seed + reset.bat / reset.sh
├── assets/                            # logo.png, logo.ico, favicon
├── postman/PepperX.postman_collection.json
├── pepperx.json                       # default settings file
├── .github/workflows/tests.yaml       # optional CI (P16)
├── .gitignore
├── .dockerignore
├── README.md
├── DOCKERHUB_README.md
├── CHANGELOG.md
├── LICENSE.md                         # MIT
├── REST_API.md
├── S3_API.md
├── RESP_API.md
├── WEBSOCKETS_API.md
├── MCP_API.md
└── PEPPERX.md / PEPPERX_PLAN.md       # product spec / this plan
```

`PepperX.Core` internal layout (one type per file, always):

```text
PepperX.Core/
├── Constants.cs
├── Database/
│   ├── IMetadataDatabaseDriver.cs
│   ├── MetadataDatabaseDriverFactory.cs
│   ├── DatabaseSettings.cs
│   ├── DatabaseTypeEnum.cs
│   ├── SchemaMigration.cs
│   ├── Interfaces/
│   │   ├── IContainerMethods.cs
│   │   ├── IExtentMethods.cs
│   │   ├── IReadLeaseMethods.cs
│   │   ├── INodeMethods.cs
│   │   └── IRequestHistoryMethods.cs
│   └── Postgresql/
│       ├── PostgresqlMetadataDatabaseDriver.cs
│       ├── Implementations/           # one class per interface
│       ├── Queries/                   # one query class per entity, handwritten SQL
│       ├── Sanitizer.cs
│       └── Converters.cs
├── Enumeration/
│   ├── EnumerationQuery.cs
│   ├── EnumerationResult.cs           # EnumerationResult<T>
│   └── EnumerationOrderEnum.cs
├── Enums/                             # ExtentStateEnum, DeleteCoordinationModeEnum, ApiErrorEnum, ...
├── Helpers/
│   ├── IdGenerator.cs
│   └── HashHelper.cs
├── Models/                            # Container, Extent, NodeRecord, ReadLease, RequestHistoryEntry, ObjectMetadata
├── Requests/                          # WriteObjectRequest, UpdateMetadataRequest, ContainerCreateRequest, RequestHistoryFilter, ...
├── Responses/                         # ApiErrorResponse, ContainerResponse, ObjectWriteResponse, StatisticsResponse, RequestHistoryPage, RequestHistorySummary, ...
├── Serialization/
│   ├── PepperXSerializer.cs           # wraps SerializationHelper; Watson ISerializationHelper
│   └── StrictEnumConverterFactory.cs  # per Constellation/SharpAI reference
├── Settings/                          # PepperXSettings + one class per subsystem (§12)
├── Services/
│   ├── ContainerService.cs
│   ├── ObjectWriteService.cs
│   ├── ObjectReadService.cs
│   ├── ObjectDeleteService.cs
│   ├── SearchService.cs
│   ├── StatisticsService.cs
│   ├── RehydrationService.cs
│   ├── JanitorService.cs
│   └── NodeHeartbeatService.cs
└── Storage/
    ├── IExtentStorageDriver.cs
    ├── ExtentWriteResult.cs
    ├── ExtentPayloadStream.cs
    ├── Format/
    │   ├── ExtentFormatConstants.cs
    │   ├── ExtentHeader.cs
    │   ├── ExtentFormatWriter.cs
    │   └── ExtentFormatReader.cs
    └── Disk/
        └── DiskExtentStorageDriver.cs
```

`PepperX.Server` internal layout:

```text
PepperX.Server/
├── Program.cs                         # thin: delegates to PepperXServer
├── PepperXServer.cs                   # composition root, lifecycle, graceful shutdown
├── Api/
│   ├── Rest/
│   │   ├── HealthRoutes.cs
│   │   ├── ContainerRoutes.cs
│   │   ├── ObjectRoutes.cs
│   │   ├── SearchRoutes.cs
│   │   ├── AdminRoutes.cs
│   │   └── RequestHistoryRoutes.cs
│   ├── S3/
│   │   ├── S3ProtocolHandler.cs
│   │   ├── S3ServiceCallbacks.cs
│   │   ├── S3BucketCallbacks.cs
│   │   ├── S3ObjectCallbacks.cs
│   │   └── S3ErrorMapper.cs
│   ├── Resp/
│   │   ├── RespProtocolHandler.cs
│   │   ├── RespCommandDispatcher.cs
│   │   ├── RespConnectionState.cs
│   │   ├── RespResponseWriter.cs
│   │   └── Commands/                  # one handler class per command family
│   ├── Websockets/
│   │   ├── WebsocketProtocolHandler.cs
│   │   ├── WebsocketDispatcher.cs
│   │   ├── WsRequestEnvelope.cs
│   │   ├── WsResponseEnvelope.cs
│   │   └── WsOperationEnum.cs
│   └── Mcp/
│       ├── McpProtocolHandler.cs
│       ├── McpToolRegistrar.cs
│       └── Arguments/                 # one typed argument class per tool
└── Services/
    ├── RequestHistoryCaptureService.cs
    └── RequestHistoryPruneService.cs
```

---

## 6. Non-Negotiable Code Conformance Rules

Distilled from `CODE_STYLE.md` + `BACKEND_ARCHITECTURE.md` Code Standards. **Apply to every C# file. Reviewed at every phase gate.**

1. Namespace declaration first; **`using` directives inside the namespace block**; Microsoft/system usings first (alphabetical), then others (alphabetical).
2. **No `var`** — ever. Explicit types only.
3. **No tuples** — return typed objects; set status codes on the response object.
4. **One class or one enum per file.** No nested multi-type files. No partial classes.
5. XML documentation on **all public** classes, constructors, properties, methods, enums (include defaults/min/max and `<exception>` tags). **No** XML docs on private members/methods.
6. Public members `LikeThis`; private members `_LikeThis` (underscore + PascalCase). Constants that a developer might reasonably tune must be public properties backed by private defaults, not `const`.
7. Properties needing validation use explicit getters/setters with backing fields; null-check setters (`ArgumentNullException`), clamp numerics (`Math.Clamp`) with documented ranges.
8. Every async method takes a `CancellationToken` (unless the class holds a token/source member); check cancellation at appropriate points; **every await** in library/server code uses `.ConfigureAwait(false)`.
9. `IEnumerable`-returning methods get an async variant accepting a `CancellationToken`.
10. Specific exception types with contextual messages; custom domain exceptions where useful; exception filters where appropriate.
11. Full dispose pattern (`Dispose(bool)`, `IAsyncDisposable` where relevant, `base.Dispose()` in derived); classic `using (...) { }` blocks, **not** using declarations.
12. Nullable reference types enabled; guard clauses at method start; proactively eliminate NREs; `.FirstOrDefault()` + null check over `.First()`; `.Any()` over `.Count() > 0`; beware multiple enumeration.
13. Thread-safety documented in XML comments; `Interlocked` for simple atomics; `ReaderWriterLockSlim` for read-heavy scenarios.
14. **No `Console.WriteLine` in library code** (`PepperX.Core`, SDK libraries, `Test.Shared`). Console output belongs to console apps and runners only.
15. Regions (`Public-Members`, `Private-Members`, `Constructors-and-Factories`, `Public-Methods`, `Private-Methods`) required for files ≥ 500 lines; optional below that but keep consistent per project.
16. Typed request/response DTOs; **no `JsonElement`/`JsonNode` navigation for fixed contracts**. The only schemaless surface is the metadata `Object` property (documented).
17. Handwritten SQL lives in `Queries/` classes; parameterized via Npgsql parameters; `Sanitizer` for identifiers only. (Manually prepared SQL strings are intentional — see CODE_STYLE.md.)
18. IDs via `PrettyId` K-sortable generation with fixed prefixes from `Constants.cs` (§8.1); GUIDs only for wire-protocol needs (WS envelope ids) and scratch files.
19. Builds must be **zero errors, zero warnings** (`TreatWarningsAsErrors=true`).
20. If a README exists for the area you touched, verify it is still accurate before closing the task.

---

## 7. Architecture Overview

```text
                       ┌───────────────────────────────────────────────┐
                       │                PepperX.Server                 │
 clients ──REST──────► │ Watson 7 :8000 ──┐                            │
 aws sdk ──S3────────► │ S3Server :8001 ──┤                            │
 redis  ──RESP───────► │ RespListener :6379 ─┤   PepperX.Core services │
 ws     ──WS─────────► │ WatsonWebsocket :8002 ┼─► Container/Object/   │
 mcp    ──HTTP/TCP───► │ Voltaic :8003/:8004 ──┘   Search/Delete/etc.  │
                       │        │                        │             │
                       │        │ IMetadataDatabaseDriver│ IExtentStorageDriver
                       └────────┼────────────────────────┼─────────────┘
                                ▼                        ▼
                     PostgreSQL (authoritative)   Shared extent storage
                     containers/extents/labels/   (DiskExtentStorageDriver:
                     tags/leases/nodes/history     local path or shared mount)
```

- All five protocol handlers are thin adapters over the same `PepperX.Core` services — one semantic core, five dialects. Protocol handlers contain **no** business logic.
- Multi-node: N identical processes; each registers itself in `nodes` and heartbeats. Coordination (unique keys, replace atomicity, delete/read leases) happens entirely through PostgreSQL.
- `DiskExtentStorageDriver` writes to a configured root path. Multi-node deployments point every node at the same shared filesystem (NFS/SMB/SAN) — documented operational requirement. The `IExtentStorageDriver` seam allows object-store drivers later.

---

## 8. Data Model & Database Schema

### 8.1 ID prefixes (Constants.cs + IdGenerator)

| Entity | Prefix | Example |
|---|---|---|
| Container | `ctr_` | `ctr_2y4NpQ...` |
| Extent | `ext_` | `ext_9hLm2K...` |
| Node | `nod_` | |
| Read lease | `lse_` | |
| Request history entry | `req_` | |

PrettyId K-sortable, length 24 (matches `BACKEND_ARCHITECTURE.md` IdGenerator example). Extent IDs being K-sortable gives us creation-ordered keyset pagination for free.

### 8.2 Tables (PostgreSQL, all timestamps UTC `timestamptz`)

**`schema_migrations`** — `version int PK`, `description text`, `applied_utc`.

**`nodes`** — `id varchar(64) PK`, `hostname text`, `started_utc`, `last_heartbeat_utc`, `created_utc`. Index on `last_heartbeat_utc`.

**`containers`** — `id varchar(64) PK`, `name varchar(255) NOT NULL UNIQUE`, `tags jsonb NOT NULL DEFAULT '{}'`, `object_count bigint NOT NULL DEFAULT 0`, `total_bytes bigint NOT NULL DEFAULT 0`, `created_utc`, `last_update_utc`. Counters are maintained transactionally with every extent create/replace/delete (exact, cheap capacity stats).

**`extents`** — `id varchar(64) PK`, `container_id varchar(64) NOT NULL REFERENCES containers(id)`, `object_key varchar(1024) NOT NULL`, `state varchar(16) NOT NULL` (`Active` | `Deleting`), `size_bytes bigint NOT NULL`, `sha256 varchar(64) NOT NULL`, `content_type varchar(255)`, `storage_driver varchar(32) NOT NULL` (`Disk`), `storage_location text NOT NULL` (driver-relative path), `has_metadata_object boolean NOT NULL`, `created_utc`, `last_update_utc`.
- `UNIQUE (container_id, object_key) WHERE state = 'Active'` (partial unique index — allows old `Deleting` + new `Active` rows to coexist during replace).
- Index `(container_id, state, created_utc DESC)`; index `(container_id, state, object_key text_pattern_ops)` for prefix listing.

**`extent_labels`** — `id bigserial PK`, `extent_id varchar(64) NOT NULL REFERENCES extents(id) ON DELETE CASCADE`, `container_id varchar(64) NOT NULL`, `label varchar(512) NOT NULL`. Index `(container_id, label, extent_id)`; index `(extent_id)`.

**`extent_tags`** — `id bigserial PK`, `extent_id ... ON DELETE CASCADE`, `container_id`, `tag_key varchar(512) NOT NULL`, `tag_value text NOT NULL`. Index `(container_id, tag_key, tag_value, extent_id)`; index `(extent_id)`.

**`extent_read_leases`** — `id varchar(64) PK`, `extent_id varchar(64) NOT NULL`, `node_id varchar(64) NOT NULL`, `acquired_utc`, `expires_utc NOT NULL`. Index `(extent_id, expires_utc)`; index `(node_id)`. One row per in-flight read (§10).

**`request_history`** — per the `RequestHistoryEntry` model in `BACKEND_ARCHITECTURE.md` (id `req_`, method, path, url, status_code, duration_ms, source_ip, request/response headers `jsonb`, bodies + truncation flags + byte lengths, created/completed UTC; tenant/user/principal columns omitted per D2). Indexes on `created_utc`, `status_code`, `method`.

### 8.3 Label/tag search semantics

- Labels filter: **AND** — every requested label must be present. SQL: join/`GROUP BY extent_id HAVING COUNT(DISTINCT label) = @n`.
- Tags filter: **AND** — every requested key must exist with exactly the requested value.
- Optional `CaseInsensitive` flag applies `LOWER()` comparisons (expression indexes `LOWER(label)`, `LOWER(tag_key)`, `LOWER(tag_value)` included in migration 1).
- Combined with prefix/suffix on `object_key`, created before/after, ordering, and pagination — all server-side in one SQL statement (see `EnumerationQuery`, §11.1).

---

## 9. Extent File Format ("PXE1")

Self-describing, single file per extent. Everything needed to rebuild the database row + labels + tags lives in the header; the metadata `Object` lives **only** here.

```text
offset  size  field
0       4     magic "PXE1"
4       2     format version (ushort, = 1)
6       2     flags (bit 0: has metadata object; others reserved = 0)
8       4     header JSON length H (uint, little-endian)
12      H     header JSON (UTF-8)
12+H    N     payload bytes (N = header.SizeBytes)
12+H+N  32    SHA-256 of payload (raw bytes)
12+H+N+32 4   closing magic "1EXP" (truncation detection)
```

Header JSON (typed class `ExtentHeader`, serialized with the project serializer — **not** hand-built strings):

```json
{
  "ExtentId": "ext_...",
  "ContainerId": "ctr_...",
  "ContainerName": "photos",
  "Key": "2026/07/cat.jpg",
  "ContentType": "image/jpeg",
  "SizeBytes": 123456,
  "Sha256": "hex...",
  "Labels": ["animal", "cute"],
  "Tags": { "team": "mammals" },
  "Object": { "any": ["json", "at", "all"] },
  "CreatedUtc": "2026-07-23T00:00:00.000Z",
  "FormatVersion": 1
}
```

Rules:
- Writer streams the payload to a temp file (`{root}/.tmp/{extentId}.pxe.tmp`), hashing while copying, then writes header+payload+trailer and flushes/fsyncs, then atomically moves into place. DB insert commits **after** the file is durable; janitor removes orphan files that have no DB row (and, during rehydration, the reverse).
- Reader validates magic + version; payload checksum verification on read is configurable (`Storage.VerifyChecksumOnRead`, default false for speed; always verified during rehydration).
- Disk layout: `{root}/{containerId}/{first 2 chars after 'ext_'}/{extentId}.pxe` (fanout keeps directories small).
- `ReadRange(offset, count)` supported by seeking to `12+H+offset` — required for S3 range GETs.

---

## 10. Core Runtime Semantics

### 10.1 Write (create)

1. Validate container exists; validate key (non-empty, ≤1024 bytes UTF-8), labels/tags/object size limits (§12 settings).
2. Generate `ext_` id; stream payload through `ExtentFormatWriter` to temp file (SHA-256 computed inline); fsync; atomic move to final location.
3. Single DB transaction: guarded `INSERT` of the extent row (partial unique index enforces one Active per key) + label rows + tag rows + container counter update (`object_count + 1`, `total_bytes + size`).
4. On unique violation → this is a replace (§10.2) if the caller allows overwrite, else 409 (`SETNX`/`If-None-Match` paths).
5. On DB failure after file write → delete the file (best effort; janitor covers the crash window).

### 10.2 Replace (overwrite)

One DB transaction: `UPDATE extents SET state='Deleting' WHERE container_id=@c AND object_key=@k AND state='Active'` + `INSERT` new Active row + counter adjustment (`total_bytes += new - old`). Then the old extent goes through delete draining (§10.4 steps 2–4) asynchronously (fire-and-forget task with logging; janitor is the backstop). Serialization failures / unique violations retry with bounded backoff (`Storage.ReplaceRetryCount`, default 3).

Conditional replace (used by RESP `INCR` CAS and metadata rewrites): the `UPDATE ... WHERE id=@expectedOldId AND state='Active'` variant — 0 rows affected → concurrent modification → caller retries.

### 10.3 Read

1. Cluster mode: **single round trip** guarded lease acquisition — CTE that selects the Active extent row by `(container_id, object_key)` and inserts a lease row (`expires_utc = now() + Cluster.ReadLeaseTtl`), returning the extent metadata. Zero rows → 404 (or 410 if a `Deleting` row exists).
2. Stream payload from the storage driver (full or range).
3. Release the lease (`DELETE FROM extent_read_leases WHERE id=@leaseId`) in a `finally` path.
4. Long reads: a lease renewal timer extends `expires_utc` every `Ttl/2` while streaming (large object downloads must not lose their lease).
5. `Local` coordination mode (`Cluster.DeleteCoordinationMode = Local`): skip lease rows entirely; use an in-process per-extent `ReaderWriterLockSlim` registry instead. Setting documented as safe only for single-node deployments.

### 10.4 Delete (cluster-wide delete-blocks-reads)

1. Transaction: `UPDATE extents SET state='Deleting' WHERE ... AND state='Active' RETURNING ...` — the moment this commits, **no node can admit a new read** (lease acquisition is guarded on `state='Active'`).
2. Drain: poll `SELECT COUNT(*) FROM extent_read_leases WHERE extent_id=@id AND expires_utc > now()` every `Cluster.DeleteDrainPollMs` (default 50ms) until zero. In-flight reads finish and release; crashed readers age out via TTL (default 30s). Max wait `Cluster.DeleteDrainTimeout` (default 120s) → hand off to janitor rather than fail the delete response (delete returns success once tombstoned; physical destruction is guaranteed-eventual).
3. Physically delete the extent file via the storage driver.
4. Transaction: delete extent row (labels/tags cascade), delete stale lease rows, decrement container counters.
5. Janitor (§10.6) resumes step 3–4 for any `Deleting` rows found at startup/interval (crash recovery).

### 10.5 Rehydration

`RehydrationService`: given a storage root, enumerate `*.pxe` files, parse headers (full checksum verification), and rebuild `containers` (from header ContainerId/Name; tags from a container manifest — see below), `extents`, `extent_labels`, `extent_tags`, and counters. Modes: `Verify` (report drift, change nothing), `Repair` (add missing DB rows, remove DB rows with no backing file), `Rebuild` (truncate metadata tables, full rebuild).
- Container tags cannot be derived from extent headers → `DiskExtentStorageDriver` maintains a tiny per-container manifest file `{root}/{containerId}/container.json` (`{ Id, Name, Tags, CreatedUtc }`) written on container create/update. This keeps "full rehydration of a database based on the raw file contents" true for container tags too.
- Exposed as: CLI verb (`pepperx rehydrate --mode Rebuild`), admin REST endpoint (`POST /v1.0/admin/rehydrate`), and dashboard action.

### 10.6 Janitor + heartbeat

- `NodeHeartbeatService`: upserts this node's `last_heartbeat_utc` every `Cluster.HeartbeatIntervalSeconds` (default 5).
- `JanitorService` (interval default 60s): finishes orphaned `Deleting` extents; purges expired leases; purges leases owned by dead nodes (heartbeat older than `Cluster.NodeDeadAfterSeconds`, default 60); removes temp/orphan files older than 1h; prunes request history past retention (or this runs as its own `RequestHistoryPruneService` — either is fine, single timer preferred).

---

## 11. Protocol Surfaces (contracts)

### 11.1 Enumeration pattern (native REST, WS, MCP)

`EnumerationQuery` — follow `C:\Code\LiteGraph\HnswLite\src\HnswIndex.Server\Classes\EnumerationQuery.cs` exactly in spirit (properties, clamping, `FromQueryString`, `Validate`): `MaxResults` (1–1000, default 100), `Skip` (≥0), `ContinuationToken` (string extent id — deviation from HnswLite's Guid, documented), `Ordering` (`CreatedAscending|CreatedDescending|KeyAscending|KeyDescending`), `Prefix`, `Suffix`, `CreatedAfterUtc`, `CreatedBeforeUtc`, `Labels` (AND), `Tags` (AND), `CaseInsensitive`.

`EnumerationResult<T>` — follow `C:\Code\LiteGraph\litegraph\src\LiteGraph\EnumerationResult.cs`: `Success`, `Timestamp`, `MaxResults`, `ContinuationToken` (string), `EndOfResults`, `TotalRecords`, `RecordsRemaining`, `Objects` (`List<T>`, JsonPropertyOrder 999).

Continuation tokens are supported for `Created*` orderings (keyset on K-sortable id); `Skip` for the rest; both mutually exclusive (`Validate`).

### 11.2 Native REST API (Watson 7, port 8000)

Conventions: JSON via `PepperXSerializer`; errors are `ApiErrorResponse { Error (ApiErrorEnum), Message, StatusCode, Context? }`; status codes set explicitly on the response object; `req.CancellationToken` flows into every service/db call; **every route registered with full OpenAPI metadata** (`WithTag/WithSummary/WithDescription/WithOperationId/WithParameter/WithRequestBody/WithResponse`) exactly as `RecallDbServer.RegisterRoutes()` demonstrates; `UseOpenApi` + Swagger UI enabled; CORS Preflight + PreRouting + PostRouting hooks registered.

| # | Method + path | Purpose |
|---|---|---|
| R1 | `GET /` | Health: name, version, start time, uptime (no capture) |
| R2 | `HEAD /` | Health probe |
| R3 | `GET /v1.0/api/health` | Health for dashboard `validate` call |
| R4 | `PUT /v1.0/containers` | Create container. Body `ContainerCreateRequest { Name, Tags? }` → 201 `ContainerResponse` / 409 |
| R5 | `GET /v1.0/containers` | List containers (query: maxResults/skip/prefix/order) → `EnumerationResult<ContainerResponse>` |
| R6 | `POST /v1.0/containers/enumerate` | Body `EnumerationQuery` → `EnumerationResult<ContainerResponse>` |
| R7 | `GET /v1.0/containers/{container}` | Container detail incl. `ObjectCount`, `TotalBytes`, `Tags` / 404 |
| R8 | `HEAD /v1.0/containers/{container}` | Existence → 200/404 |
| R9 | `PUT /v1.0/containers/{container}/tags` | Replace container tags (body `Dictionary<string,string>`) — also rewrites container manifest |
| R10 | `DELETE /v1.0/containers/{container}` | Delete; 409 `NotEmpty` unless `?force=true` (force = bulk delete contents via §10.4 flow) |
| R11 | `PUT /v1.0/containers/{container}/objects/{key+}` | **Raw write.** Body = payload bytes. Headers: `Content-Type`; `x-pepperx-labels` (comma-separated); `x-pepperx-tags` (URL-encoded `k=v&k2=v2`); `x-pepperx-object` (base64 JSON, size-limited). Query `?nooverwrite=true` → 409 if key exists. → 201 `ObjectWriteResponse { ExtentId, Key, SizeBytes, Sha256, Replaced }` |
| R12 | `POST /v1.0/containers/{container}/objects/{key+}` | **Envelope write.** Body `WriteObjectRequest { ContentType, Labels, Tags, Object, DataBase64, NoOverwrite }` — for large/complex metadata → 201 `ObjectWriteResponse` |
| R13 | `GET /v1.0/containers/{container}/objects/{key+}` | Read payload (streamed; honors `Range`). Response headers echo content-type, `x-pepperx-labels`, `x-pepperx-tags`, `x-pepperx-extent-id`, `x-pepperx-sha256`; `x-pepperx-object` echoed base64 when ≤ configured header limit, else `x-pepperx-object-available: true` |
| R14 | `GET /v1.0/containers/{container}/objects/{key+}?metadata=true` | Full metadata JSON `ObjectMetadata { Key, ExtentId, ContainerId, SizeBytes, Sha256, ContentType, Labels, Tags, Object, CreatedUtc }` (no payload) |
| R15 | `HEAD /v1.0/containers/{container}/objects/{key+}` | Existence + metadata headers → 200/404 |
| R16 | `PUT /v1.0/containers/{container}/objects/{key+}/metadata` | Update labels/tags/object. Body `UpdateMetadataRequest { Labels?, Tags?, Object? }` → extent rewrite (D9) → 200 `ObjectWriteResponse` |
| R17 | `DELETE /v1.0/containers/{container}/objects/{key+}` | Delete (§10.4) → 204 / 404 |
| R18 | `GET /v1.0/containers/{container}/objects` | List objects (query: maxResults/skip/prefix/order) → `EnumerationResult<ObjectMetadata>` (Object omitted in list rows — DB-only query stays fast; flag `HasMetadataObject`) |
| R19 | `POST /v1.0/containers/{container}/objects/enumerate` | Body `EnumerationQuery` (labels/tags/prefix/suffix/dates) → `EnumerationResult<ObjectMetadata>` — **the** label/tag search endpoint |
| R20 | `POST /v1.0/objects/enumerate` | Cross-container search (same body + optional `Containers` list) — admin/ops convenience |
| R21 | `GET /v1.0/admin/stats` | `StatisticsResponse`: container count, object count, total bytes, per-container rollups, disk free/total from driver, DB size |
| R22 | `GET /v1.0/admin/nodes` | Cluster nodes + heartbeat ages |
| R23 | `POST /v1.0/admin/rehydrate` | Body `RehydrationRequest { Mode }` → runs §10.5; returns report |
| R24–R28 | `GET/GET/GET/DELETE/DELETE /v1.0/api/request-history[...]` | Exactly the five request-history endpoints + summary from `BACKEND_ARCHITECTURE.md` (list, `{id}`, `summary`, delete one, bulk delete) |
| R29 | `GET /openapi.json`, Swagger UI route | Watson `UseOpenApi()` |

`{key+}` = greedy path parameter (keys may contain `/`). Watson 7 supports greedy route parameters, so object keys are carried literally in the path — no URL-encoding scheme needed.

### 11.3 S3 API (S3Server 7.3.0, port 8001) — buckets, objects, tags only

Anonymous mode: `Service.IsAnonymousRequestAllowed => true`. Additionally `Service.GetSecretKey` returns a configurable static secret (`S3.StaticAccessKey`/`S3.StaticSecretKey`, defaults `pepperx`/`pepperx`) so signed AWS SDK clients also work. Bucket = container; object key = key.

| S3 operation | Callback | Mapping |
|---|---|---|
| ListBuckets | `Service.ListBuckets` | containers list |
| CreateBucket | `Bucket.Write` | container create (409 → `BucketAlreadyExists`) |
| DeleteBucket | `Bucket.Delete` | container delete, no force (`BucketNotEmpty`) |
| HeadBucket | `Bucket.Exists` | container exists |
| ListObjectsV1/V2 | `Bucket.Read` | key listing with prefix/marker/max-keys → `ListBucketResult` (delimiter/common-prefixes supported: computed from key listing) |
| GetBucketTagging / PutBucketTagging / DeleteBucketTagging | `Bucket.ReadTagging/WriteTagging/DeleteTagging` | container `Tags` |
| GetBucketLocation | `Bucket.ReadLocation` | static region from settings |
| PutObject | `Object.Write` | write/replace (§10.1/10.2); `x-amz-tagging` header → tags |
| GetObject | `Object.Read` | streamed read |
| GetObject w/ Range | `Object.ReadRange` | range read |
| HeadObject | `Object.Exists` | metadata headers |
| DeleteObject | `Object.Delete` | delete |
| DeleteObjects (multi) | `Object.DeleteMultiple` | per-key delete; per-key results |
| GetObjectTagging / PutObjectTagging / DeleteObjectTagging | `Object.ReadTagging/WriteTagging/DeleteTagging` | tags read; write/delete = extent rewrite (D9) |
| Everything else (ACL, versioning, multipart, retention, select, website, logging) | leave callbacks null | S3Server default error (`NotImplemented`) — documented in S3_API.md |

`S3ErrorMapper` translates core exceptions to `S3Exception` with correct S3 error codes (`NoSuchBucket`, `NoSuchKey`, `BucketAlreadyExists`, `BucketNotEmpty`, `InvalidArgument`, `InternalError`).

### 11.4 RESP API (RedisRespServer 0.1.1, port 6379)

Keyspace mapping: RESP database index `SELECT n` maps to container named `Resp.ContainerPrefix + n` (default `resp0`…`resp15`, auto-created on first write; `Resp.DatabaseCount` default 16). Values are binary payloads, content type `application/octet-stream`, no labels/tags/object. RESP2 default; `HELLO 3` switches the connection to RESP3 (map/boolean/double/null types used where appropriate). Per-connection state: selected DB, protocol version, client name.

| Command | Semantics |
|---|---|
| `PING [msg]` | `+PONG` / bulk echo |
| `ECHO msg` | bulk echo |
| `HELLO [2|3]` | RESP3 handshake; returns server/version/proto map |
| `AUTH ...` | `-ERR Client sent AUTH, but no password is set` (Redis-compatible; system is unauthenticated) |
| `SELECT n` | switch mapped container (0..DatabaseCount-1) |
| `QUIT` | `+OK`, close |
| `CLIENT SETNAME/GETNAME/ID` | minimal support |
| `COMMAND` / `COMMAND COUNT/DOCS` | minimal stub (count + names) |
| `INFO` | server section text (version, mode, keyspace counts) |
| `DBSIZE` | Active key count in mapped container |
| `FLUSHDB [ASYNC]` | bulk-delete mapped container contents |
| `GET k` | payload bulk string / nil |
| `SET k v [NX|XX]` | write/replace; `NX` → nooverwrite; `XX` → replace-only. `EX/PX/EXAT/PXAT/KEEPTTL/GET` → `-ERR ... not supported` |
| `SETNX k v` | NX write, `:1`/`:0` |
| `GETSET k v` / `SET ... GET`? | GETSET supported (read + replace); `SET ... GET` unsupported |
| `GETDEL k` | read then delete |
| `MGET k...` | array of bulk/nil |
| `MSET k v ...` | sequential writes, `+OK` |
| `DEL k...` / `UNLINK k...` | delete, return count |
| `EXISTS k...` | count |
| `STRLEN k` | size_bytes |
| `TYPE k` | `+string` / `+none` |
| `TTL k` / `PTTL k` | `:-1` / `:-2` (no expiry support) |
| `EXPIRE`/`PEXPIRE` | `-ERR ... not supported` |
| `KEYS pattern` | glob match over key listing (documented O(n)) |
| `SCAN cursor [MATCH p] [COUNT n]` | cursor = numeric skip; returns `[nextCursor, keys[]]` |
| `INCR/INCRBY/DECR/DECRBY/INCRBYFLOAT k [n]` | CAS loop: read + parse (must be integer/float else `-ERR value is not an integer or out of range`) + conditional replace on extent id; retry up to `Resp.CasRetryCount` (default 8) |

Max inline value size `Resp.MaxValueBytes` (default 64MB, clamped 1KB–512MB). All responses via `RespResponseWriter` (correct RESP2/RESP3 wire types — simple string, error, integer, bulk, array, null, map, boolean, double).

### 11.5 WebSockets API (WatsonWebsocket 4.1.8, port 8002)

Text frames, JSON envelopes, REST-equivalent operations. Payloads travel base64 (`DataBase64`) in v1; `Websocket.MaxMessageBytes` default 128MB clamped.

```json
// request
{ "RequestId": "guid", "Operation": "ObjectWrite", "Container": "photos", "Key": "a/b.bin",
  "Body": { /* typed per-operation request, e.g. WriteObjectRequest */ },
  "Query": { /* EnumerationQuery for enumerate ops */ } }
// response
{ "RequestId": "guid", "Success": true, "StatusCode": 201,
  "Error": null | { "Error": "Conflict", "Message": "..." },
  "Result": { /* typed response */ }, "DataBase64": null | "..." }
```

`WsOperationEnum`: `Health`, `ContainerCreate`, `ContainerRead`, `ContainerList`, `ContainerEnumerate`, `ContainerUpdateTags`, `ContainerDelete`, `ContainerExists`, `ObjectWrite`, `ObjectRead`, `ObjectReadMetadata`, `ObjectUpdateMetadata`, `ObjectDelete`, `ObjectExists`, `ObjectList`, `ObjectEnumerate`, `SearchEnumerate` (cross-container), `AdminStats`, `AdminNodes`. Requests are handled concurrently per connection; responses correlate by `RequestId`. Server pings on `Websocket.KeepAliveSeconds`. Malformed envelope → response with `RequestId: null`, `StatusCode: 400`.

### 11.6 MCP API (Voltaic 0.4.0, HTTP `/mcp` port 8003 + TCP JSON-RPC port 8004)

MCP server advertising `tools` capability; standard lifecycle (`initialize`, `notifications/initialized`, `ping`, `tools/list`, `tools/call`). Each tool has a typed argument class in `Api/Mcp/Arguments/` with JSON schema metadata; results return structured content (JSON of the same typed responses as REST).

Tools: `pepperx_container_create`, `pepperx_container_read`, `pepperx_container_list`, `pepperx_container_enumerate`, `pepperx_container_update_tags`, `pepperx_container_delete`, `pepperx_object_write` (base64 data), `pepperx_object_read` (returns metadata + base64 data, size-capped by `Mcp.MaxInlineBytes` default 8MB), `pepperx_object_read_metadata`, `pepperx_object_update_metadata`, `pepperx_object_delete`, `pepperx_object_exists`, `pepperx_object_enumerate`, `pepperx_search` (cross-container), `pepperx_stats`, `pepperx_nodes`. Both transports (HTTP, TCP) serve the identical registrar.

---

## 12. Settings (`pepperx.json` + `PEPPERX_*` env overrides)

Strongly typed classes, one per file, validation + clamping in setters, loaded by `SettingsManager` (JSON file → env overrides → validate → log without secrets). Shape:

```json
{
  "CreatedUtc": "2026-07-23T00:00:00.000Z",
  "Logging":   { "ConsoleLogging": true, "FileLogging": true, "LogDirectory": "logs", "LogFilename": "pepperx.log", "MinimumSeverity": "Info" },
  "Database":  { "Type": "Postgresql", "Hostname": "localhost", "Port": 5432, "DatabaseName": "pepperx", "Username": "pepperx", "Password": "pepperx", "MaxPoolSize": 100 },
  "Storage":   { "Driver": "Disk", "Disk": { "RootDirectory": "./data/extents" }, "VerifyChecksumOnRead": false, "MaxObjectBytes": 5368709120, "MaxKeyBytes": 1024, "MaxLabels": 64, "MaxLabelLength": 512, "MaxTags": 64, "MaxMetadataObjectBytes": 1048576, "ReplaceRetryCount": 3 },
  "Cluster":   { "NodeId": null, "DeleteCoordinationMode": "Cluster", "ReadLeaseTtlSeconds": 30, "HeartbeatIntervalSeconds": 5, "NodeDeadAfterSeconds": 60, "DeleteDrainPollMs": 50, "DeleteDrainTimeoutSeconds": 120, "JanitorIntervalSeconds": 60 },
  "Rest":      { "Enabled": true, "Hostname": "*", "Port": 8000, "Ssl": false, "MetadataObjectHeaderLimitBytes": 16384 },
  "S3":        { "Enabled": true, "Hostname": "*", "Port": 8001, "Ssl": false, "Region": "us-west-1", "AllowAnonymous": true, "StaticAccessKey": "pepperx", "StaticSecretKey": "pepperx" },
  "Resp":      { "Enabled": true, "Port": 6379, "DatabaseCount": 16, "ContainerPrefix": "resp", "MaxValueBytes": 67108864, "CasRetryCount": 8 },
  "Websocket": { "Enabled": true, "Hostname": "*", "Port": 8002, "MaxMessageBytes": 134217728, "KeepAliveSeconds": 30 },
  "Mcp":       { "Enabled": true, "HttpPort": 8003, "TcpPort": 8004, "MaxInlineBytes": 8388608 },
  "RequestHistory": { "Enabled": true, "MaxRequestBodyBytes": 65536, "MaxResponseBodyBytes": 65536, "RetentionDays": 30, "ExcludeBodyPaths": ["/v1.0/containers/*/objects/*"] }
}
```

Env overrides at minimum: `PEPPERX_SETTINGS_FILE`, `PEPPERX_DB_HOSTNAME|PORT|DATABASE|USERNAME|PASSWORD`, `PEPPERX_STORAGE_ROOT`, `PEPPERX_NODE_ID`, `PEPPERX_REST_PORT`, `PEPPERX_S3_PORT`, `PEPPERX_RESP_PORT`, `PEPPERX_WS_PORT`, `PEPPERX_MCP_HTTP_PORT`, `PEPPERX_MCP_TCP_PORT`.

---

# Implementation Phases

Recommended order: P00 → P01 → P02 → P03 → P04 → P05 → (P06, P07, P08, P09 in any order) → P10 → P11 → P12 → P13 → P14 → P15 → P16. Test suites (P10) should be **grown alongside** P02–P09 (write descriptors as each surface lands), then completed and hardened in P10.

---

## Phase 00 — Repository Scaffold & Housekeeping

- [x] **P00-01** Create the repository layout from §5 (empty projects + solution). `dotnet new` the seven `src/` projects; add all to `src/PepperX.sln`.
- [x] **P00-02** Set common csproj properties on every C# project: `net8.0;net10.0`, `Nullable=enable`, `ImplicitUsings=disable`, `TreatWarningsAsErrors=true`, `GenerateDocumentationFile=true` (Core, Server, Sdk).
- [x] **P00-03** Add `.gitignore` (dotnet + node + IDE + logs + data dirs) and `.dockerignore` (bin/obj/node_modules/dist/data/logs/artifacts).
- [x] **P00-04** Add `LICENSE.md` (MIT, © 2026 Joel Christner).
- [x] **P00-05** Stub root docs so the files exist and links resolve: `README.md`, `DOCKERHUB_README.md`, `CHANGELOG.md` (v1.0.0 Unreleased section), `REST_API.md`, `S3_API.md`, `RESP_API.md`, `WEBSOCKETS_API.md`, `MCP_API.md`. (Full content in P15.)
- [x] **P00-06** Add `assets/` with logo placeholder (logo.png, logo.ico, favicon.ico) — replace with real art before release.
- [x] **P00-07** Add default `pepperx.json` matching §12.
- [x] **P00-08** `git init`, initial commit.

**Conformance gates**: `dotnet build src/PepperX.sln` succeeds with zero warnings; repo root matches `REPOSITORY_REQUIREMENTS.md` file list.

---

## Phase 01 — Core Domain: Constants, IDs, Enums, Models, Settings, Serialization

Reference: `BACKEND_ARCHITECTURE.md` model/IdGenerator examples; `C:\Code\Constellation\constellation\src\Constellation.Core\Serialization\StrictEnumConverterFactory.cs`.

- [x] **P01-01** `Constants.cs`: product name/version, ID prefixes (§8.1), default ports, JSON content type, logo ASCII art (console banner), header strings (`x-pepperx-*`).
- [x] **P01-02** `Helpers/IdGenerator.cs`: PrettyId K-sortable, length 24; one method per entity (`GenerateContainerId`, `GenerateExtentId`, `GenerateNodeId`, `GenerateLeaseId`, `GenerateRequestHistoryId`).
- [x] **P01-03** `Helpers/HashHelper.cs`: streaming SHA-256 (hash-while-copying to a destination stream, returns hex), constant-time-safe hex compare not required (no secrets).
- [x] **P01-04** Enums (one file each): `ExtentStateEnum` (`Active`, `Deleting`), `DatabaseTypeEnum` (`Postgresql`), `StorageDriverTypeEnum` (`Disk`), `DeleteCoordinationModeEnum` (`Cluster`, `Local`), `EnumerationOrderEnum`, `ApiErrorEnum` (`BadRequest`, `NotFound`, `Conflict`, `NotEmpty`, `TooLarge`, `Deleting`, `InternalError`, `NotImplemented`), `RehydrationModeEnum` (`Verify`, `Repair`, `Rebuild`).
- [x] **P01-05** Models (validation-backed properties per §6 rule 7): `Container` (Id, Name [1–255, charset validated: letters/digits/dot/dash/underscore, lowercase enforced for S3 compat], Tags, ObjectCount, TotalBytes, CreatedUtc, LastUpdateUtc), `Extent` (all §8.2 columns), `ObjectMetadata` (response-facing merged view incl. Labels/Tags/Object), `NodeRecord`, `ReadLease`, `RequestHistoryEntry` (per BACKEND_ARCHITECTURE minus tenant fields).
- [x] **P01-06** Enumeration: `EnumerationOrderEnum`, `EnumerationQuery` (with `FromQueryString(NameValueCollection)` + `Validate(out string)`, clamps, label/tag parsing — mirror HnswLite), `EnumerationResult<T>` (mirror LiteGraph).
- [x] **P01-07** Requests: `ContainerCreateRequest`, `WriteObjectRequest`, `UpdateMetadataRequest`, `RehydrationRequest`, `RequestHistoryFilter` (typed — never `Dictionary<string,string>`).
- [x] **P01-08** Responses: `ApiErrorResponse`, `ContainerResponse`, `ObjectWriteResponse`, `StatisticsResponse` (+ `ContainerStatistics`), `NodeResponse`, `RehydrationReport`, `RequestHistoryPage`, `RequestHistorySummary` (+ `RequestHistoryBucket` — server fills empty buckets, per BACKEND_ARCHITECTURE).
- [x] **P01-09** Settings classes per §12 (one per file: `PepperXSettings`, `LoggingSettings`, `DatabaseSettings`, `StorageSettings`, `DiskStorageSettings`, `ClusterSettings`, `RestSettings`, `S3Settings`, `RespSettings`, `WebsocketSettings`, `McpSettings`, `RequestHistorySettings`) with clamps + XML-documented defaults/ranges; `SettingsManager` (load file → env overrides → validate; creates default file if missing).
- [x] **P01-10** Serialization: `PepperXSerializer` implementing Watson's `ISerializationHelper` over `SerializationHelper`, camel-case-insensitive read, ISO-8601 UTC, `StrictEnumConverterFactory`. The metadata `Object` property is typed `object?` and round-trips arbitrary JSON — this is the single sanctioned schemaless surface (§6 rule 16).
- [x] **P01-11** Custom exceptions: `PepperXException` base + `ContainerNotFoundException`, `ObjectNotFoundException`, `ObjectAlreadyExistsException`, `ContainerNotEmptyException`, `ObjectTooLargeException`, `ConcurrentModificationException`, `ExtentCorruptException`.

**Conformance gates**: zero-warning build; spot-audit 5 files against §6 checklist; model validation behavior covered by first Touchstone descriptors (seed `Test.Shared` with `IdentifierSuite` + `ModelValidationSuite` + `EnumerationQuerySuite` now).

---

## Phase 02 — Extent Storage Layer

Reference: HnswLite storage-driver split; §9 format spec.

- [x] **P02-01** `Storage/Format/ExtentFormatConstants.cs` (magic bytes, version, offsets), `ExtentHeader.cs` (typed, validated).
- [x] **P02-02** `ExtentFormatWriter`: stream-in payload → temp file with header/payload/trailer per §9; SHA-256 computed inline; fsync (`FileStream.Flush(true)`); returns `ExtentWriteResult { SizeBytes, Sha256, Location }`. Cancellation-aware.
- [x] **P02-03** `ExtentFormatReader`: open + validate magic/version/closing magic; parse header; expose `OpenPayloadStream()` and `OpenPayloadRangeStream(offset, count)`; optional full checksum verify.
- [x] **P02-04** `IExtentStorageDriver` interface: `WriteAsync(ExtentHeader header, Stream payload, CancellationToken)`, `ReadHeaderAsync(location)`, `OpenReadAsync(location)` / `OpenReadRangeAsync(location, offset, count)` (returns `ExtentPayloadStream`: payload stream + metadata), `DeleteAsync(location)`, `ExistsAsync(location)`, `EnumerateExtentFilesAsync(CancellationToken)` (+ `IEnumerable` sync variant per §6 rule 9 — used by rehydration), `WriteContainerManifestAsync` / `ReadContainerManifestAsync` / `DeleteContainerAsync`, `GetCapacityAsync()` (total/free bytes), `Name`/`Type` properties. Full XML docs incl. thread-safety notes.
- [x] **P02-05** `DiskExtentStorageDriver`: implements P02-04 over the fanout layout (§9); temp-then-atomic-move writes; container directory + `container.json` manifest lifecycle; orphan temp cleanup helper (called by janitor); `DriveInfo`-based capacity.
- [x] **P02-06** Touchstone `ExtentFormatSuite`: header roundtrip (labels/tags/unicode keys/deep JSON object/null object), payload integrity, truncation detection (chop trailer → corrupt error), bad magic/version, range reads, zero-byte payload, large payload (100MB streamed, not buffered).
- [x] **P02-07** Touchstone `DiskStorageDriverSuite`: write/read/delete/exists, atomic move visibility, concurrent parallel reads of one extent, manifest roundtrip, enumerate-files fidelity, capacity sanity, temp-file cleanup.

**Conformance gates**: zero-warning build; suites P02-06/07 pass via `Test.Automated`; memory profile of 100MB write/read stays flat (streamed — verified by max working-set assertion in the perf smoke descriptor).

---

## Phase 03 — Metadata Database Layer (PostgreSQL)

Reference: Verbex/NetLedger `Database\` trees; `BACKEND_ARCHITECTURE.md` Database Architecture section (adapted to interface name per D7).

- [x] **P03-01** `IMetadataDatabaseDriver`: `DatabaseTypeEnum DatabaseType`, domain interface properties (`Containers`, `Extents`, `ReadLeases`, `Nodes`, `RequestHistory`), `InitializeAsync`, `GetDatabaseSizeBytesAsync`, `CloseAsync`, `IDisposable`/`IAsyncDisposable`.
- [x] **P03-02** `MetadataDatabaseDriverFactory`: `Create(DatabaseSettings)` + `CreateAndInitializeAsync(...)`; switch on `DatabaseTypeEnum` (only `Postgresql`; default → `ArgumentException`).
- [x] **P03-03** Domain interfaces (`Database/Interfaces/`), all methods tenant-free, all async with `CancellationToken`:
  - `IContainerMethods`: `CreateAsync`, `ReadByIdAsync`, `ReadByNameAsync`, `ExistsAsync`, `EnumerateAsync(EnumerationQuery)`, `UpdateTagsAsync`, `DeleteAsync`, `AdjustCountersAsync`, `ReadAllStatisticsAsync`.
  - `IExtentMethods`: `CreateAsync(Extent, List<string> labels, Dictionary<string,string> tags)` (single tx), `ReadActiveAsync(containerId, key)`, `ReadByIdAsync`, `ExistsActiveAsync`, `MarkDeletingAsync(containerId, key)` / `MarkDeletingByIdAsync(id)` (conditional, returns row or null), `ReplaceAsync(oldKeyOrId, Extent newExtent, labels, tags)` (tombstone+insert+counters in one tx), `PurgeAsync(id)` (row+labels+tags+leases+counters), `EnumerateAsync(containerId?, EnumerationQuery)` (labels/tags/prefix/suffix/dates/order/keyset — returns `EnumerationResult<Extent>` with labels+tags hydrated), `ListDeletingAsync`, `CountAsync`.
  - `IReadLeaseMethods`: `AcquireForActiveExtentAsync(containerId, key, nodeId, ttl)` → `LeaseAcquisition { Extent, LeaseId }?` (the §10.3 single-round-trip CTE), `RenewAsync(leaseId, ttl)`, `ReleaseAsync(leaseId)`, `CountActiveForExtentAsync(extentId)`, `PurgeExpiredAsync`, `PurgeForNodeAsync(nodeId)`.
  - `INodeMethods`: `UpsertHeartbeatAsync`, `ListAsync`, `ListDeadAsync(threshold)`, `DeleteAsync`.
  - `IRequestHistoryMethods`: exactly the seven methods from `BACKEND_ARCHITECTURE.md` (`CreateAsync`, `ReadAsync`, `EnumerateAsync(RequestHistoryFilter)`, `SummarizeAsync`, `DeleteAsync`, `DeleteManyAsync`, `PruneAsync`) minus tenant parameters.
- [x] **P03-04** `SchemaMigration` model + migration runner: ordered idempotent statements, tracked in `schema_migrations`, applied inside `InitializeAsync`; Migration 1 = full §8.2 schema + indexes (incl. lower() expression indexes).
- [x] **P03-05** `Postgresql/Queries/` classes (one per entity): handwritten parameterized SQL for every method above, including the label/tag AND-intersection enumeration statement, keyset pagination, partial-unique-index-aware inserts, the guarded lease-acquisition CTE, and the request-history bucket summary (`generate_series` so **every bucket in range is emitted, including empty ones**).
- [x] **P03-06** `Postgresql/Implementations/` classes + `PostgresqlMetadataDatabaseDriver` (NpgsqlDataSource pooling; `Sanitizer` for identifiers; `Converters` for jsonb/timestamptz mapping).
- [x] **P03-07** Retry policy helper for serialization failures/deadlocks (bounded, logged).
- [x] **P03-08** Touchstone `DatabaseContainerSuite`, `DatabaseExtentSuite` (incl. replace race: two concurrent `ReplaceAsync` on one key → exactly one winner, loser gets `ConcurrentModificationException`), `DatabaseLeaseSuite` (acquire blocked on `Deleting`, TTL expiry, renew, node purge), `DatabaseEnumerationSuite` (every filter and ordering + continuation tokens + `TotalRecords`/`RecordsRemaining` correctness), `DatabaseMigrationSuite` (fresh init, double init idempotent), `DatabaseRequestHistorySuite` (CRUD, filter, summary bucket gap-fill, prune).
- [x] **P03-09** Test infrastructure: `docker/compose.test.yaml` (postgres:17, port 5433, user/pass/db `pepperx_test`) + `Test.Shared/TestEnvironment.cs` reading `PEPPERX_TEST_DB_*` env with compose defaults; suites create/drop uniquely-named schemas or databases per run for isolation. Document `docker compose -f docker/compose.test.yaml up -d` in README testing section.

**Conformance gates**: zero-warning build; all P03 suites green against dockerized Postgres via `Test.Automated`; SQL audit — every statement parameterized, no string-concatenated values (identifiers via Sanitizer only).

---

## Phase 04 — Core Services (write/read/replace/delete/search/rehydrate/janitor)

Reference: §10 semantics; Chronos service-facade pattern (services add orchestration + validation over DB methods — a legitimate seam per BACKEND_ARCHITECTURE).

- [x] **P04-01** `ContainerService`: create (name validation, manifest write), read, list/enumerate, update tags (DB + manifest), delete (empty check; `force` → iterate delete flow), exists, statistics.
- [x] **P04-02** `ObjectWriteService`: §10.1 create + §10.2 replace (incl. `NoOverwrite`), limits enforcement (§12 Storage settings), header/envelope input normalization (labels trim/dedupe, tag key validation), metadata rewrite operation (D9: lease-guarded payload copy → new extent → conditional replace).
- [x] **P04-03** `ObjectReadService`: §10.3 lease-guarded read (full + range), lease renewal timer for long streams, metadata-only read (DB row + labels/tags; `Object` fetched by reading the extent **header only** from storage — never the payload), exists.
- [x] **P04-04** `ObjectDeleteService`: §10.4 tombstone → drain → destroy → purge; bulk delete (used by force container delete, S3 DeleteObjects, RESP FLUSHDB); `Local` mode path with in-process `ReaderWriterLockSlim` registry (striped dictionary, documented thread-safety).
- [x] **P04-05** `SearchService`: container-scoped and cross-container enumeration facade returning `EnumerationResult<ObjectMetadata>`; guards `Validate()`; caps MaxResults.
- [x] **P04-06** `StatisticsService`: aggregates container counters + driver capacity + DB size + node list into `StatisticsResponse`.
- [x] **P04-07** `NodeHeartbeatService` + `JanitorService` per §10.6 (both `IDisposable`, timer-based, cancellation-aware, log-and-continue error handling).
- [x] **P04-08** `RehydrationService` per §10.5 (three modes; streaming enumeration; progress callback for CLI/REST reporting; full checksum verification; container manifest handling; counter recomputation).
- [x] **P04-09** Touchstone `ObjectLifecycleSuite`: write→read→replace→delete happy path; NoOverwrite conflict; limits (too-large object/labels/tags/metadata); read-during-delete: reader A holds stream open (slow read), delete starts → new read B gets 404/410 **immediately**, delete completes only after A finishes; replace churn under 16 parallel writers to one key (exactly-one-active invariant).
- [x] **P04-10** Touchstone `MultiNodeSemanticsSuite`: instantiate **two** full service stacks (two node ids) over one DB + one storage root; verify: read lease taken on node 1 blocks physical delete issued from node 2 until release; tombstone from node 2 immediately blocks new reads on node 1; janitor on node 2 cleans leases of a "crashed" node 1 (simulated by abandoning leases + stale heartbeat).
- [x] **P04-11** Touchstone `RehydrationSuite`: build dataset (3 containers, mixed labels/tags/objects, container tags) → snapshot DB state → wipe DB → `Rebuild` → deep-equality of containers/extents/labels/tags/counters; `Verify` reports drift after manual file removal; `Repair` fixes both directions.

**Conformance gates**: zero-warning build; P04 suites green; §6 audit of all service files; no protocol types referenced from Core (dependency direction check).

---

## Phase 05 — Server Host + Native REST + OpenAPI/Swagger + Request History

Reference: `RecallDbServer.cs` (hosting, hooks, OpenAPI config, fluent route metadata), `BACKEND_ARCHITECTURE.md` (Preflight/PostRouting, request capture, route registrars), route table §11.2.

- [x] **P05-01** `Program.cs` (thin) + `PepperXServer` composition root: settings → logging (SyslogLogging, console banner via Constants logo — allowed here, it's an app) → database factory + initialize → storage driver → services → protocol handlers (each behind its `Enabled` flag) → graceful shutdown (CTRL+C: stop listeners, dispose janitor/heartbeat, drain, dispose db). CLI verbs: default `serve`; `rehydrate --mode X [--settings path]`.
- [x] **P05-02** Watson webserver wiring: `PepperXSerializer` as serializer; `Routes.Preflight` (CORS 200 + headers), `Routes.PreRouting` (start timestamp, default content type, CORS headers), `Routes.PostRouting` (end timestamp, debug log line `METHOD url status (ms)`, request-history capture call), default route → 404 `ApiErrorResponse`. Exception route → `ApiErrorEnum` mapping (ArgumentException→400, NotFound exceptions→404, AlreadyExists/NotEmpty/Concurrent→409, TooLarge→413, else→500).
- [x] **P05-03** `UseOpenApi(api => ...)`: title/version/description/license MIT; tags for Health, Containers, Objects, Search, Admin, Request History; Swagger UI enabled. (No security schemes — unauthenticated.)
- [x] **P05-04** Route registrar classes (§5 Server layout), each `Register(Webserver)` method adding its routes **with complete fluent OpenAPI metadata** on every route (tag, summary, description, operationId, every path/query parameter, request body type, every response code — the "fully annotated" requirement). Implement R1–R23 + R29 from §11.2.
- [x] **P05-05** Streaming correctness: R11 write path streams `ctx.Request.Data` directly into `ObjectWriteService` (no buffering); R13 read path streams via chunked/content-length send with `Range` support (206/416 semantics); greedy `{key+}` routes carry keys containing `/` literally (Watson 7 supports greedy params — §11.2).
- [x] **P05-06** `RequestHistoryCaptureService` (build entry synchronously in PostRouting; redact `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `*api-key*`, `*token*` headers case-insensitively; truncate bodies per settings with `Truncated` flags + original byte counts; **skip body capture** for paths matching `RequestHistory.ExcludeBodyPaths`; dispatch insert via `Task.Run`, log-and-swallow) + `RequestHistoryPruneService` (or janitor hook) + `RequestHistoryRoutes` implementing R24–R28 exactly per BACKEND_ARCHITECTURE (list omits bodies; summary requires fromUtc/toUtc/bucketMinutes; bulk delete returns `deletedCount`; routes stay registered and return empty sets when capture disabled).
- [x] **P05-07** Touchstone `RestContainerSuite`, `RestObjectSuite` (raw + envelope writes, header metadata roundtrip incl. unicode labels/tags and base64 object, nooverwrite, metadata read/update, range reads, HEAD semantics, 404/409/413 shapes), `RestEnumerationSuite` (R6/R18/R19/R20 filters + pagination + FromQueryString parity), `RestAdminSuite` (stats, nodes, rehydrate), `RestRequestHistorySuite` (capture on, redaction, truncation, exclusion paths, summary gap-fill, bulk delete, capture-disabled empty state), `RestOpenApiSuite` (`/openapi.json` parses; **every registered route present** with operationId + responses; Swagger UI route 200).
- [x] **P05-08** Boot-smoke descriptor: start full server on ephemeral ports against test Postgres, hit `/`, write/read/delete one object, stop cleanly (no orphan timers/threads — assert via clean disposal).

**Conformance gates**: zero-warning build; P05 suites green; manual `curl` pass of every §11.2 route recorded in the phase annotation; OpenAPI completeness suite green (this is the enforcement of "fully annotated"); request-history capture adds no measurable latency to excluded data-path routes (spot-check in P11).

---

## Phase 06 — S3 Protocol

Reference: S3Server callback surface (`C:\Code\Less3\S3Server-7.0\src\S3Server\Callbacks\*.cs`), Less3 server as the worked example; mapping table §11.3.

- [x] **P06-01** `S3ProtocolHandler`: construct `S3Server` on `S3Settings`; wire `Service.IsAnonymousRequestAllowed` (true per settings) + `Service.GetSecretKey` (static keypair); lifecycle start/stop.
- [x] **P06-02** `S3ServiceCallbacks` (ListBuckets), `S3BucketCallbacks` (Write/Delete/Exists/Read incl. prefix/marker/delimiter/common-prefixes/max-keys, Read/Write/DeleteTagging, ReadLocation), `S3ObjectCallbacks` (Write incl. `x-amz-tagging` parse, Read, ReadRange, Exists, Delete, DeleteMultiple, Read/Write/DeleteTagging via D9 rewrite) — all thin adapters over Core services; streamed bodies both directions.
- [x] **P06-03** `S3ErrorMapper`: core exceptions → `S3Exception` codes per §11.3; unmapped → `InternalError` with logged detail.
- [x] **P06-04** Container-name compatibility: S3 bucket naming rules enforced by the shared `Container.Name` validation (P01-05) — verify parity; document any REST-created names that are not S3-addressable (should be none given shared validation).
- [x] **P06-05** Touchstone `S3ProtocolSuite` using **AWSSDK.S3** client (path-style, custom endpoint, static creds → exercises signed path; plus one raw anonymous `HttpClient` case): bucket create/list/head/delete + BucketNotEmpty, bucket tagging CRUD, PutObject/GetObject (binary fidelity), HeadObject, range GET (206), DeleteObject, DeleteObjects multi, object tagging CRUD (verify rewrite preserved payload + labels), NoSuchBucket/NoSuchKey error codes, multipart initiate → `NotImplemented`.
- [x] **P06-06** Cross-protocol descriptor: object written via S3 is readable via native REST with tags intact, and vice versa.

**Conformance gates**: zero-warning build; S3 suites green; `S3_API.md` skeleton updated with the exact supported/unsupported operation matrix.

---

## Phase 07 — RESP Protocol

Reference: `C:\Code\RedisRespServer\src\Redish.Server` (command handling shape), `Sample.RedisInterface`, `Test.StackExchangeRedis`; command table §11.3→§11.4.

- [x] **P07-01** `RespProtocolHandler`: `RespListener` + `RespInterface` on `RespSettings`; connection lifecycle; per-connection `RespConnectionState` (selected db, proto version, client name) keyed by client GUID.
- [x] **P07-02** `RespResponseWriter`: RESP2/RESP3 emitters for simple string, error, integer, bulk string (binary-safe), array, null (RESP2 `$-1`/RESP3 `_`), map, boolean, double — matching the connection's negotiated protocol.
- [x] **P07-03** `RespCommandDispatcher` + one handler class per command family (`Commands/` folder): full §11.4 table incl. HELLO negotiation, SELECT container mapping with lazy container auto-create, INCR-family CAS loop, KEYS glob matcher, SCAN cursor mapping, unsupported-option error messages byte-compatible with Redis phrasing where listed.
- [x] **P07-04** Binary safety end-to-end: values with 0x00/0xFF/CRLF bytes survive SET/GET roundtrip (RESP bulk strings are length-prefixed — verify no string conversions corrupt payloads).
- [x] **P07-05** Touchstone `RespRawProtocolSuite`: hand-built RESP frames over `TcpClient` asserting exact wire bytes for: PING, ECHO, HELLO 2 vs HELLO 3 reply shapes, SET/GET/DEL/EXISTS, SET NX/XX branches, unsupported EX error, MGET mixed hit/nil, INCR on non-numeric error text, SCAN cursor walk, TYPE/TTL/STRLEN, SELECT isolation between dbs, FLUSHDB, inline error for unknown command.
- [x] **P07-06** Touchstone `RespInteropSuite` using **StackExchange.Redis**: connect (handles its handshake: HELLO/CLIENT/COMMAND traffic), StringSet/StringGet incl. binary payload, KeyDelete, KeyExists, StringIncrement, MGET/MSET, DBSIZE, concurrent 32-client hammer (correctness under parallelism).
- [x] **P07-07** Concurrency descriptor: 8 parallel INCR loops × 100 on one key → final value exactly 800 (CAS proof, cluster mode).
- [x] **P07-08** Cross-protocol descriptor: RESP-SET value readable via REST (container `resp0`, content-type octet-stream) and vice versa.

**Conformance gates**: zero-warning build; RESP suites green including StackExchange.Redis interop; `redis-cli` manual smoke (PING/SET/GET/KEYS/INFO) recorded in phase annotation.

---

## Phase 08 — WebSockets Protocol

Reference: WatsonWebsocket Test.Server; envelope spec §11.5.

- [x] **P08-01** `WebsocketProtocolHandler`: `WatsonWsServer` on `WebsocketSettings`; client connect/disconnect logging; keepalive; max-message enforcement with a clean 400-style envelope error instead of connection drop where possible.
- [x] **P08-02** Envelope models (`WsRequestEnvelope`, `WsResponseEnvelope`, `WsErrorBody`, `WsOperationEnum`) — typed `Body` deserialization per operation (deserialize envelope, then bind `Body` to the operation's request type via the serializer; this two-step is the sanctioned pattern, still no hand-rolled JsonElement walking).
- [x] **P08-03** `WebsocketDispatcher`: operation → Core service mapping for every §11.5 operation; per-message `Task.Run` handling (concurrent requests per connection); correlation by `RequestId`; exception → enveloped `ApiErrorEnum` mapping (reuse P05-02 mapper).
- [x] **P08-04** Touchstone `WebsocketProtocolSuite` (using `ClientWebSocket`): every operation happy-path; error envelopes (unknown op, malformed JSON, missing RequestId, oversized message); 16 interleaved concurrent requests on one connection correlate correctly; 10MB base64 payload roundtrip byte-identical; server restart mid-connection → client sees close.
- [x] **P08-05** Cross-protocol descriptor: WS-written object readable via REST/S3 with metadata intact.

**Conformance gates**: zero-warning build; WS suites green; WEBSOCKETS_API.md envelope + operation reference drafted.

---

## Phase 09 — MCP Protocol

Reference: `LiteGraphMcpServer.cs`, Tablix `McpToolRegistrar.cs`, `C:\Code\Voltaic\README.md` (v0.4.0 API; the split is `Voltaic.Core` / `Voltaic.Mcp` / `Voltaic.A2A`); tool list §11.6.

- [x] **P09-01** `McpProtocolHandler`: Voltaic MCP server over Streamable HTTP (`/mcp`, sessions, SSE) on `Mcp.HttpPort` + TCP JSON-RPC transport on `Mcp.TcpPort`; both fed by one `McpToolRegistrar`; server info name/version from Constants; lifecycle methods (initialize/initialized/ping) via Voltaic defaults.
- [x] **P09-02** Typed argument classes (one file each, `Arguments/`) with JSON schema metadata (types, required, descriptions) for all 16 tools in §11.6; results as structured content wrapping the shared Response DTOs; `pepperx_object_read` enforces `MaxInlineBytes` with a clear too-large error directing callers to REST.
- [x] **P09-03** Touchstone `McpProtocolSuite` (Voltaic client over both HTTP and TCP): initialize handshake + capability shape; `tools/list` returns all 16 with schemas; `tools/call` happy path per tool; error content for not-found/conflict/too-large; `ping`.
- [x] **P09-04** Cross-protocol descriptor + MCP Inspector manual smoke recorded in annotation.

**Conformance gates**: zero-warning build; MCP suites green on both transports; MCP_API.md tool reference drafted.

---

## Phase 10 — Test Suite Completion (Touchstone, four runners)

Reference: `BACKEND_TEST_ARCHITECTURE.md` (project shapes verbatim), Conductor/Tempo Test.Shared organization.

- [x] **P10-01** `Test.Shared`: consolidate all suites from P01–P09 under `Suites/` with a single `PepperXSuites.All` registry (Tempo pattern). Verify: references only `Touchstone.Core` + `PepperX.Core`/`PepperX.Server`; **zero console output**; every case self-contained (creates + cleans its data; unique names via IdGenerator); skip-with-reason for anything environment-gated.
- [x] **P10-02** `Test.Automated`: `Touchstone.Cli` `ConsoleRunner.RunAsync(PepperXSuites.All, resultsPath)` with `--results` arg parsing exactly per BACKEND_TEST_ARCHITECTURE; exit codes 0/1.
- [x] **P10-03** `Test.Xunit`: `TouchstoneFactBase` RunAll fact + `TheoryData` per-descriptor theory class (both patterns, verbatim shapes from the reference doc).
- [x] **P10-04** `Test.Nunit`: `TouchstoneNunitBase` fact-style + `TouchstoneTestCaseSource` data-driven (both patterns).
- [x] **P10-05** Coverage audit vs. this plan — every REST route, every S3 operation in the mapping table, every RESP command, every WS operation, every MCP tool, every service semantic (§10), every enumeration filter has at least one descriptor; add `ProtocolParitySuite`: one canonical object written through each of the five protocols, read back through all five, metadata/payload identical everywhere.
- [x] **P10-06** Negative/robustness sweep: cancellation honored mid-write and mid-read (no orphan Active rows without files; janitor cleans temp); DB connection loss mid-operation surfaces clean 500s and recovers; storage root read-only → clean errors.
- [x] **P10-07** README testing section: docker prerequisite, `docker compose -f docker/compose.test.yaml up -d`, run commands for all four runners, JSON export, env vars.

**Conformance gates**: `dotnet run --project src/Test.Automated` fully green; `dotnet test src/Test.Xunit` and `src/Test.Nunit` green on net8.0 **and** net10.0; suite count and category summary recorded in the phase annotation.

---

## Phase 11 — Test.Performance

Reference: PEPPERX.md ("console-runnable… shows what it is doing and produces beautifully formatted results"); Touchstone CLI output style for table aesthetics.

- [ ] **P11-01** Harness: console app (`System.CommandLine`-free simple arg parsing is fine); `--compose` mode shells `docker compose -f docker/compose.test.yaml up -d --wait` for Postgres, then hosts an **in-process** server node on ephemeral ports (plus `--target <url>` mode to aim at any running node/cluster instead); `--duration`, `--concurrency`, `--workload`, `--object-size`, `--results <json>` args.
- [ ] **P11-02** Workload engine: precise concurrency control (bounded worker tasks), warmup period excluded from stats, latency capture per op into HdrHistogram-style buckets (hand-rolled reservoir is acceptable; no new heavy deps), throughput windows.
- [ ] **P11-03** Workload mixes (each runnable alone or as the default gauntlet): `write-small` (1KB), `write-large` (10MB streamed), `read-heavy` (95/5 over pre-seeded corpus), `mixed` (70r/20w/10 search), `search` (label/tag enumeration under concurrent writes), `replace-churn` (hot-key overwrite), `delete-churn` (write+delete pairs), `resp-ops` (RESP SET/GET via TcpClient), `s3-ops` (AWSSDK PutObject/GetObject) — REST is the default transport; per-protocol mixes prove protocol overhead deltas.
- [ ] **P11-04** Live progress display: per-second line or repainted block — workload name, elapsed/remaining, current ops/s, MB/s, errors (color via ANSI, redirection-aware like Touchstone.Cli).
- [ ] **P11-05** Results report: per-workload table — ops, errors, ops/s, MB/s, latency min/mean/p50/p95/p99/max (ms); environment block (node count, coordination mode, object size, concurrency, .NET version); OVERALL summary; `--results` JSON export with the same data; non-zero exit if error rate exceeds threshold.
- [ ] **P11-06** Cluster-vs-local comparison run documented: same workload with `DeleteCoordinationMode=Cluster` vs `Local` to quantify the lease overhead (numbers recorded in README performance notes).
- [ ] **P11-07** Add `Test.Performance` usage section to README.

**Conformance gates**: gauntlet completes clean on a dev machine; output reviewed for the "beautiful" bar (aligned columns, colors, summary); JSON export parses; baseline numbers recorded in the phase annotation.

---

## Phase 12 — SDKs (C#, Python, JavaScript) — REST + WebSockets

Reference: `REPOSITORY_REQUIREMENTS.md` §7 (sdk/{language} + test harness + README each); Verbex `sdk\*` layout; SharpAI C# SDK test harness. Named types everywhere — **no methods returning raw response-body strings**.

- [ ] **P12-01** `sdk/csharp`: `PepperX.Sdk.sln` with `PepperX.Sdk` library — `PepperXRestClient` (HttpClient-based; methods for every §11.2 route; streaming `Stream` overloads for object write/read; typed models mirroring Core's Requests/Responses/EnumerationQuery/EnumerationResult — duplicated into the SDK namespace, no Core project reference so the package is standalone) + `PepperXWebsocketClient` (`ClientWebSocket`; request/response correlation with `TaskCompletionSource` map, timeout, reconnect policy; same typed surface). §6 style rules apply in full. XML docs + nullable + net8.0;net10.0.
- [ ] **P12-02** `sdk/csharp` Test.Automated (Touchstone) + `Sdk.ConsoleApp` exercising every client method against a live local node (env `PEPPERX_URL` default `http://localhost:8000`); README.md with install/usage/samples for both clients.
- [ ] **P12-03** `sdk/python`: `pepperx` package (httpx sync + async `PepperXRestClient`; `websockets`-based `PepperXWebsocketClient`; dataclass/typed models with full type hints mirroring the wire contracts; streaming upload/download; exceptions mirroring ApiErrorEnum). pyproject.toml, ruff/mypy clean.
- [ ] **P12-04** `sdk/python` tests (pytest against live node, marker-gated) + `examples/console.py` exerciser + README.md.
- [ ] **P12-05** `sdk/js`: `@pepperx/sdk` TypeScript package (fetch-based `PepperXRestClient`, `ws`/browser-WebSocket `PepperXWebsocketClient`, full `.d.ts` typed models, stream support via Web Streams, ESM+CJS build via tsup or plain tsc dual build). No axios.
- [ ] **P12-06** `sdk/js` tests (vitest against live node, env-gated) + `examples/console.mjs` exerciser + README.md.
- [ ] **P12-07** Cross-SDK contract check: one script per SDK writes a canonical object (labels/tags/object/binary payload) and each other SDK reads + asserts it (rotation matrix recorded in annotation).
- [ ] **P12-08** `sdk/README.md` index describing the three SDKs + pointing S3/RESP users at standard AWS/Redis clients (D6).

**Conformance gates**: all three SDK test harnesses green against a locally running node; consoles run clean; each SDK README accurate; C# SDK zero-warning build.

---

## Phase 13 — Admin Dashboard (React)

Reference: `FRONTEND_ARCHITECTURE.md` (structure, stack, required views, ApiClient, i18n) + `DASHBOARD_STYLE_AND_USABILITY.md` (every checklist; rejection criteria). Study before coding: Tempo (shell/tables/pagination), Hydra (request history, API explorer, route-specific headers, i18n), Conductor (setup/tour), Constellation (compact health cards). This phase must NOT be skimped — iterate on UX until it clears the "control room" bar.

### 13.1 Route inventory (required before coding; keep updated)

| Route | User job | Backend resources | Table/filter needs | Actions & modals | Empty/error states |
|---|---|---|---|---|---|
| `/` (Connect) | Point dashboard at a node | `GET /` , `/v1.0/api/health` | — | Connect; recent servers list | Unreachable server, version mismatch warning |
| `/dashboard/home` | "Is PepperX healthy right now?" | stats, nodes, request-history summary | Chart range Hour/Day/Week/Month | KPI tiles (containers, objects, bytes, requests, success %, avg ms, nodes up); ActivityChart (bucket-click → filtered requests); attention list (dead nodes, recent 5xx); CTA cards (create container, open search, open explorer) | No traffic yet; stats endpoint down (partial-failure panels) |
| `/dashboard/containers` | Manage containers | R4–R10 | Above-table pagination, name prefix filter, sort, refresh, page size persistence | `+ Add` (create modal: name validation live, tags editor); row menu View / Edit Tags / View JSON / Delete (typed-name confirm; force option with count warning) | No containers → CTA; backend error + retry |
| `/dashboard/containers/:name` | Browse a container's objects | R7, R13–R19 | Key prefix search, label chips filter, tag k=v filter rows, date range, sort, server-side pagination | Object detail modal (metadata grid, labels/tags pills, JSON `Object` viewer, download payload, copy key/extent id, sha256); upload modal (file + labels/tags/object editors); row menu View / Metadata / Download / Delete (confirm) | Empty container → upload CTA; no filter matches |
| `/dashboard/search` | Cross-container metadata search | R20 | Query builder: containers multi-select, labels (AND chips), tags (k=v rows), prefix/suffix, dates, case-insensitive toggle; results table with container column | Same object detail modal; "open in container" link | No matches (state the filter in words); guidance empty state |
| `/dashboard/capacity` | Capacity + cluster health | R21, R22 | — | Per-container bytes/count bars (hand-rolled SVG), disk free/total, DB size, node cards (heartbeat age, dead highlighted); rehydrate action (mode picker + typed confirm + progress + report modal) | Single node note; stats unavailable |
| `/dashboard/requests` | Investigate traffic/failures | R24–R28 | KPI strip; ActivityChart (Hour 60×1m / Day 96×15m / Week 84×2h / Month 120×6h + refresh); filters: method, status, path contains, from/to; pagination 10–1000 (default 25) | Row → RequestDetailsModal (metadata/headers/bodies/raw JSON, copy buttons, truncation warnings); delete row; bulk delete w/ filter-in-plain-English confirm | No retained traffic vs no filter match vs capture disabled |
| `/dashboard/explorer` | Exercise the API live | `/openapi.json` + raw execute | Operation search + tag grouping | OpenAPI-driven forms (path/query/header/body from schema), execute w/ running state, response tabs (status/headers/body/timing), curl/fetch/C# snippets recomputed live, history (12, localStorage), DELETE/bulk confirm | OpenAPI missing → named error state |
| `/dashboard/settings` | Server info | `GET /`, R21, R22, settings-ish info | — | Endpoint cards for ALL five protocols (REST/S3/RESP/WS/MCP host:port, copyable), version/uptime, storage driver + root, coordination mode, theme + language controls, GitHub link | Server unreachable |

- [ ] **P13-01** Scaffold per FRONTEND_ARCHITECTURE §Project Structure: Vite + React 19 + Router 7; `context/` (AppContext with serverUrl persistence + theme; no auth token — ConnectContext validates via health), `utils/api.js` **single fetch-based ApiClient** with every endpoint method + shared query builder + normalized `{ items, totalCount, ... }` paging shape + normalized ApiError; `hooks/` (useApi, useDebounce, useLocalStorage, useApiExplorer); eslint + prettier configs; design tokens in `index.css` (light + dark from day one, semantic tokens per style doc).
- [ ] **P13-02** Shared components (build BEFORE pages): Sidebar (grouped: **Store** [Home, Containers, Search, Capacity] / **Observability** [Request History] / **Developer** [API Explorer] / **System** [Settings]; icons; active states; collapsible; footer w/ version + tour + setup relaunch), Topbar (endpoint chip copyable, health dot, theme toggle, language selector, GitHub icon link, disconnect), PageHeader (route title + operator summary — Hydra style), DataTable + TableFrame (above-table control bar: total records, visible range, page size 10/25/50/100, first/prev/jump/next/last, refresh; sortable headers w/ aria-sort; row-click guards; horizontal scroll; monospace id/key cells with `white-space: nowrap`), ActionMenu (portal-rendered, never clipped), Modal + ConfirmModal (sizes; ESC/backdrop close; focus trap; body scroll lock; NO browser confirm/alert anywhere), JsonViewer (mono, copy), CopyButton/CopyableId (checkmark success state, no layout shift), StatusBadge/MethodBadge/StatusCodePill, FilterBar, Toast, ActivityChart (hand-rolled stacked SVG per FRONTEND spec: success-on-failure rects, ~3 Y ticks, ~8 X labels, portal tooltip, `var(--color-success)`/`var(--color-danger)`, zero-fill missing buckets client-side, bucket click callback, range control + refresh).
- [ ] **P13-03** i18n foundation per I18N.md + FRONTEND §Internationalization: `src/i18n/` (index.js init before first paint, localeRegistry.js [storage key `pepperx.locale`, BCP 47, direction metadata], resources.js, formatters.js [formatNumber/Date/Time/DateTime/RelativeTime/Duration/Bytes/Percent/List — explicit locale always]), i18next + react-i18next + browser-languagedetector; `document.documentElement.lang/dir` sync; LanguageSelector on Connect + Topbar + Settings; catalogs: **en** (source), **de**, **ja**, plus runtime **pseudo-expansion** and **pseudo-RTL** locales for QA; every string in every shared component and view flows through `t()` — zero raw literals in JSX (CI grep check).
- [ ] **P13-04** Connect screen: branded (logo, product name), server URL input w/ recent-servers dropdown (localStorage), validate via health call, clear error states, loading state, language selector, note that PepperX is an unauthenticated backend.
- [ ] **P13-05** Views per the route inventory (§13.1), each with loading/empty/error/populated states, backend-driven filters (never filter only the current page), URL-persisted filter state where it helps return-to-view, auto-refresh (30s, paused while any modal is open) on Home/Capacity/Requests.
- [ ] **P13-06** First-run experience: setup wizard (detect empty store → offer "create your first container" + sample object + links to docs/SDKs; dismissal persisted per server; relaunchable from sidebar footer) + guided tour (replayable) per style doc §Setup/Onboarding.
- [ ] **P13-07** `document.title` per route; keyboard/accessibility pass (landmarks, aria-labels on icon buttons, focus rings, color-plus-text status indicators, table header scopes, reduced-motion).
- [ ] **P13-08** Dashboard Dockerfile (multi-stage node build → nginx with SPA fallback + gzip + health endpoint per FRONTEND reference) at `docker/dashboard/Dockerfile`; `npm run dev` proxy config for local work.
- [ ] **P13-09** **Mandatory visual QA** (style doc): Playwright script capturing 1280px/768px/390px × light/dark for: Connect, Home, Containers, Container detail + object modal, Search, Capacity, Requests + details modal, Explorer, Settings, sidebar collapsed, and pseudo-expansion + pseudo-RTL smoke at 1280px. Fix every clip/overlap/scroll defect found. Store artifacts under `dashboard/test-artifacts/` (gitignored) and summarize findings in the phase annotation.
- [ ] **P13-10** Dashboard unit/component tests: ApiClient (query building, error normalization), formatters (multi-locale incl. RTL pseudo), locale persistence + lang/dir sync, ActivityChart bucket zero-fill, DataTable pagination math, openApi.js flatten/defaults/snippets; `npm run lint` + `npm run build` clean.
- [ ] **P13-11** Handoff note appended to this phase's annotation: route inventory confirmation, references used and why (required by style doc), any backend gaps discovered (file them as tasks), visual QA summary.

**Conformance gates**: style-doc **Rejection Criteria** reviewed line-by-line — none apply; **Acceptance Criteria** checklist pass recorded; lint/build/tests clean; visual QA artifacts reviewed; i18n definition-of-done items for shipped locales met.

---

## Phase 14 — Docker & Factory

Reference: `REPOSITORY_REQUIREMENTS.md` (.yaml, build contexts), LiteGraph/AssistantHub `docker\factory`.

- [ ] **P14-01** `docker/server/Dockerfile`: multi-stage (sdk build/publish → `mcr.microsoft.com/dotnet/aspnet:10.0` runtime... use `runtime` image since Watson self-hosts), non-root user, `/app/data` + `/app/logs` volumes, all five ports exposed, HEALTHCHECK curl `/`.
- [ ] **P14-02** `docker/compose.yaml`: services `postgres` (17, volume, healthcheck), `pepperx1` (depends_on healthy postgres; env-config; ports 8000-8004+6379; shared `extents` volume), `pepperx2` (profile `cluster`; same extents volume + db — real two-node cluster locally), `dashboard` (port 3000). Build contexts, `.yaml` extension, no `:latest` external pins.
- [ ] **P14-03** `docker/compose.test.yaml` (postgres only, port 5433) — created in P03-09, finalize here.
- [ ] **P14-04** `docker/factory/`: `factory-settings.json` (container-friendly paths), `seed/` (seed console script or `seed.ps1`+`seed.sh` using curl — creates `photos`/`documents`/`cache` containers with tagged/labeled sample objects incl. metadata objects, plus RESP keys and request-history traffic so the dashboard demos rich), `reset.bat` + `reset.sh` (compose down -v → up -d --wait → run seed → print URLs).
- [ ] **P14-05** Verify: `reset.sh` from clean checkout → dashboard at :3000 connects to :8000, all views populated; `--profile cluster` up → `/v1.0/admin/nodes` shows 2 nodes; delete-under-read semantics hold across the two containers (manual check recorded).
- [ ] **P14-06** `.dockerignore` finalized; image sizes recorded; `DOCKERHUB_README.md` content (P15) references these images.

**Conformance gates**: cold-start `reset` works on Windows (`reset.bat`) and via WSL/bash (`reset.sh`); dashboard container rebuilt on dashboard changes (style-doc acceptance item); compose files pass `docker compose config`.

---

## Phase 15 — Documentation & Postman

Reference: `REPOSITORY_REQUIREMENTS.md`; `WRITING_DOCUMENTS.md` applies to prose docs (README/DOCKERHUB_README) — write them with voice, not template filler; API reference docs are technical documents (tables welcome).

- [ ] **P15-01** `README.md`: logo, badges, what/why (positioning: unauthenticated backend KV store with metadata search, five protocols, rehydratable), architecture diagram, quickstart (docker compose path + bare `dotnet run` path), settings reference (§12 table), protocol overview table w/ links to the five API docs, testing guide, performance harness guide, SDK pointers, dashboard screenshot(s), license.
- [ ] **P15-02** `DOCKERHUB_README.md`: README key points restyled for Docker Hub; **explicit absolute URLs** for all images (repo `assets/` raw URLs); tags, env vars, volumes, compose example.
- [ ] **P15-03** `REST_API.md`: every §11.2 route — method, path, headers, query params, request/response examples (real captured payloads), error shapes, enumeration pattern explainer, range reads, metadata size limits.
- [ ] **P15-04** `S3_API.md`: mapping table (supported/unsupported with reasons), anonymous + static-credential config, AWS CLI + AWSSDK examples (path-style config), error code mapping, D9 tagging-rewrite cost note.
- [ ] **P15-05** `RESP_API.md`: full command table w/ semantics + unsupported options, database→container mapping, RESP2/RESP3 notes, redis-cli + StackExchange.Redis examples, INCR CAS note, size limits.
- [ ] **P15-06** `WEBSOCKETS_API.md`: envelope schema, operation catalog with per-op body/result types + examples, correlation/concurrency rules, size limits, sample JS + C# snippets.
- [ ] **P15-07** `MCP_API.md`: transports (Streamable HTTP `/mcp` + TCP), lifecycle, tool catalog with argument schemas + example calls, inline-size limits, Claude Desktop / MCP Inspector configuration examples.
- [ ] **P15-08** `postman/PepperX.postman_collection.json`: folders Health/Containers/Objects/Search/Admin/Request History covering every REST route with example bodies + a `{{baseUrl}}` variable; verified by import + run against factory data.
- [ ] **P15-09** `CHANGELOG.md` v1.0.0 entry (Keep-a-Changelog style); version constants aligned (Constants.cs, csproj, package.json, SDK versions).
- [ ] **P15-10** Accuracy pass: execute every command/sample in every doc against the factory environment (CODE_STYLE.md: "If a README exists, analyze it and ensure it is accurate").

**Conformance gates**: every documented sample verified live; docs cross-link correctly; prose docs pass a WRITING_DOCUMENTS.md voice review (no template sections, no "This document provides…" openers, varied rhythm).

---

## Phase 16 — Final Conformance Sweep & Release Readiness

- [ ] **P16-01** Full §6 style audit across `src/` and `sdk/csharp` (scripted greps: `\bvar\b` in C#, tuple returns, `using` outside namespace, `Console.WriteLine` in library projects, missing XML docs via build warnings, `.Result`/`.Wait()` sync-over-async, awaits missing `ConfigureAwait(false)` in Core/Server/Sdk) — fix all findings.
- [ ] **P16-02** `dotnet build -c Release` zero warnings on net8.0 + net10.0; `npm run build` + `npm run lint` clean.
- [ ] **P16-03** Full green run: Test.Automated (results.json archived), Test.Xunit, Test.Nunit, dashboard tests, all three SDK harnesses, Test.Performance gauntlet baseline re-recorded.
- [ ] **P16-04** Fresh-clone rehearsal on a clean machine/profile: clone → `docker/factory/reset` → dashboard walk-through (connect, every nav section, table mechanics, modals, request history, explorer, theme, both locales, disconnect) → SDK console apps against it. Log any friction as tasks and fix.
- [ ] **P16-05** Rehydration fire-drill on the factory dataset: stop nodes → drop the database → `pepperx rehydrate --mode Rebuild` → full test-suite spot check + dashboard verification. This is the headline durability claim — it must demonstrably work.
- [ ] **P16-06** Optional CI: `.github/workflows/tests.yaml` per BACKEND_TEST_ARCHITECTURE (postgres service container; build, Test.Automated w/ results artifact, both dotnet test runners, dashboard lint/build).
- [ ] **P16-07** Close the Decision Log (append any decisions made during implementation), finalize CHANGELOG, tag `v1.0.0`.

**Definition of Done** — all true:
1. Every phase gate above passed and annotated.
2. All five protocols pass their correctness suites, including third-party client interop (AWSSDK.S3, StackExchange.Redis, ClientWebSocket, Voltaic client) and the five-way `ProtocolParitySuite`.
3. Delete-blocks-reads proven cluster-wide by `MultiNodeSemanticsSuite`; immutability + atomic replace proven under concurrency.
4. Database fully rehydrates from raw extents (P16-05 drill).
5. Dashboard meets every DASHBOARD_STYLE_AND_USABILITY acceptance criterion with visual QA evidence at three widths × two themes.
6. Docs and Postman verified against a live system; repo satisfies REPOSITORY_REQUIREMENTS.md file-for-file.
7. Zero build warnings anywhere; zero §6 violations.

---

## Appendix A — Suite Registry (target shape of `PepperXSuites.All`)

`Identifier`, `ModelValidation`, `Settings`, `Serialization`, `EnumerationQuery`, `ExtentFormat`, `DiskStorageDriver`, `DatabaseMigration`, `DatabaseContainer`, `DatabaseExtent`, `DatabaseLease`, `DatabaseEnumeration`, `DatabaseRequestHistory`, `ObjectLifecycle`, `MultiNodeSemantics`, `Rehydration`, `RestContainer`, `RestObject`, `RestEnumeration`, `RestAdmin`, `RestRequestHistory`, `RestOpenApi`, `BootSmoke`, `S3Protocol`, `RespRawProtocol`, `RespInterop`, `WebsocketProtocol`, `McpProtocol`, `ProtocolParity`, `Robustness`.

## Appendix B — Progress Log

| Date | Who | Note |
|---|---|---|
| 2026-07-23 | plan | Plan authored; decisions D1–D10 resolved with owner. |
