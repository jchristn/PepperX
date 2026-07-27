# PepperX REST API

The native REST surface. It is the most complete of the five protocols — every capability PepperX has
is reachable here, and the other four expose subsets shaped by their own conventions.

A running node serves interactive documentation generated from the same route definitions:

- Swagger UI — `http://localhost:8000/swagger`
- OpenAPI document — `http://localhost:8000/openapi.json`

If this file and `/openapi.json` ever disagree, the OpenAPI document is authoritative: it is produced
by the server itself.

---

## Contents

- [Conventions](#conventions)
- [Health](#health)
- [Containers](#containers)
- [Objects](#objects)
- [Metadata](#metadata)
- [Search and enumeration](#search-and-enumeration)
- [Admin](#admin)
- [Request history](#request-history)
- [Errors](#errors)

---

## Conventions

### No authentication

There is none. PepperX is backend infrastructure meant to sit behind a service that performs its own
access control. Every endpoint below is reachable by anyone who can reach the port, including the
destructive ones. Do not expose a node to an untrusted network.

### Object keys travel as a query parameter

Object keys may contain slashes, spaces, and any other character, which makes them unsuitable for a
path segment — a key like `reports/2026/q1.pdf` would be indistinguishable from a nested route. Keys
are therefore passed as `?key=`, URL-encoded:

```
GET /v1.0/containers/telemetry/object?key=reports%2F2026%2Fq1.pdf
```

Container names, which are constrained to a safe character set, do travel in the path.

### JSON casing

Request and response bodies use `PascalCase` property names, matching the .NET types they serialize
from. Query parameters are `camelCase`.

### Timestamps

All timestamps are UTC, ISO 8601, and named with a `Utc` suffix (`CreatedUtc`, `LastHeartbeatUtc`).
The server neither accepts nor emits local times.

### Content types

Request and response bodies are `application/json` unless stated otherwise. Object payloads are
transferred as raw bytes with whatever content type the object was written with.

---

## Health

### `GET /`

Product identity and uptime. This is what a client should call to confirm it is talking to PepperX
rather than some other service on the same port.

```json
{
  "Name": "PepperX",
  "Version": "1.0.0",
  "StartTimeUtc": "2026-07-23T23:27:45.895895Z",
  "UptimeMs": 29133.9071
}
```

### `HEAD /`

Returns `200` when the process is running. No body.

### `GET /v1.0/api/health`

Health check for monitors and orchestrators.

```json
{ "Status": "Healthy", "Product": "PepperX", "Version": "1.0.0" }
```

This is the endpoint the Docker healthcheck uses. It reports process liveness — it does not probe the
database or storage, so a node can report healthy while PostgreSQL is down. Use `GET /v1.0/admin/stats`
if you need to know whether the node's dependencies are actually reachable.

---

## Containers

A container holds objects and maps one-to-one onto an S3 bucket. Names must be 3–63 characters of
lowercase letters, digits, and hyphens, starting and ending with a letter or digit — the S3 bucket
naming rules, so that a container created over REST is addressable over S3.

### `PUT /v1.0/containers`

Create a container.

```bash
curl -X PUT http://localhost:8000/v1.0/containers \
  -H 'Content-Type: application/json' \
  -d '{"Name":"telemetry","Tags":{"team":"platform","env":"prod"}}'
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `Name` | string | yes | Must satisfy the naming rules above |
| `Tags` | object | no | String-to-string map |
| `Cache` | object | no | Per-container [cache settings](#put-v10containerscontainercache). **When omitted, the container defaults to caching enabled (LRU).** Pass `{"Enabled":false}` to opt out |
| `RespDatabaseIndex` | int | no | Claim a [RESP database index](#put-v10containerscontainerresp-index) for this container. Must be unique across containers; a conflict fails with `409` |
| `MultipartUploadExpiryDays` | int | no | Per-container [multipart-upload expiry](#put-v10containerscontainermultipart-expiry) in days, `1`–`365`. When omitted or `null`, the container inherits the system-wide `S3.MultipartUploadExpiryDays` default |

`201` with the container on success. `409 Conflict` if the name is taken.

```json
{
  "Id": "ctr_mry4fbo6_4XrvQxXgOey",
  "Name": "telemetry",
  "Tags": { "team": "platform", "env": "prod" },
  "ObjectCount": 0,
  "TotalBytes": 0,
  "CreatedUtc": "2026-07-23T23:07:00.682678Z",
  "LastUpdateUtc": "2026-07-23T23:07:00.682678Z",
  "RespDatabaseIndex": null,
  "MultipartUploadExpiryDays": null,
  "Cache": {
    "Enabled": true,
    "Policy": "LRU",
    "MaxObjects": 1000,
    "MaxMemoryBytes": 268435456,
    "EvictCount": 10,
    "MaxCacheableObjectBytes": 1048576
  }
}
```

### `GET /v1.0/containers`

List containers. Accepts the [enumeration query parameters](#via-query-string).

### `POST /v1.0/containers/enumerate`

The same listing with the query in a JSON body, which is the form to use when filters get long enough
to strain a URL. See [search and enumeration](#search-and-enumeration).

### `GET /v1.0/containers/{container}`

Read one container by name, including its live object count and byte total.

### `HEAD /v1.0/containers/{container}`

`200` if it exists, `404` if not. No body.

### `PUT /v1.0/containers/{container}/tags`

Replace a container's tags. The body is the complete tag map — this is a replace, not a merge, so
sending `{}` clears them.

```bash
curl -X PUT http://localhost:8000/v1.0/containers/telemetry/tags \
  -H 'Content-Type: application/json' \
  -d '{"team":"platform","env":"staging"}'
```

### `GET /v1.0/containers/{container}/cache`

Read a container's cache configuration together with this node's live cache statistics. `404` if the
container does not exist.

```json
{
  "Enabled": true,
  "Policy": "LRU",
  "MaxObjects": 1000,
  "MaxMemoryBytes": 268435456,
  "EvictCount": 10,
  "MaxCacheableObjectBytes": 1048576,
  "HitCount": 42,
  "MissCount": 8,
  "HitRate": 0.84,
  "CurrentCount": 37,
  "CurrentMemoryBytes": 5242880,
  "EvictionCount": 3
}
```

Statistics are per node: each node maintains its own cache, so `HitCount`, `CurrentCount`, and the
rest reflect only the node that answered the request.

### `PUT /v1.0/containers/{container}/cache`

Replace a container's cache settings and apply them to the live cache immediately.

```bash
curl -X PUT http://localhost:8000/v1.0/containers/telemetry/cache \
  -H 'Content-Type: application/json' \
  -d '{"Enabled":true,"Policy":"LRU","MaxObjects":1000,"MaxMemoryBytes":268435456,"EvictCount":10,"MaxCacheableObjectBytes":1048576}'
```

| Field | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `false` | Whether to cache this container's objects. New containers default to `true` |
| `Policy` | string | `LRU` | `FIFO` or `LRU`; an unrecognized value falls back to `LRU` |
| `MaxObjects` | int | `1000` | Maximum resident objects; clamped to at least 1 |
| `MaxMemoryBytes` | long | `0` | Memory cap in bytes; `0` means no cap |
| `EvictCount` | int | `10` | Objects evicted on contention; clamped to at most `MaxObjects` |
| `MaxCacheableObjectBytes` | long | `1048576` | Per-object admission ceiling; objects larger than this are never cached. `0` means no ceiling |

Returns `200` with the same shape as the `GET` above. A clearly-invalid request — for example
`EvictCount` greater than `MaxObjects`, or a positive `MaxMemoryBytes` smaller than
`MaxCacheableObjectBytes` — is rejected with `400`; `404` if the container does not exist.

**Semantics.** Reads are cache-first: a hit is validated against the object's current active extent
and served from memory with no read lease, so it is strictly cheaper than a miss; a miss (or a stale
entry) falls through to storage and, on a full read of a within-ceiling object, hydrates the cache.
Writes are **write-through** — the object lands in both storage and cache before the write is
acknowledged. Deletes evict from the cache first, then tombstone storage. Coherence across nodes is
maintained by the active-extent check, so a replace or delete on one node is never served stale from
another node's cache. Range reads slice a cached full payload but a range miss streams straight
through without hydrating.

> **Rebuild caveat.** Cache settings live in the metadata database (and are mirrored into the
> container manifest so a normal rehydrate preserves them). A full `rehydrate --mode Rebuild`, which
> reconstructs the database from extent storage, resets a container's cache settings to the enabled
> default.

### `PUT /v1.0/containers/{container}/resp-index`

Assign or clear the container's **RESP (Redis) database index**. A Redis client that issues `SELECT n`
with the assigned index addresses this container instead of the default `resp{n}` — the one way a
numeric Redis `SELECT` can reach an arbitrarily named container.

```bash
# Claim index 5 for this container
curl -X PUT http://localhost:8000/v1.0/containers/foo/resp-index \
  -H 'Content-Type: application/json' -d '{"Index":5}'

# Clear the mapping
curl -X PUT http://localhost:8000/v1.0/containers/foo/resp-index \
  -H 'Content-Type: application/json' -d '{"Index":null}'
```

| Field | Type | Notes |
|---|---|---|
| `Index` | int or null | The RESP database index to claim, or `null` to clear. Must be non-negative |

Returns `200` with the updated container. The index is **unique across containers** (enforced by the
database): claiming one another container already holds returns `409 Conflict`. A negative index returns
`400`; a missing container returns `404`. To be selectable by a client the index must also be within
`Resp.DatabaseCount` (16 by default). The assignment is mirrored into the container manifest, so a full
`rehydrate --mode Rebuild` preserves it. See [`RESP_API.md`](RESP_API.md#addressing-an-arbitrarily-named-container).

### `PUT /v1.0/containers/{container}/multipart-expiry`

Assign or clear the container's **multipart-upload expiry** — the number of days an initiated multipart
upload may sit incomplete before the janitor reclaims it. At initiate time the server stamps each
upload's expiry using the container's value when set, otherwise the system-wide
`S3.MultipartUploadExpiryDays`.

```bash
# Expire this container's abandoned uploads after 3 days
curl -X PUT http://localhost:8000/v1.0/containers/foo/multipart-expiry \
  -H 'Content-Type: application/json' -d '{"Days":3}'

# Clear the override (inherit the system default)
curl -X PUT http://localhost:8000/v1.0/containers/foo/multipart-expiry \
  -H 'Content-Type: application/json' -d '{"Days":null}'
```

| Field | Type | Notes |
|---|---|---|
| `Days` | int or null | Days before an incomplete upload expires, `1`–`365`, or `null` to clear the override and inherit `S3.MultipartUploadExpiryDays` |

Returns `200` with the updated container; `404` if the container does not exist. Only uploads initiated
after the change take the new window — the janitor purges by each upload's stamped `ExpiresUtc`.

### Multipart uploads

Large objects can be uploaded in parts and assembled server-side into a single object. The full
lifecycle — initiate, upload parts, complete — is available over REST, and mirrored on the
[S3 protocol](S3_API.md#multipart-upload). Parts stage on shared storage and assemble into one immutable
object on completion, so a completed multipart object is indistinguishable from one written in a single
`PUT`, and any node can complete an upload started on another. Every part except the last must be at
least `S3.MultipartMinPartBytes` (default 5 MiB); an upload may have up to `S3.MultipartMaxParts` parts
(default 10000). An upload that is never completed or aborted is reclaimed after the container's
`MultipartUploadExpiryDays` when set, otherwise the system-wide `S3.MultipartUploadExpiryDays`
(default 7); a container can override the window via
[`PUT /v1.0/containers/{container}/multipart-expiry`](#put-v10containerscontainermultipart-expiry).

#### `POST /v1.0/containers/{container}/multipart-uploads?key={key}`

Initiate an upload; returns an `UploadId` for the subsequent calls.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `key` | string | (required) | Target object key |
| `contentType` | string | — | Content type for the completed object |

Tags for the completed object may be supplied via the `x-pepperx-tags` header (URL-encoded `k=v&k=v`).

```bash
curl -X POST 'http://localhost:8000/v1.0/containers/telemetry/multipart-uploads?key=big/object.bin&contentType=application/octet-stream'
```
```json
{ "UploadId": "mpu_...", "Key": "big/object.bin" }
```
`201` on success; `404` if the container does not exist.

#### `PUT /v1.0/containers/{container}/multipart-uploads/{uploadId}/parts/{partNumber}`

Upload one part. The raw request body is the part payload. The response `ETag` is the part's MD5 — keep
it for the complete call.

```bash
curl -X PUT --data-binary @part1.bin \
  'http://localhost:8000/v1.0/containers/telemetry/multipart-uploads/mpu_.../parts/1'
```
```json
{ "PartNumber": 1, "ETag": "9e107d9d...", "SizeBytes": 5242880 }
```

To **copy** a part from an existing object instead of sending a body, omit the body and set
`x-pepperx-copy-source: {container}/{key}` (optionally `x-pepperx-copy-source-range: bytes=start-end`):

```bash
curl -X PUT -H 'x-pepperx-copy-source: telemetry/source.bin' \
  'http://localhost:8000/v1.0/containers/telemetry/multipart-uploads/mpu_.../parts/2'
```
`200` on success; `404 NoSuchUpload` if the upload does not exist; `404 NoSuchKey` if a copy source is missing.

#### `POST /v1.0/containers/{container}/multipart-uploads/{uploadId}/complete`

Assemble the listed parts, in ascending order, into the object. Each part's `ETag` must match the staged
part, and every part except the last must meet the minimum part size.

```bash
curl -X POST -H 'Content-Type: application/json' \
  --data '{"Parts":[{"PartNumber":1,"ETag":"9e107d9d..."},{"PartNumber":2,"ETag":"a1b2c3..."}]}' \
  'http://localhost:8000/v1.0/containers/telemetry/multipart-uploads/mpu_.../complete'
```
```json
{ "ContainerName": "telemetry", "Key": "big/object.bin", "ETag": "e3b0c44...-2", "ExtentId": "ext_...", "SizeBytes": 5242886 }
```
`200` on success. Errors: `400` for a missing/mismatched part (`InvalidPart`), out-of-order parts
(`InvalidPartOrder`), or a too-small non-final part (`EntityTooSmall`); `404 NoSuchUpload` if the upload
does not exist — including a second, racing complete, since exactly one wins.

#### `GET /v1.0/containers/{container}/multipart-uploads/{uploadId}/parts`

List an upload's staged parts, paginated ascending by part number.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `maxParts` | int | 1000 | Page size, 1–1000 |
| `partNumberMarker` | int | — | Resume after this part number |

```json
{
  "Parts": [
    { "PartNumber": 1, "ETag": "9e107d9d...", "Md5": "9e107d9d...", "Sha256": "b94d27b9...",
      "SizeBytes": 5242880, "CreatedUtc": "2026-07-26T12:00:01Z" }
  ],
  "IsTruncated": false,
  "NextPartNumberMarker": null
}
```

Each part carries both its `Md5` (also surfaced as `ETag`) and its `Sha256`.

#### `GET /v1.0/containers/{container}/multipart-uploads/{uploadId}/parts/{partNumber}`

Read a single staged part's metadata (size, MD5/ETag, SHA-256, staged timestamp).

```json
{ "PartNumber": 1, "ETag": "9e107d9d...", "Md5": "9e107d9d...", "Sha256": "b94d27b9...", "SizeBytes": 5242880, "CreatedUtc": "2026-07-26T12:00:01Z" }
```
`200` on success; `404` if the upload or the part does not exist.

#### `DELETE /v1.0/containers/{container}/multipart-uploads/{uploadId}/parts/{partNumber}`

Delete a single staged part, discarding its blob. `204` on success; `404` if the part does not exist.
Completing the upload while its part list still references the deleted part fails with `InvalidPart`
until the part is re-uploaded.

#### `GET /v1.0/containers/{container}/multipart-uploads`

List the container's in-progress uploads, paginated.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `maxUploads` | int | 1000 | Page size, 1–1000 |
| `keyMarker` | string | — | Resume after this object key |
| `uploadIdMarker` | string | — | Resume after this upload id (paired with `keyMarker`) |

```json
{
  "Uploads": [
    { "UploadId": "mpu_...", "Key": "big/object.bin", "ContentType": "application/octet-stream",
      "InitiatedUtc": "2026-07-26T12:00:00Z", "ExpiresUtc": "2026-08-02T12:00:00Z" }
  ],
  "IsTruncated": false,
  "NextKeyMarker": null,
  "NextUploadIdMarker": null
}
```

`200` with the page; `404` if the container does not exist.

#### `DELETE /v1.0/containers/{container}/multipart-uploads/{uploadId}`

Abort an in-progress multipart upload, discarding its staged parts. `204` on success; idempotent
(aborting an unknown upload also returns `204`). Force-deleting a container also purges any of its
in-progress uploads.

### `DELETE /v1.0/containers/{container}`

Delete a container. Refuses with `409 NotEmpty` if it still holds objects.

```bash
# Delete the container and everything in it. Not recoverable.
curl -X DELETE 'http://localhost:8000/v1.0/containers/telemetry?force=true'
```

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `force` | bool | `false` | Delete the container's objects along with it |

`204` on success.

---

## Objects

An object is one immutable extent: a binary payload plus its metadata. Writing to a key that already
exists does not modify the existing extent — it writes a new one and atomically repoints the key,
last writer winning. Readers holding the old extent finish reading it safely.

### `PUT /v1.0/containers/{container}/object?key={key}`

Write an object with the payload as the raw request body. This is the form to use for anything large:
the payload streams rather than being buffered into a JSON envelope.

```bash
curl -X PUT 'http://localhost:8000/v1.0/containers/telemetry/object?key=metrics/cpu.json' \
  -H 'Content-Type: application/json' \
  -H 'x-pepperx-labels: metric,cpu' \
  -H 'x-pepperx-tags: resolution=1m&host=web-01' \
  -H "x-pepperx-object: $(echo -n '{"pipeline":"ingest"}' | base64)" \
  --data-binary @cpu.json
```

Metadata travels in headers:

| Header | Format | Notes |
|---|---|---|
| `x-pepperx-labels` | Comma-separated | A flat list of strings |
| `x-pepperx-tags` | URL-encoded `k=v&k=v` | Both key and value must be percent-encoded |
| `x-pepperx-object` | Base64 of a JSON document | Freeform; arbitrary nesting |

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `key` | string | — | Required, URL-encoded |
| `nooverwrite` | bool | `false` | Return `409` instead of replacing an existing key |

Headers were chosen over a multipart body so the payload stays a plain byte stream — a client can
`PUT` a file with no framing and no size ceiling beyond the configured limit. The tradeoff is that
`x-pepperx-object` is bounded by the server's header limit
(`Rest.MetadataObjectHeaderLimitBytes`, 16 KiB by default); larger metadata objects need the JSON
envelope form below.

`201` on create, `200` on replace:

```json
{
  "Key": "metrics/cpu.json",
  "ExtentId": "ext_mry4fbo6_9KpQm2wRxYz",
  "SizeBytes": 4180,
  "Sha256": "a3f1...",
  "Md5": "9e107d9d...",
  "CreatedUtc": "2026-07-23T23:07:02.719656Z"
}
```

### `POST /v1.0/containers/{container}/object?key={key}`

Write an object using a JSON envelope, with the payload base64-encoded. Use this when the metadata
object is too large for a header, or when the client cannot set custom headers.

```json
{
  "ContentType": "application/json",
  "Data": "eyJjcHUiOiAwLjQyfQ==",
  "Labels": ["metric", "cpu"],
  "Tags": { "resolution": "1m" },
  "Object": { "pipeline": "ingest", "attempt": 1 }
}
```

Base64 costs about a third in transfer size and forces the whole payload into memory on both ends, so
prefer `PUT` for anything of consequence.

### `GET /v1.0/containers/{container}/object?key={key}`

Read an object's payload. The response body is the raw bytes; metadata comes back in headers.

```bash
curl -O -J 'http://localhost:8000/v1.0/containers/telemetry/object?key=metrics/cpu.json'
```

Response headers:

| Header | Notes |
|---|---|
| `Content-Type` | As written |
| `Content-Length` | Payload size in bytes |
| `x-pepperx-extent-id` | The extent actually served |
| `x-pepperx-sha256` | SHA-256 of the payload |
| `x-pepperx-md5` | MD5 of the payload (the S3 ETag is derived from this); absent on legacy objects |
| `x-pepperx-labels`, `x-pepperx-tags`, `x-pepperx-object` | As written |

Supports `Range` for partial reads, answering `206 Partial Content`:

```bash
curl -H 'Range: bytes=0-1023' \
  'http://localhost:8000/v1.0/containers/telemetry/object?key=large.bin'
```

A read takes a lease for its duration. A concurrent delete waits for in-flight reads to finish before
destroying the payload, so a read that has started always completes — see
[delete](#delete-v10containerscontainerobjectkeykey).

### `HEAD /v1.0/containers/{container}/object?key={key}`

Existence and metadata headers without the payload. The cheapest way to check whether a key exists
and how large it is.

### `DELETE /v1.0/containers/{container}/object?key={key}`

Delete an object. `204` on success.

Deletion is not instantaneous by design. The extent is tombstoned first, which makes it invisible to
new reads; the node then waits for any in-flight read leases across the cluster to drain before the
payload is destroyed. This is why a delete can take a moment under load, and why a read issued before
the delete still returns data rather than a truncated stream.

A read that begins *after* the tombstone gets `410 Deleting`, not `404` — the distinction tells a
client the key existed and is going away, rather than never having existed.

---

## Metadata

### `GET /v1.0/containers/{container}/object/metadata?key={key}`

Full metadata for one object without transferring the payload.

```json
{
  "Key": "metrics/cpu.json",
  "ExtentId": "ext_mry4fbo6_9KpQm2wRxYz",
  "ContainerId": "ctr_mry4fbo6_4XrvQxXgOey",
  "ContainerName": "telemetry",
  "SizeBytes": 4180,
  "Sha256": "a3f1...",
  "Md5": "9e107d9d...",
  "ContentType": "application/json",
  "Labels": ["metric", "cpu"],
  "Tags": { "resolution": "1m" },
  "Object": { "pipeline": "ingest", "attempt": 1 },
  "HasMetadataObject": true,
  "CreatedUtc": "2026-07-23T23:07:02.719656Z"
}
```

`Md5` is the content MD5 (the value the S3 ETag is derived from); `Sha256` is PepperX's native content
hash. `Md5` is absent (null) on objects written before MD5 recording was added. For a
multipart-assembled object, the S3 ETag over the [S3 protocol](S3_API.md#multipart-upload) is the
`…-N` form; over REST the object still reports its content `Md5` and `Sha256`.

### `PUT /v1.0/containers/{container}/object/metadata?key={key}`

Update metadata, preserving the payload.

```json
{
  "Labels": ["metric", "cpu", "verified"],
  "Tags": { "resolution": "1m", "reviewed": "true" },
  "Object": { "pipeline": "ingest", "attempt": 2 },
  "ClearObject": false
}
```

| Field | Type | Semantics |
|---|---|---|
| `Labels` | array or null | `null` leaves them unchanged; an array replaces them wholesale |
| `Tags` | object or null | Same — replace, not merge |
| `Object` | any or null | `null` leaves it unchanged |
| `ClearObject` | bool | Set `true` to remove the metadata object; needed because `null` means "unchanged" |

Extents are immutable, so this is implemented as a rewrite: a new extent is written carrying the same
payload and the new metadata, then the key is repointed. The extent ID therefore changes. This is
worth knowing if you have stored an extent ID somewhere — it identifies a version, not a key.

---

## Search and enumeration

Three endpoints share one query shape:

| Endpoint | Scope |
|---|---|
| `POST /v1.0/containers/enumerate` | Containers |
| `POST /v1.0/containers/{container}/objects/enumerate` | Objects in one container |
| `POST /v1.0/objects/enumerate` | Objects across every container |

### Query

```json
{
  "MaxResults": 100,
  "Skip": 0,
  "Ordering": "CreatedDescending",
  "Prefix": "metrics/",
  "Suffix": ".json",
  "Labels": ["metric", "cpu"],
  "Tags": { "resolution": "1m" },
  "Containers": ["telemetry", "backups"],
  "CreatedAfterUtc": "2026-07-01T00:00:00Z",
  "CreatedBeforeUtc": "2026-08-01T00:00:00Z",
  "CaseInsensitive": false,
  "ContinuationToken": null
}
```

| Field | Type | Default | Notes |
|---|---|---|---|
| `MaxResults` | int | 100 | Clamped to a sane maximum |
| `Skip` | int | 0 | Offset paging; prefer `ContinuationToken` for deep pages |
| `Ordering` | enum | `CreatedDescending` | `CreatedAscending`, `CreatedDescending`, `KeyAscending`, `KeyDescending` |
| `Prefix` / `Suffix` | string | null | Match on the key |
| `Labels` | array | null | **All** listed labels must be present |
| `Tags` | object | null | **All** listed pairs must match |
| `Containers` | array | null | Cross-container search only; omit for all |
| `CreatedAfterUtc` / `CreatedBeforeUtc` | timestamp | null | Half-open range |
| `CaseInsensitive` | bool | false | Applies to prefix, suffix, labels, and tags |
| `ContinuationToken` | string | null | From a previous response |

Labels and tags are conjunctive — `["metric","cpu"]` finds objects carrying both, not either. There is
no disjunction; run separate queries and union the results if you need one.

### Response

```json
{
  "Success": true,
  "StartUtc": "2026-07-23T23:08:57.615707Z",
  "EndUtc": "2026-07-23T23:08:57.615709Z",
  "MaxResults": 100,
  "TotalRecords": 1420,
  "RecordsRemaining": 1320,
  "ContinuationToken": "eyJvIjoxMDB9",
  "EndOfResults": false,
  "Objects": [ ]
}
```

`TotalRecords` counts everything matching the filter, not just this page. Page by following
`ContinuationToken` until `EndOfResults` is true; that is stable under concurrent writes in a way that
`Skip` is not.

### Via query string

`GET /v1.0/containers` and `GET /v1.0/containers/{container}/objects` accept the simple subset as
query parameters: `maxResults`, `skip`, `ordering`, `prefix`, `suffix`, `caseInsensitive`. Label and
tag filters need the POST form.

---

## Admin

### `GET /v1.0/admin/stats`

Aggregate statistics: container and object counts, bytes stored, per-container rollups, volume
capacity, metadata database size, and cluster nodes.

```json
{
  "ContainerCount": 4,
  "ObjectCount": 8,
  "TotalBytes": 18642,
  "Containers": [ { "Id": "ctr_...", "Name": "telemetry", "ObjectCount": 3, "TotalBytes": 4812 } ],
  "StorageTotalBytes": 1081101176832,
  "StorageFreeBytes": 577659994112,
  "DatabaseSizeBytes": 8912896,
  "Nodes": [ ]
}
```

### `GET /v1.0/admin/nodes`

Cluster membership and liveness.

```json
[
  {
    "Id": "nod_mry563mx_8Dnwa5RlgVv",
    "Hostname": "7156e7a34234",
    "StartedUtc": "2026-07-23T23:27:46.090239Z",
    "LastHeartbeatUtc": "2026-07-23T23:28:31.087030Z",
    "HeartbeatAgeSeconds": 3.73,
    "IsAlive": true
  }
]
```

A node is considered dead after `Cluster.NodeDeadAfterSeconds` without a heartbeat (60 by default).
Dead nodes stay listed until the janitor reaps them, which is deliberate — a node that vanished is
something an operator should see, not something that should silently disappear.

### `GET /v1.0/admin/settings`

A non-secret view of the node's configuration: protocol listeners, storage driver and directory,
database location, node identity, and request-history retention.

The database password and the S3 static keys are deliberately absent. This endpoint is unauthenticated
like everything else, and serving credentials from it would turn a configuration display into a
credential leak.

### `PUT /v1.0/admin/settings`

Persist a partial settings update to the node's settings file. Every field is optional; only the ones
supplied change.

```bash
curl -X PUT http://localhost:8000/v1.0/admin/settings \
  -H 'Content-Type: application/json' \
  -d '{"RequestHistoryRetentionDays": 14, "LogMinimumSeverity": "Warn"}'
```

| Field | Type | Notes |
|---|---|---|
| `LogMinimumSeverity` | string | Debug, Info, Warn, Error, Alert, Critical, Emergency |
| `VerifyChecksumOnRead` | bool | Verify each extent's checksum on read |
| `DeleteCoordinationMode` | string | `Cluster` or `Local` |
| `RequestHistoryEnabled` | bool | |
| `RequestHistoryRetentionDays` | int | |
| `RequestHistoryMaxRequestBodyBytes` | int | |
| `RequestHistoryMaxResponseBodyBytes` | int | |

**Changes take effect after a restart.** The server captures its configuration into its services at
startup and does not read it live, so this writes the file the next boot will read rather than
altering the running process. The editable surface is deliberately narrow — ports, hostnames, and
database details are not here, because a wrong value would leave the node unable to start after the
restart that applies it.

The response is the updated `GET /v1.0/admin/settings` view.

### `POST /v1.0/admin/restart`

Exit the process so a container restart policy (`restart: unless-stopped` or `always`) brings it back
up on the current settings file. Returns `202`; the connection drops as the node exits.

```bash
curl -X POST http://localhost:8000/v1.0/admin/restart
```

This has no effect when the node is not run under a restart policy — a bare `dotnet run` simply stops.
Paired with `PUT /v1.0/admin/settings`, it is how the dashboard applies a settings change: save, then
restart.

### `POST /v1.0/admin/rehydrate`

Reconcile the metadata database against raw extent storage.

```bash
curl -X POST http://localhost:8000/v1.0/admin/rehydrate \
  -H 'Content-Type: application/json' -d '{"Mode":"Verify"}'
```

| Mode | Effect |
|---|---|
| `Verify` | Reports drift. Changes nothing. |
| `Repair` | Adds rows for extents found in storage, removes rows for extents that are gone. |
| `Rebuild` | Discards the metadata and reconstructs it entirely from storage. |

This works because extents are self-describing: every `.pxe` file carries its own key, labels, tags,
metadata object, and checksum in a header ahead of the payload. The database is an index for fast
search, not the system of record — lose it entirely and `Rebuild` reconstructs it from the files.

```json
{
  "Mode": "Verify",
  "Success": true,
  "ContainersDiscovered": 4,
  "ExtentsDiscovered": 8,
  "RowsAdded": 0,
  "RowsRemoved": 0,
  "Drift": [],
  "DurationMs": 41.2
}
```

Run `Verify` first. `Rebuild` on a large deployment reads every extent header on disk.

---

## Request history

Every REST request the node serves is captured, subject to `RequestHistory` settings. This is what
backs the dashboard's Request History view.

### `GET /v1.0/api/request-history`

Paged list. Bodies are omitted from list results — they can be large, and a page of fifty of them
would be unusable.

| Parameter | Type | Notes |
|---|---|---|
| `method` | string | Exact match, e.g. `PUT` |
| `statusCode` | int | Exact match |
| `pathContains` | string | Substring match on the path |
| `fromUtc` / `toUtc` | timestamp | Half-open range |
| `pageNumber` | int | 1-based |
| `pageSize` | int | Default 25 |

### `GET /v1.0/api/request-history/{id}`

One record in full, including request and response headers and bodies.

Bodies are truncated at `RequestHistory.MaxRequestBodyBytes` / `MaxResponseBodyBytes` (64 KiB each by
default), with `RequestBodyTruncated` / `ResponseBodyTruncated` flagging it. Paths matching
`RequestHistory.ExcludeBodyPaths` have their bodies dropped entirely — object payload routes are
excluded by default, since capturing them would double every write.

### `GET /v1.0/api/request-history/summary`

Time-bucketed counts, for charting.

| Parameter | Required | Notes |
|---|---|---|
| `fromUtc`, `toUtc` | yes | Window bounds |
| `bucketMinutes` | yes | Bucket width |
| `method`, `statusCode`, `pathContains` | no | Same filters as the list |

```json
{
  "TotalCount": 1942,
  "TotalSuccess": 1851,
  "TotalFailure": 91,
  "AverageDurationMs": 3.1,
  "Buckets": [
    {
      "BucketStartUtc": "2026-07-23T22:00:00Z",
      "BucketEndUtc": "2026-07-23T22:15:00Z",
      "SuccessCount": 240,
      "FailureCount": 12,
      "AverageDurationMs": 2.9
    }
  ]
}
```

Empty buckets are returned as zeros rather than omitted, so a client can plot the series without
filling gaps itself.

### `DELETE /v1.0/api/request-history/{id}`

Delete one record.

### `DELETE /v1.0/api/request-history`

Bulk delete everything matching a filter, using the same parameters as the list. Returns the number
deleted. With no filter, this deletes all retained history.

---

## Errors

Failures return a typed JSON body:

```json
{
  "Error": "NotFound",
  "Message": "The specified container does not exist.",
  "StatusCode": 404
}
```

| `Error` | HTTP | Meaning |
|---|---|---|
| `BadRequest` | 400 | Malformed request or failed validation |
| `NotFound` | 404 | No such container, object, or record |
| `Conflict` | 409 | Already exists, or a concurrent modification conflict |
| `NotEmpty` | 409 | Container still holds objects and `force` was not set |
| `Deleting` | 410 | The extent is being deleted and is no longer readable |
| `TooLarge` | 413 | Payload or a metadata field exceeded a configured limit |
| `InternalError` | 500 | Unexpected server-side failure |
| `NotImplemented` | 501 | Not available on this protocol surface |

Clients should branch on `Error` rather than the status code where the two are not one-to-one —
`Conflict` and `NotEmpty` both return 409 but call for different handling, and `Deleting` (410) is
retryable in a way `NotFound` (404) is not.

---

## See also

- [`S3_API.md`](S3_API.md) — S3-compatible surface
- [`RESP_API.md`](RESP_API.md) — Redis wire protocol
- [`WEBSOCKETS_API.md`](WEBSOCKETS_API.md) — WebSocket request/response protocol
- [`MCP_API.md`](MCP_API.md) — Model Context Protocol tools
- [`sdk/README.md`](sdk/README.md) — C#, Python, and JavaScript clients
