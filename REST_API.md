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

`201` with the container on success. `409 Conflict` if the name is taken.

```json
{
  "Id": "ctr_mry4fbo6_4XrvQxXgOey",
  "Name": "telemetry",
  "Tags": { "team": "platform", "env": "prod" },
  "ObjectCount": 0,
  "TotalBytes": 0,
  "CreatedUtc": "2026-07-23T23:07:00.682678Z",
  "LastUpdateUtc": "2026-07-23T23:07:00.682678Z"
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
| `x-pepperx-sha256` | Checksum of the payload |
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
  "ContentType": "application/json",
  "Labels": ["metric", "cpu"],
  "Tags": { "resolution": "1m" },
  "Object": { "pipeline": "ingest", "attempt": 1 },
  "HasMetadataObject": true,
  "CreatedUtc": "2026-07-23T23:07:02.719656Z"
}
```

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
