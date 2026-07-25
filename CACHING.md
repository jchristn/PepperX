# PepperX Caching Layer — Implementation Plan

Add a per-container metadata-and-data caching capability, backed by the
[`Caching`](https://www.nuget.org/packages/caching) NuGet package (**v5.0.1**), toggled and configured
per container by an administrator, with settings persisted in the metadata database.

**Status:** Not started. This document is the authoritative build guide; annotate it while
implementing.

---

## 0. How to use this document

- Work the **phases (C0–C9) in order**. Each phase has checkbox tasks `- [ ] **Cxx-yy**`; flip to
  `- [x]` as you complete them, and append a note to the [Progress Log](#appendix-a--progress-log)
  at each phase gate.
- Every phase ends with **Conformance gates** — do not close the phase until they pass.
- All C# conforms to the **Non-Negotiable Code Conformance Rules** in
  [`PEPPERX_PLAN.md` §6](PEPPERX_PLAN.md) (distilled from `c:\code\agents\requirements\CODE_STYLE.md`
  and `BACKEND_ARCHITECTURE.md`). The rules that bite hardest here are called out in
  [§8](#8-conformance-rules-that-apply-most-here).
- Reference source for the caching package: `C:\code\misc\caching` (also on NuGet at
  `Caching` 5.0.1, in the local cache).

---

## 1. Goals and scope

A container may have caching **enabled or disabled** by an administrator. When enabled, the
administrator configures:

| Setting | Maps to |
|---|---|
| **Maximum memory size** | `CacheBase.MaxMemoryBytes` + a `SizeEstimator` |
| **Maximum number of objects** | cache `capacity` |
| **Type of cache** (FIFO, LRU) | `FIFOCache` vs `LRUCache` |
| **Number of objects to evict during contention** | cache `evictCount` |
| **Maximum cacheable object size** (added — see [D3](#2-decision-log)) | per-object admission ceiling |

Behavior, per operation, against a cache-enabled container:

- **Read** — consult the cache first. A **hit** is served from the cache; a **miss** reads from
  terminal storage and hydrates the cache.
- **Write** — **write-through**: the write must land on **both** the cache and terminal storage
  before it is acknowledged to the caller.
- **Delete** — remove from the cache **first**, then delete from terminal storage.

The cache must be **compliant with the existing read/write/delete semantics** for terminal storage
(immutable extents, atomic replace, cluster-wide delete-blocks-reads).

Because caching lives in the **service layer** (`ObjectReadService` / `ObjectWriteService` /
`ObjectDeleteService`), **all five protocols** (REST, S3, RESP, WebSockets, MCP) benefit
automatically — they are thin adapters over the same services.

Out of scope: caching of enumeration/search results (always served from the database); caching for
any storage driver other than the disk driver (the seam is driver-agnostic, but only disk exists).

---

## 2. Decision Log

Resolved with the product owner before writing this plan. Settled — do not relitigate.

| # | Decision | Resolution |
|---|---|---|
| **D1** | Cross-node cache coherence | **Validate hits against the database.** The cache is in-process, so each node caches independently. On a read hit, the service cheaply looks up the container's **current active extent id** for the key (an indexed DB read the read path already performs) and serves the cached payload **only if the cached entry's extent id still matches**. A mismatch (replace or delete on another node) or an absent active extent is treated as a **miss**, and the stale entry is evicted. This gives cluster-wide correctness — no stale reads across nodes — while still skipping the expensive terminal-storage payload read on a hit. |
| **D2** | Cache hit vs read lease | **No lease on hits.** A validated hit is served straight from memory with no read lease. A delete removes the cache entry first, then runs the normal tombstone → drain → destroy for terminal storage. In-memory hits are inherently safe — they never touch the payload being destroyed — and a hit's coherence check (D1) fails the moment the active extent is tombstoned, so a deleted object cannot be served from cache. The delete-drain invariant continues to govern **all storage reads** (misses). |
| **D3** | Large objects | **Per-container "maximum cacheable object size."** Objects larger than the ceiling **bypass** the cache entirely (always read from and written straight to terminal storage, never admitted). Prevents one 5 GiB object from thrashing or exhausting node memory. Default 1 MiB; `0` means "no ceiling." |
| **D4** | What the cache stores/serves | **Payload + metadata, point reads by key.** A cache entry holds the object's payload bytes **and** its `ObjectMetadata`. Full `GET`, `HEAD`, and `GET …/metadata` are served from a validated hit; a **range** read slices a cached full payload when present, else bypasses; enumeration/search always hit the database. |
| **D5** | Settings persistence | **Per-container, in the metadata database**, on the `containers` table, added by a **new versioned migration** using `ALTER TABLE … ADD COLUMN IF NOT EXISTS` (idempotent, applied at startup). Not in `pepperx.json`. |
| **D6** | Runtime application | **Live.** Enabling, disabling, or reconfiguring a container's cache takes effect immediately: the `ContainerCacheManager` builds, disposes, or rebuilds the container's cache instance on the settings-update call. No node restart required. A rebuild drops the current cached entries (they re-hydrate on demand). |
| **D7** | Warm-up | **Lazy.** Caches start cold and hydrate on demand. No prepopulation on startup or enable. |
| **D8** | Rebuild interaction | Cache settings are database-only (D5). A full `rehydrate --mode Rebuild`, which reconstructs the database from extent storage, **resets cache settings to defaults** because they are not part of extent storage. This is documented; task **C1-06** optionally mirrors them into the container manifest so Rebuild preserves them. |

---

## 3. The `Caching` package — what we use

`CacheBase<T1, T2>` (abstract) with two concrete policies. Thread-safe, `IDisposable`.

```csharp
// Construction
new FIFOCache<string, CachedObject>(capacity, evictCount, comparer?);   // evicts oldest-inserted
new LRUCache<string, CachedObject>(capacity, evictCount, comparer?);     // evicts least-recently-used

// Memory bounding (in addition to count)
cache.MaxMemoryBytes = <bytes>;                 // 0 = no memory cap
cache.SizeEstimator  = (CachedObject co) => co.SizeBytes;

// Operations we use
bool TryGet(T1 key, out T2 val);
void AddReplace(T1 key, T2 val, DateTime? expiration = null);
void Remove(T1 key);
bool Contains(T1 key);
void Clear();
int  Count();

// Statistics (surfaced to the API/dashboard)
CacheStatistics GetStatistics();   // HitCount, MissCount, EvictionCount, ExpirationCount,
                                   // HitRate, CurrentCount, Capacity, CurrentMemoryBytes
long HitCount; long MissCount; long EvictionCount; double HitRate; long CurrentMemoryBytes;
```

- `capacity` = **maximum number of objects**; `evictCount` = **number to evict during contention**;
  `MaxMemoryBytes` = **maximum memory size**; FIFO/LRU = **type of cache**.
- We do **not** use expiration/persistence/events for v1 (D1 handles coherence, not TTL). The
  `SizeEstimator` returns each entry's byte size (payload + a fixed metadata overhead estimate) so
  the memory cap is accurate.

---

## 4. Architecture

```
                       cache-enabled container operation
                                     │
        ┌────────────────────────────┼───────────────────────────────┐
        │ ObjectReadService          │  ObjectWriteService            │  ObjectDeleteService
        │                            │                                │
 READ   │ 1. active extent id  ◄─────┤  WRITE (write-through)          │  DELETE
        │    (indexed DB read)       │  1. write extent → storage      │  1. cache.Remove(key)  ← first
        │ 2. cache.TryGet(key)       │  2. DB record (create/replace)  │  2. tombstone → drain → destroy
        │    HIT & id matches ──► serve from memory (no lease)         │     (existing terminal path)
        │    MISS / id mismatch ──► lease + storage read → hydrate     │  cross-node: other caches self-heal
        │                            │  3. if size ≤ ceiling:           │     via D1 coherence check
        │                            │     cache.AddReplace(key,...)    │
        │                            │  4. ack (both landed)            │
        └────────────────────────────┴───────────────────────────────┘
                  ContainerCacheManager  (container id → live FIFO/LRU cache)
```

### 4.1 Coherence token (D1)

The **active extent id** for `(containerId, key)` is the coherence token. The database's
`extents` table has a partial unique index `ux_extents_active_key ON (container_id, object_key)
WHERE state = 'Active'`, so `IExtentMethods.ReadActiveAsync(containerId, key)` is a single indexed
lookup. A read already performs this lookup to locate the extent; a cache hit reuses it to validate,
then **skips the read lease and the storage payload read**. Net effect vs a miss: a hit does the same
one indexed DB read, **minus** the lease insert/renew/release and **minus** the storage read. A hit
is therefore strictly cheaper than a miss, and correctness holds across nodes.

### 4.2 Read flow (pseudocode)

```
ReadAsync(container, key, offset, count):
    c = RequireContainer(container)
    if not c.Cache.Enabled:  return <existing path>
    active = Db.Extents.ReadActiveAsync(c.Id, key)          // coherence token; also the miss target
    if active == null:  cache.Remove(key); return null       // not found (also evicts stale entry)
    cache = Manager.Get(c.Id, c.Cache)
    if cache.TryGet(key, out entry) and entry.ExtentId == active.Id:
        # HIT
        payload = entry.Payload sliced by (offset,count) if a range, else full
        return ObjectReadHandle(active, MemoryPayloadStream(payload), release = no-op)   # no lease
    else:
        cache.Remove(key)  # if a stale entry was present
        # MISS: existing lease-guarded storage path
        handle = <existing ReadClusterAsync / ReadLocalAsync>
        if this is a FULL read and active.SizeBytes ≤ ceiling(c.Cache):
            bytes = read full payload into memory
            cache.AddReplace(key, CachedObject{ key, active.Id, metadata, bytes })
            return ObjectReadHandle over the in-memory bytes (lease already released)
        return handle   # range read or over-ceiling object streams straight through, uncached
```

`ReadMetadataAsync` / `ExistsAsync`: same coherence check; a validated hit returns
`entry.Metadata` / `true` without touching storage. Metadata-only misses do **not** hydrate (only
full reads and writes populate the payload+metadata entry).

### 4.3 Write flow — write-through (D-goal)

```
WriteAsync(...):
    <existing: write extent to storage (computes size), create/replace DB record → newExtentId>
    if c.Cache.Enabled and finalSize ≤ ceiling(c.Cache):
        # payload bytes were captured during the streamed write, bounded by ceiling+1 (see below)
        cache.AddReplace(key, CachedObject{ key, newExtentId, metadata, capturedBytes })
    # both storage and cache have landed → acknowledge
```

Capturing the bytes: wrap the incoming payload `Stream` in a **bounded-capture stream** that tees up
to `ceiling + 1` bytes into memory while streaming to storage (one pass, no re-read). If the object
exceeds the ceiling, discard the buffer and do not cache. Terminal storage is authoritative: if the
in-memory `AddReplace` itself were to fail (effectively only OOM), log and still acknowledge — the
durable write already succeeded — but attempt it synchronously **before** returning so a subsequent
read on the same node is a hit. `UpdateMetadataAsync` (extent rewrite) re-populates the entry with
the new extent id, updated metadata, and unchanged payload, subject to the ceiling.

### 4.4 Delete flow (D-goal, D2)

```
DeleteAsync(container, key):
    if c.Cache.Enabled:  Manager.Get(c.Id, c.Cache)?.Remove(key)   # cache first
    <existing: tombstone → drain in-flight leases → destroy storage → purge DB>
BulkDeleteContainerAsync(containerId):
    Manager.Get(containerId)?.Clear()                              # then existing per-key deletes
```

Container force-delete additionally calls `Manager.Remove(containerId)` to dispose the cache.

---

## 5. Data model & schema changes

### 5.1 Migration (new version, idempotent)

Append to `PostgresqlMigrations.All()` in
`src/PepperX.Core/Database/Postgresql/PostgresqlMigrations.cs`:

```csharp
new SchemaMigration(2, "Per-container cache settings", new List<string>
{
    "ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_enabled boolean NOT NULL DEFAULT false;",
    "ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_policy varchar(8) NOT NULL DEFAULT 'LRU';",
    "ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_max_objects integer NOT NULL DEFAULT 1000;",
    "ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_max_memory_bytes bigint NOT NULL DEFAULT 0;",
    "ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_evict_count integer NOT NULL DEFAULT 10;",
    "ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_max_object_bytes bigint NOT NULL DEFAULT 1048576;",
})
```

Migrations are version-gated by the `schema_migrations` table and applied in
`PostgresqlMetadataDatabaseDriver.RunMigrationsAsync` at startup. `ADD COLUMN IF NOT EXISTS` matches
the existing `IF NOT EXISTS` convention and is safe on a database that already has the columns.

### 5.2 Column ↔ model mapping

| Column | `ContainerCacheSettings` property | Notes |
|---|---|---|
| `cache_enabled` | `bool Enabled` | default `false` |
| `cache_policy` | `CacheEvictionPolicyEnum Policy` | `FIFO` \| `LRU`, default `LRU` |
| `cache_max_objects` | `int MaxObjects` | clamp `1 … int.MaxValue`, default 1000 |
| `cache_max_memory_bytes` | `long MaxMemoryBytes` | clamp `0 … long.MaxValue` (`0` = no cap), default 0 |
| `cache_evict_count` | `int EvictCount` | clamp `1 … MaxObjects`, default 10 |
| `cache_max_object_bytes` | `long MaxCacheableObjectBytes` | clamp `0 … long.MaxValue` (`0` = no ceiling), default 1 MiB |

### 5.3 Validation and clamping (all DB-persisted settings)

Every settings value that enters from an API body, is read back from the database, or is applied to a
live cache **must be null-checked and range-clamped** — never trusted as-is. This holds for legacy
rows written before a column existed, for hand-edited databases, and for malformed request bodies.

| Field | Null / missing → | Clamp | Cross-field rule |
|---|---|---|---|
| `Enabled` | `false` | — | — |
| `Policy` | `LRU` | must parse to `FIFO`/`LRU`; unknown string → `LRU` | — |
| `MaxObjects` | `1000` | `Math.Clamp(value, 1, MaxObjectsCeiling)` where `MaxObjectsCeiling` is a documented public tunable (default `10_000_000`) | — |
| `MaxMemoryBytes` | `0` | `Math.Clamp(value, 0, long.MaxValue)`; negative → `0` | if `> 0` and `< MaxCacheableObjectBytes`, raise the memory cap to at least the object ceiling (or reject in `Validate`) so at least one object can be admitted |
| `EvictCount` | `10` | `Math.Clamp(value, 1, MaxObjects)` | must be `≤ MaxObjects` (clamp down after `MaxObjects` is resolved) |
| `MaxCacheableObjectBytes` | `1048576` | `Math.Clamp(value, 0, long.MaxValue)`; negative → `0` | — |

Rules:

- **Setters clamp; they do not throw** (except a genuine `ArgumentNullException` on a required
  reference). Clamping keeps a running node resilient to odd stored values; `Validate(out string?
  error)` is the stricter gate used by the API layer to reject clearly-invalid **requests** with a
  400 (e.g. `EvictCount > MaxObjects`, `MaxMemoryBytes` positive but smaller than
  `MaxCacheableObjectBytes`).
- **Ordering matters:** resolve `MaxObjects` first, then clamp `EvictCount` against it.
- The clamp ceilings (`MaxObjectsCeiling`, and any others) are **public properties backed by private
  defaults** (Rule 6), documented with their ranges — not `const`.
- The `Converters.ReadContainer` DB read path routes every cache column through the same clamped
  setters (never assigns backing fields directly), so a bad row is normalized on read rather than
  propagated. Enum parsing uses `Enum.TryParse` with a safe fallback, never a cast.
- This is the pattern for **any** settings stored in the database, not just caching.

---

## 6. New and modified files

### New files (one public type per file — Rule 4)

| File | Type |
|---|---|
| `src/PepperX.Core/Enums/CacheEvictionPolicyEnum.cs` | `enum CacheEvictionPolicyEnum { FIFO, LRU }` |
| `src/PepperX.Core/Models/ContainerCacheSettings.cs` | `class ContainerCacheSettings` (validated, clamped) |
| `src/PepperX.Core/Caching/CachedObject.cs` | `class CachedObject` (key, extent id, metadata, payload, size) |
| `src/PepperX.Core/Caching/ContainerCache.cs` | `class ContainerCache : IDisposable` — wraps one `CacheBase` + its settings signature |
| `src/PepperX.Core/Caching/ContainerCacheManager.cs` | `class ContainerCacheManager : IDisposable` — container id → `ContainerCache` |
| `src/PepperX.Core/Requests/UpdateCacheSettingsRequest.cs` | `class UpdateCacheSettingsRequest` |
| `src/PepperX.Core/Responses/ContainerCacheResponse.cs` | `class ContainerCacheResponse` (settings + live stats) |
| `src/Test.Shared/Suites/DatabaseContainerCacheSuite.cs` | DB-level persistence suite |
| `src/Test.Shared/Suites/ContainerCacheSuite.cs` | service-level behavior suite |
| `src/Test.Shared/Suites/RestContainerCacheSuite.cs` | REST endpoint suite |

### Modified files

| File | Change |
|---|---|
| `src/PepperX.Core/PepperX.Core.csproj` | add `<PackageReference Include="Caching" Version="5.0.1" />` |
| `src/PepperX.Core/Models/Container.cs` | add `ContainerCacheSettings Cache` (never null) |
| `src/PepperX.Core/Responses/ContainerResponse.cs` | add `Cache`; map in `FromModel` |
| `src/PepperX.Core/Database/Postgresql/PostgresqlMigrations.cs` | migration v2 (§5.1) |
| `src/PepperX.Core/Database/Postgresql/Converters.cs` | `ReadContainer` reads the 6 cache columns |
| `src/PepperX.Core/Database/Postgresql/Implementations/PostgresqlContainerMethods.cs` | `_Columns`, `CreateAsync` INSERT, new `UpdateCacheSettingsAsync` |
| `src/PepperX.Core/Database/Interfaces/IContainerMethods.cs` | add `UpdateCacheSettingsAsync(string id, ContainerCacheSettings settings, CancellationToken)` |
| `src/PepperX.Core/Requests/ContainerCreateRequest.cs` | optional `ContainerCacheSettings? Cache` |
| `src/PepperX.Core/Services/ContainerService.cs` | persist cache settings on create; `UpdateCacheSettingsAsync`; wire manager on create/reconfigure/delete |
| `src/PepperX.Core/Services/ObjectReadService.cs` | read integration (§4.2) |
| `src/PepperX.Core/Services/ObjectWriteService.cs` | write-through integration (§4.3) |
| `src/PepperX.Core/Services/ObjectDeleteService.cs` | delete integration (§4.4) |
| `src/PepperX.Core/Storage/ExtentPayloadStream.cs` | factory/ctor to wrap an in-memory buffer (for hit serving) if not already possible |
| `src/PepperX.Server/PepperXServer.cs` | construct `ContainerCacheManager`; inject into services; dispose on shutdown |
| `src/PepperX.Server/Api/Rest/ContainerRoutes.cs` | `GET`/`PUT /v1.0/containers/{container}/cache` with full OpenAPI metadata |
| `dashboard/src/utils/api.js` | `containerCache(name)`, `updateContainerCache(name, settings)` |
| `dashboard/src/views/ContainersView.jsx` | caching fields in create + detail/edit modals; live stats in view |
| `dashboard/src/i18n/{en,de,ja}.json` | cache strings |
| `postman/PepperX.postman_collection.json` (via `scratchpad/gen_postman.py`) | cache GET/PUT in Containers folder |
| `src/Test.Shared/PepperXSuites.cs` | register the three new suites |
| `REST_API.md`, `README.md`, `CHANGELOG.md`, `dashboard/README.md` | document the capability |

---

## 7. Cache manager design

`ContainerCacheManager` (thread-safe, `IDisposable`):

- Holds a `System.Collections.Concurrent.ConcurrentDictionary<string, ContainerCache>` keyed by
  **container id**.
- `ContainerCache Get(string containerId, ContainerCacheSettings settings)` — returns the live cache
  for the container, building it lazily if absent, or **rebuilding** it if the supplied settings'
  signature differs from the built instance's (policy/capacity/evict/memory changed). Returns `null`
  when `settings.Enabled` is false (and disposes any existing instance).
- `ContainerCache? Get(string containerId)` — returns the existing instance or `null` (used by the
  delete path, which does not want to build a cache just to clear it).
- `void Configure(string containerId, ContainerCacheSettings settings)` — apply enable/disable/
  reconfigure explicitly (called by `ContainerService.UpdateCacheSettingsAsync`).
- `void Remove(string containerId)` — dispose and drop (called on container delete).
- `CacheStatistics? Statistics(string containerId)` — for the API/dashboard.
- `Dispose()` disposes every `ContainerCache`.

`ContainerCache` (`IDisposable`) wraps the chosen `FIFOCache`/`LRUCache<string, CachedObject>`, stores
the settings signature it was built from, sets `MaxMemoryBytes` + `SizeEstimator`, and exposes
`TryGet` / `AddReplace` / `Remove` / `Clear` / `Statistics`. Thread-safety is documented in XML
comments (the underlying cache is thread-safe; the manager guards build/rebuild with per-container
locking).

---

## 8. Conformance rules that apply most here

From [`PEPPERX_PLAN.md` §6](PEPPERX_PLAN.md) — all 20 apply; these are the ones this feature trips on:

- **No `var`**; **no tuples** (Rules 2, 3). Return typed DTOs; the read hit returns a real
  `ObjectReadHandle`.
- **One public type per file; no partial classes** (Rule 4) — every new type above is its own file.
- **XML docs on all public members** with defaults/ranges and `<exception>` (Rule 5); none on private.
- **Public `LikeThis`, private `_LikeThis`**; tunables are public properties over private defaults,
  not `const` (Rule 6).
- **Validated setters** with backing fields; `ArgumentNullException` null-checks; `Math.Clamp` with
  documented ranges (Rule 7) — `ContainerCacheSettings` and `UpdateCacheSettingsRequest`.
- **Every async method takes a `CancellationToken`; every `await` in Core/Server uses
  `ConfigureAwait(false)`** (Rule 8).
- **Full dispose pattern**; classic `using (...) {}` blocks (Rule 11) — manager and caches.
- **Thread-safety documented**; `Interlocked`/locks as needed (Rule 13).
- **No `Console.WriteLine` in `PepperX.Core`** (Rule 14) — use the injected `LoggingModule`.
- **Typed request/response DTOs**; no `JsonElement` navigation (Rule 16).
- **Handwritten SQL parameterized via Npgsql** (Rule 17) — the cache columns.
- **Zero-warning build**, `TreatWarningsAsErrors=true`, net8.0 + net10.0 (Rule 19).
- **Update any README you touch** (Rule 20).

---

## 9. Requirements compliance (`c:\code\agents\requirements`)

Conformance with the requirements repository is **mandatory and non-negotiable** — for this feature's
new code and for the existing code paths it touches. Where a requirement and a local reference app
conflict, the requirement document wins (per `EXAMPLE_APPLICATIONS.md`). Each phase gate re-checks the
relevant document; **C9-01/C9-07** are the final audits.

| Document | Applies to this feature via |
|---|---|
| `CODE_STYLE.md` | Every new/modified C# file — the §6 rules ([§8](#8-conformance-rules-that-apply-most-here)). Primary gate at **C9-01**. |
| `BACKEND_ARCHITECTURE.md` | Service-facade seam (cache lives in services, not protocol handlers); driver/interface boundaries; Core references no protocol types; settings/DTO conventions; migration discipline. |
| `BACKEND_TEST_ARCHITECTURE.md` | All of Phase **C8** — Touchstone descriptors, runner-agnostic (console/xUnit/NUnit), skip-on-no-DB, suite registration in `PepperXSuites.All`. |
| `FRONTEND_ARCHITECTURE.md` | Dashboard caching UI (Phase **C6**) — `ApiClient` single access point, component structure, hooks, no hardcoded colors/strings. |
| `DASHBOARD_STYLE_AND_USABILITY.md` | Caching controls and live-stats presentation (Phase **C6**) — operator-console clarity, destructive-action friction where relevant, diagnostics without raw JSON. |
| `I18N.md` | Every user-visible cache string through `t()` in en/de/ja (+ pseudo-locales); key-parity test (Phase **C6**). |
| `REPOSITORY_REQUIREMENTS.md` | File placement, `.gitignore`, no build artifacts committed (Phase **C9**). |
| `WRITING_DOCUMENTS.md` | This plan and the doc updates in Phase **C7** (accurate, executed-once commands). |
| `AUTHENTICATION.md` | **Explicit divergence (inherited):** PepperX is unauthenticated by design (`PEPPERX_PLAN.md` §3/D2). The cache endpoints are unauthenticated like every other endpoint; no new auth surface is introduced. Recorded here so the divergence stays explicit. |
| `EXAMPLE_APPLICATIONS.md` | Reference implementations to mirror; when in conflict, the requirement wins. |

**Codebase pass (not just new code):** the user directive is to verify the whole codebase against
these requirements, not only the caching additions. Task **C9-07** runs that sweep and files any
pre-existing violations it finds as follow-up tasks (fixing in-scope ones where cheap and safe;
recording the rest so they are visible rather than silently carried).

---

## Phase C0 — Core types and package

- [ ] **C0-01** Add `<PackageReference Include="Caching" Version="5.0.1" />` to
  `src/PepperX.Core/PepperX.Core.csproj`. Restore; confirm it resolves for net8.0 **and** net10.0.
- [ ] **C0-02** `CacheEvictionPolicyEnum.cs` — `enum { FIFO, LRU }` with XML docs per value.
- [ ] **C0-03** `ContainerCacheSettings.cs` — properties per §5.2 with backing fields and
  **clamped, null-safe setters exactly per §5.3** (documented ranges; clamp, do not throw, except
  `ArgumentNullException` on required references). Public clamp-ceiling tunables (`MaxObjectsCeiling`,
  default `10_000_000`) as properties over private defaults. `string BuildSignature()` returning
  policy/capacity/evict/memory/ceiling so the manager detects reconfiguration. `bool Validate(out
  string? error)` enforcing the cross-field rules (§5.3) for the API to reject bad requests with 400.
  XML docs incl. defaults/ranges. **Unit-test the clamping directly** in C8-01a.
- [ ] **C0-04** `CachedObject.cs` — `Key`, `ExtentId`, `ObjectMetadata Metadata`, `byte[] Payload`,
  computed `long SizeBytes` (`Payload.LongLength` + fixed metadata overhead estimate). Constructor
  null-checks `Key`, `ExtentId`, `Metadata`, `Payload`. Immutable after construction; XML docs.
- [ ] **C0-05** `UpdateCacheSettingsRequest.cs` — the editable fields with the **same clamped,
  null-safe setters and `Validate(out string?)`** as §5.3, plus `ContainerCacheSettings ToSettings()`
  (which itself re-clamps, so the request and the model can never disagree on bounds).
- [ ] **C0-06** `ContainerCacheResponse.cs` — settings echo + live stats (Enabled, Policy, MaxObjects,
  MaxMemoryBytes, EvictCount, MaxCacheableObjectBytes, HitCount, MissCount, HitRate, CurrentCount,
  CurrentMemoryBytes, EvictionCount). Factory `FromSettingsAndStatistics(ContainerCacheSettings,
  CacheStatistics?)` (stats null → zeros).

**Conformance gate:** zero-warning build (net8.0 + net10.0); §6 audit of the six new files.

---

## Phase C1 — Persistence (schema + driver)

- [ ] **C1-01** `Container.cs` — add `ContainerCacheSettings Cache` (never null; setter coalesces to a
  fresh default). Update its XML docs.
- [ ] **C1-02** Migration v2 in `PostgresqlMigrations.cs` exactly per §5.1.
- [ ] **C1-03** `Converters.ReadContainer` — read the six cache columns into `container.Cache`
  (parse `cache_policy` to the enum; tolerate legacy rows via the column defaults).
- [ ] **C1-04** `PostgresqlContainerMethods.cs` — extend `_Columns`; include the cache columns in
  `CreateAsync`'s INSERT (parameterized); add
  `Task<Container?> UpdateCacheSettingsAsync(string id, ContainerCacheSettings settings, CancellationToken token = default)`
  running a parameterized `UPDATE containers SET cache_… = @…, last_update_utc = now() WHERE id = @id;`
  then re-reading the row.
- [ ] **C1-05** `IContainerMethods.cs` — declare `UpdateCacheSettingsAsync` with XML docs.
- [ ] **C1-06** *(optional, D8)* Mirror cache settings into `ContainerManifest` (+ storage
  read/write) so `rehydrate --mode Rebuild` restores them. If skipped, document the reset behavior in
  `REST_API.md` rehydrate section and the dashboard hint.
- [ ] **C1-07** `ContainerResponse.cs` — add `Cache`; map in `FromModel` (deep copy).

**Conformance gate:** zero-warning build; migration applies cleanly on a fresh DB and is a no-op on an
already-migrated DB (verified in C8); `DatabaseMigrationSuite` still green.

---

## Phase C2 — Cache manager

- [ ] **C2-01** `ContainerCache.cs` — wraps a `CacheBase<string, CachedObject>` built from settings
  (FIFO/LRU by `Policy`, `capacity = MaxObjects`, `evictCount = EvictCount`), sets `MaxMemoryBytes`
  and `SizeEstimator = co => co.SizeBytes`, stores the settings signature, exposes
  `TryGet`/`AddReplace`/`Remove`/`Clear`/`Statistics`/`Signature`. Full dispose pattern; documented
  thread-safety.
- [ ] **C2-02** `ContainerCacheManager.cs` per §7: `Get(id, settings)`, `Get(id)`, `Configure(id,
  settings)`, `Remove(id)`, `Statistics(id)`, `Dispose()`. Per-container build/rebuild guarded
  (e.g. `lock` per container id or `ConcurrentDictionary.AddOrUpdate` with a rebuild check).
  Disabled settings dispose+drop any live instance. `LoggingModule?` optional ctor arg for
  build/rebuild/dispose logging.

**Conformance gate:** zero-warning build; unit-level exercise of build/rebuild/disable/remove in C8;
§6 audit (dispose, thread-safety docs, no `Console`).

---

## Phase C3 — Service integration

- [ ] **C3-01** `ExtentPayloadStream` — ensure a cache hit can be served: add a factory/ctor that
  wraps an in-memory `byte[]` (or a `MemoryStream`) so a hit can build an `ObjectReadHandle` with a
  **no-op** release. Keep checksum semantics consistent (an in-memory hit is already verified data;
  no re-verify).
- [ ] **C3-02** `ObjectReadService` — inject `ContainerCacheManager`. Implement §4.2 in `ReadAsync`
  (full + range), `ReadMetadataAsync`, `ExistsAsync`: coherence check via `ReadActiveAsync`;
  validated hit served from memory with **no lease**; miss falls through to the existing
  lease-guarded path and **hydrates** on a full read of a `≤ ceiling` object; stale entries evicted.
  Range hit slices the cached full payload; range miss streams straight through (no hydration).
- [ ] **C3-03** `ObjectWriteService` — inject the manager. Implement §4.3 write-through in `WriteAsync`
  (bounded-capture stream up to `ceiling + 1`; `AddReplace` before ack when `≤ ceiling`) and
  `UpdateMetadataAsync` (re-populate entry with new extent id + metadata). Terminal storage remains
  authoritative; cache-insert failure logs and still acknowledges.
- [ ] **C3-04** `ObjectDeleteService` — inject the manager. `Remove(key)` **first** in `DeleteAsync`
  / `DeleteByContainerIdAsync`; `Clear()` in `BulkDeleteContainerAsync`. (Container force-delete's
  `Manager.Remove(containerId)` is driven from `ContainerService` — C3-05.)
- [ ] **C3-05** `ContainerService` — inject the manager. On `CreateAsync`, persist request cache
  settings and `Configure` if enabled. New
  `Task<ContainerCacheResponse> UpdateCacheSettingsAsync(string name, UpdateCacheSettingsRequest req,
  CancellationToken)` → validate → `Db.Containers.UpdateCacheSettingsAsync` → `Manager.Configure` →
  return `ContainerCacheResponse` (with live stats). New
  `Task<ContainerCacheResponse> ReadCacheAsync(string name, CancellationToken)` → settings + stats.
  `DeleteAsync` → `Manager.Remove(containerId)`.

**Conformance gate:** zero-warning build; `ObjectLifecycleSuite` and `MultiNodeSemanticsSuite` still
green (caching disabled by default — no behavior change for existing tests); `ConfigureAwait(false)`
on every new await; dependency direction preserved (Core references no protocol types).

---

## Phase C4 — Composition

- [ ] **C4-01** `PepperXServer.StartAsync` — construct
  `ContainerCacheManager cache = new ContainerCacheManager(_Logging);` after the storage driver and
  before the object services; pass it into `ObjectDeleteService`, `ObjectReadService`,
  `ObjectWriteService`, and `ContainerService` constructors (extend those ctors).
- [ ] **C4-02** `PepperXServer.StopAsync` / dispose — dispose the manager during graceful shutdown
  (after listeners stop, alongside janitor/heartbeat).

**Conformance gate:** zero-warning build; server boots and serves with a cache-disabled container
exactly as before (boot-smoke descriptor green).

---

## Phase C5 — REST API + OpenAPI

- [ ] **C5-01** `ContainerRoutes` — `GET /v1.0/containers/{container}/cache` → `ReadCacheAsync` →
  `ContainerCacheResponse` (404 if container missing). Full fluent OpenAPI metadata (tag
  `Containers`, summary, description, path param, 200 response type, 404).
- [ ] **C5-02** `ContainerRoutes` — `PUT /v1.0/containers/{container}/cache` → body
  `UpdateCacheSettingsRequest` → `UpdateCacheSettingsAsync` → 200 `ContainerCacheResponse` (400 on
  invalid settings, 404 if missing). Full OpenAPI metadata incl. request body type.
- [ ] **C5-03** Confirm `ContainerResponse` (already carrying `Cache` from C1-07) is returned by the
  existing container read/create so clients see cache config without the extra call.
- [ ] **C5-04** `RestApiSuite`/`RestContainerCacheSuite` covers both routes (see C8).

**Conformance gate:** zero-warning build; `/openapi.json` includes both new operations with complete
parameter/response metadata; the OpenAPI completeness suite is green; manual `curl` of both routes
recorded in the Progress Log.

---

## Phase C6 — Dashboard

Follow `c:\code\agents\requirements\FRONTEND_ARCHITECTURE.md` and `DASHBOARD_STYLE_AND_USABILITY.md`;
every user-visible string flows through `t()` (en/de/ja) — the key-parity test enforces it.

- [ ] **C6-01** `api.js` — `containerCache(name)` (`GET …/cache`) and `updateContainerCache(name,
  settings)` (`PUT …/cache`).
- [ ] **C6-02** Container **create** modal (`ContainersView.jsx`) — a "Caching" section: enable
  toggle; when enabled, policy select (FIFO/LRU), max objects, max memory (bytes), evict count, max
  cacheable object size. Sent on `createContainer` (extend the API client `createContainer` to pass
  `Cache`, and `ContainerCreateRequest` server-side already accepts it from C0/C3).
- [ ] **C6-03** `ContainerDetailModal` — **view** mode shows cache settings + live statistics
  (enabled, policy, hit rate, current count, memory, evictions); **edit** mode edits the settings and
  saves via `updateContainerCache`. Loads stats via `containerCache(name)` when opened.
- [ ] **C6-04** i18n: add `cache.*` keys to `en.json` (source), `de.json`, `ja.json`; the
  pseudo-locales regenerate. Run the locale key-parity test.
- [ ] **C6-05** Dashboard component test (if a settings/among-tables test fits) and a note in
  `dashboard/README.md` describing the caching controls.

**Conformance gate:** `npm run lint` clean; `npm test` green (incl. key-parity); `npm run build`
clean; Playwright `qa/visual.mjs` + `qa/locales.mjs` clean (no console errors, no overflow, correct
`lang`/`dir`); the caching UI verified against a live cache-enabled container.

---

## Phase C7 — Postman & documentation

- [ ] **C7-01** Add to the **Containers** folder (regenerate via `scratchpad/gen_postman.py`, do not
  hand-edit the JSON): **Get cache settings** (`GET {{baseUrl}}/v1.0/containers/{{container}}/cache`)
  and **Update cache settings** (`PUT …/cache` with an example body enabling an LRU cache). Verify
  with `newman run` against a live node.
- [ ] **C7-02** `REST_API.md` — document both endpoints (params, request/response, the write-through
  / delete-first / coherence semantics, the size ceiling, and the D8 Rebuild-reset caveat) in the
  Containers/Admin section.
- [ ] **C7-03** `README.md` — a short "Per-container caching" bullet under features; `CHANGELOG.md`
  entry; `dashboard/README.md` note (from C6-05).

**Conformance gate:** `newman run` of the two new requests passes against a live node; every command
and body in the docs executed once against the factory environment (WRITING_DOCUMENTS accuracy rule).

---

## Phase C8 — Tests (Touchstone, all runners)

Follow `c:\code\agents\requirements\BACKEND_TEST_ARCHITECTURE.md`. Register every new suite in
`src/Test.Shared/PepperXSuites.cs`. Suites run under the console runner, xUnit, **and** NUnit
unchanged (runner-agnostic descriptors), and skip cleanly when PostgreSQL is unavailable. The
coverage below is intentionally exhaustive: functional behavior, **concurrency**, and **consistency**
are each their own suite so a gap in one is visible.

### C8a — Settings, persistence, migration

- [ ] **C8-01** `DatabaseContainerCacheSuite` (template: `DatabaseContainerSuite`, `DbTest.Case`):
  persist settings via `IContainerMethods.UpdateCacheSettingsAsync`; read back on `ReadByName/Id`;
  defaults on a plain-created container; enum round-trips (FIFO/LRU); create-with-cache-settings.
- [ ] **C8-01a** `ContainerCacheSettingsValidationSuite` (pure, no DB) — the §5.3 contract:
  - null/missing each field → documented default (including a null `Policy` string → `LRU`, and an
    unrecognized policy string → `LRU`);
  - out-of-range each field (negative, zero where `1` is the floor, absurdly large) → clamped to the
    documented bound;
  - cross-field: `EvictCount > MaxObjects` clamps down to `MaxObjects`; `MaxMemoryBytes` positive but
    below `MaxCacheableObjectBytes` handled per §5.3; `Validate(out error)` returns false with a
    message for the genuinely-invalid request cases and true otherwise;
  - `UpdateCacheSettingsRequest.ToSettings()` and `ContainerCacheSettings` agree on every bound (round
    a request through and confirm identical clamped output);
  - `Converters.ReadContainer` normalizes a **deliberately out-of-range stored row** (insert raw SQL
    with `cache_evict_count = -5`, `cache_policy = 'bogus'`) into clamped, valid settings on read.
- [ ] **C8-02** Migration idempotency (extend `DatabaseMigrationSuite`): after init the six `cache_*`
  columns exist with correct types/defaults; a second `InitializeAsync` is a no-op (version-gated);
  a database already carrying the columns (simulate by pre-adding them) still initializes cleanly.

### C8b — Functional behavior (`ContainerCacheSuite`, service-level via `ServiceStack`)

- [ ] **C8-03a** **Read hit does not touch storage:** enable cache, write, read (hydrate), then delete
  the underlying extent **file** from storage while leaving the DB extent Active; a second read is
  still served from cache and returns the exact bytes; `HitCount` incremented.
- [ ] **C8-03b** **Miss hydrates:** cold read → miss (from storage), then a second read → hit;
  `MissCount` then `HitCount` move accordingly.
- [ ] **C8-03c** **Write-through:** immediately after a write, a read is a hit with no intervening
  storage read; the cached bytes equal what was written.
- [ ] **C8-03d** **Delete-first:** delete evicts the cache entry before terminal deletion; a
  subsequent read misses → not-found/`Deleting`; `GetStatistics().CurrentCount` drops.
- [ ] **C8-03e** **Coherence on replace:** replace an object; a read returns the **new** payload; the
  stale entry (old extent id) is not served (extent-id mismatch → re-hydrate with the new one).
- [ ] **C8-03f** **Metadata + HEAD + exists** (D4): `ReadMetadataAsync`/`ExistsAsync` served from a
  validated hit without a storage header read; metadata reflects the current extent after a replace.
- [ ] **C8-03g** **Range reads** (D4): a range read after a full-read hit slices the cached payload
  (bytes match a storage range read); a range read that misses streams from storage and does **not**
  hydrate.
- [ ] **C8-03h** **Size ceiling boundary** (D3): an object of exactly `MaxCacheableObjectBytes` **is**
  cached; `ceiling + 1` bytes **bypasses** (never admitted; reads always miss; `CurrentCount`
  excludes it); with ceiling `0`, no admission ceiling applies.
- [ ] **C8-03i** **Bounded-capture correctness:** the write-path capture buffer produces bytes
  identical to a storage read for a `≤ ceiling` object, and is discarded (object not cached) for a
  `> ceiling` object — asserted by comparing a subsequent hit's bytes to storage.
- [ ] **C8-03j** **Eviction policy:** with small `capacity`/`evictCount`, FIFO evicts oldest-inserted
  and LRU evicts least-recently-used; assert exactly which keys survive after a crafted access pattern.
- [ ] **C8-03k** **Memory-cap eviction:** `MaxMemoryBytes` set low; inserting past it evicts so
  `CurrentMemoryBytes ≤ MaxMemoryBytes` holds; count cap and memory cap interact correctly.
- [ ] **C8-03l** **Reconfigure/disable/enable** (D6): disabling drops the cache (reads bypass to
  storage); re-enabling starts cold; changing policy or capacity rebuilds (entries dropped, new
  policy in effect); settings survive a service-stack restart (re-read from DB).
- [ ] **C8-03m** **Statistics accuracy:** hit/miss/eviction counts and `HitRate` match a scripted
  sequence of operations exactly.
- [ ] **C8-03n** **Disabled container unaffected:** with caching off, read/write/delete behave
  byte-for-byte as today and the manager holds no instance for that container.

### C8c — Concurrency (`ContainerCacheConcurrencySuite`)

Each case runs many parallel tasks and asserts no exceptions, no deadlock (bounded completion time),
and a coherent final state. Use deterministic assertions on invariants, not timing.

- [ ] **C8-04a** **Concurrent reads, same key:** N tasks read one hydrated key simultaneously; all
  receive identical, non-torn bytes; no exception; `HitCount == N` (± the initial hydrating miss).
- [ ] **C8-04b** **Concurrent writes, same key (replace churn):** M tasks write distinct payloads to
  one key; exactly one extent ends Active (existing invariant); the cache's entry for the key matches
  the winning extent id (coherence), and a final read returns that winner's bytes.
- [ ] **C8-04c** **Concurrent read + write, same key:** readers interleaved with a replacer; every
  read returns a **complete** payload that is either the old or the new object (never a torn/mixed
  buffer), and its extent id matches what the DB said was Active at read time.
- [ ] **C8-04d** **Concurrent read + delete, same key:** readers interleaved with a delete; no reader
  is served a deleted object (post-tombstone reads miss); no exception; the entry is gone at the end.
- [ ] **C8-04e** **Cache stampede:** many concurrent **misses** for the same cold key; all return
  correct bytes; the cache converges to a single coherent entry; no corruption from concurrent
  hydration/`AddReplace`.
- [ ] **C8-04f** **Concurrent reconfigure under load:** a steady stream of reads/writes while another
  task repeatedly `Configure`s (toggle policy/capacity/enable-disable); no `NullReferenceException`,
  no `ObjectDisposedException` leaking to callers, no deadlock; operations either use a valid cache or
  bypass cleanly; final settings match the last `Configure`.
- [ ] **C8-04g** **Concurrent enable/disable:** tasks flip `Enabled` while others read/write; every
  operation still returns correct data (bypass or cache); no half-built cache is ever observed.
- [ ] **C8-04h** **Cross-container isolation:** parallel load across many cache-enabled containers;
  no key or byte crosses container boundaries; each container's stats are independent.
- [ ] **C8-04i** **Manager lifecycle race:** `Remove(containerId)` (container delete) concurrent with
  in-flight reads/writes for that container; in-flight operations complete or bypass without throwing
  a disposed-cache error to the caller.

### C8d — Consistency & multi-node (`ContainerCacheConsistencySuite`)

- [ ] **C8-05a** **Coherence-token invariant (single node):** across a randomized sequence of
  write/replace/delete/read, assert after every read that a served hit's cached extent id equals the
  DB's current Active extent id for that key — the cache never serves a payload whose extent id
  differs from Active.
- [ ] **C8-05b** **Read-your-writes (single node):** every write is immediately visible to the next
  read on the same node (write-through), including after replace and after metadata update.
- [ ] **C8-05c** **Delete visibility:** after a delete, no read on the same node returns the object,
  and the entry is absent from the cache.
- [ ] **C8-06** **Two-node coherence** (template: `MultiNodeSemanticsSuite`, two `ServiceStack`s over
  one DB + one storage root, each with its **own** `ContainerCacheManager`):
  - node 1 writes+reads (hydrates node 1); node 2 reads the same key, gets the current object (node 2
    hydrates its own cache);
  - node 1 **replaces**; node 2's next read returns the **new** payload (node 2's coherence check
    detects the extent-id change → miss → re-hydrate) — no stale cross-node read;
  - node 1 **deletes**; node 2's next read **misses** (Active gone → not-found; node 2 evicts its
    stale entry);
  - both nodes' stats are independent and internally consistent.
- [ ] **C8-06a** **Write-through durability:** kill/skip the cache insert (simulate ceiling bypass or
  an injected cache-insert failure) and confirm the object is still durably in terminal storage and
  readable (cache is an accelerator, never the system of record).

### C8e — Stress / soak (optional but recommended)

- [ ] **C8-07** `ContainerCacheSoakSuite` (guarded behind an env flag so CI can skip): a sustained
  mixed workload (reads/writes/deletes + periodic reconfigure) for a bounded duration across several
  containers; assert at the end: no unhandled exceptions, `CurrentMemoryBytes ≤ MaxMemoryBytes` and
  `CurrentCount ≤ capacity` for every cache, no thread/timer leaks (clean disposal), and the
  coherence-token invariant (C8-05a) still holds on a final verification pass.
- [ ] **C8-08** *(optional)* `Test.Performance` workload `cache-read` (read-heavy against a
  cache-enabled container) recording hit-rate and latency vs the uncached `read-heavy` baseline.

**Conformance gate:** all three .NET runners green on **net8.0 and net10.0** (console/xUnit/NUnit);
new suites skip (not fail) without PostgreSQL; existing suites unaffected; the concurrency and
consistency suites (C8c/C8d) run clean repeatedly (run them ≥3× to shake out flakiness) with no
deadlocks or races.

---

## Phase C9 — Final conformance sweep & verification

- [ ] **C9-01** §6 audit across every new/modified C# file (scripted greps: `\bvar\b`, tuple returns,
  `using` outside namespace, `Console.Write*` in Core, missing `ConfigureAwait(false)`, missing XML
  docs via build warnings). Fix all findings.
- [ ] **C9-02** `dotnet build src/PepperX.sln -c Release` — **zero warnings, zero errors** on net8.0
  and net10.0. `npm run lint` + `npm run build` clean.
- [ ] **C9-03** Full green run: console runner (JSON archived), xUnit, NUnit; dashboard tests +
  Playwright sweeps.
- [ ] **C9-04** End-to-end fire drill on the Docker stack: enable caching on a container from the
  dashboard; write/read/delete objects over REST **and** over S3/RESP (proving cross-protocol reuse);
  confirm hit-rate climbs in the dashboard; reconfigure and confirm rebuild; disable and confirm
  bypass. Record in the Progress Log.
- [ ] **C9-05** Rebuild interaction (D8): with C1-06 done, confirm `rehydrate --mode Rebuild`
  preserves cache settings; without it, confirm they reset to defaults and the docs say so.
- [ ] **C9-06** Update the `PEPPERX_PLAN.md` Decision Log with a pointer to this feature (D17: "Caching
  — see CACHING.md"), and this document's Progress Log closed.
- [ ] **C9-07** **Codebase-wide requirements audit** (`c:\code\agents\requirements`), not limited to
  the caching code:
  - **`CODE_STYLE.md` sweep** across `src/` and `sdk/csharp/` — scripted greps for `\bvar\b`, tuple
    returns, `using` outside the namespace, `Console.Write*` in library projects, `.Result`/`.Wait()`
    sync-over-async, awaits missing `ConfigureAwait(false)` in Core/Server/SDK, `const` where a tunable
    property is called for, multi-type files / partial classes, missing XML docs (via build warnings).
  - **`BACKEND_ARCHITECTURE.md`** — dependency direction (Core references no protocol types), SQL in
    `Queries/`, migration discipline, DTO typing.
  - **`BACKEND_TEST_ARCHITECTURE.md`** — every suite registered and runner-agnostic; skip-on-no-DB.
  - **`FRONTEND_ARCHITECTURE.md` / `DASHBOARD_STYLE_AND_USABILITY.md` / `I18N.md`** — dashboard lint,
    no hardcoded colors/strings, key-parity, `ApiClient` single access point.
  - **`REPOSITORY_REQUIREMENTS.md`** — no build artifacts tracked; `.gitignore` correct; file layout.
  - Fix in-scope, low-risk findings immediately; for anything larger or outside this feature's blast
    radius, open a tracked follow-up task and note it in the Progress Log rather than silently
    carrying it. Record the audit outcome (clean, or the list of filed items) in the Progress Log.

**Definition of done:** every phase gate passed; caching is per-container configurable over REST, the
dashboard, and Postman; settings persist via an idempotent startup migration; read (hit/miss +
coherence), write-through, and delete-first semantics hold across nodes and protocols; all runners
green at zero warnings; docs accurate.

---

## Appendix A — Progress Log

Append one entry per phase gate: date, phase, what was verified, any deviation from this plan (record
new decisions here and in the table above).

- _(empty — begin at C0)_
