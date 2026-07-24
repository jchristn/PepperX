# PepperX RESP API

PepperX speaks the Redis Serialization Protocol, so any Redis client — `redis-cli`,
StackExchange.Redis, `redis-py`, `ioredis`, `go-redis` — can read and write PepperX objects with no
PepperX-specific code.

This is a **string keyspace over durable storage**, not a Redis replacement. Values are persisted as
immutable extents on disk and indexed in PostgreSQL, the same objects the other four protocols see.
That makes them durable and searchable, and it makes this much slower than an in-memory cache. Do not
put PepperX on a hot cache path expecting Redis latency.

Default port: **6379**.

---

## Contents

- [Mapping onto PepperX concepts](#mapping-onto-pepperx-concepts)
- [Connecting](#connecting)
- [Supported commands](#supported-commands)
- [What is not supported](#what-is-not-supported)
- [Notes on semantics](#notes-on-semantics)

---

## Mapping onto PepperX concepts

| Redis | PepperX |
|---|---|
| Database index `n` (`SELECT n`) | Container named `resp{n}` |
| Key | Object key |
| String value | Extent payload |

`SELECT 0` targets the container `resp0`, `SELECT 3` targets `resp3`, and so on up to
`Resp.DatabaseCount` (16 by default). The prefix is configurable via `Resp.ContainerPrefix`.

Containers are created on first write. A `GET` against a database whose container does not exist
returns a nil reply rather than an error, which is what a Redis client expects from a missing key.

Objects written over RESP carry no labels and no metadata object, but they are ordinary PepperX
objects: visible over REST, listed in the dashboard, and searchable by key prefix.

---

## Connecting

### redis-cli

```bash
redis-cli -p 6379

127.0.0.1:6379> SELECT 0
OK
127.0.0.1:6379> SET greeting "hello from RESP"
OK
127.0.0.1:6379> GET greeting
"hello from RESP"
127.0.0.1:6379> STRLEN greeting
(integer) 15
127.0.0.1:6379> KEYS *
1) "greeting"
```

### redis-py

```python
import redis

client = redis.Redis(host="localhost", port=6379, db=0, decode_responses=True)
client.set("metrics:cpu", "0.42")
print(client.get("metrics:cpu"))
print(client.dbsize())
```

### StackExchange.Redis

```csharp
using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
IDatabase db = redis.GetDatabase(0);

await db.StringSetAsync("metrics:cpu", "0.42");
string? value = await db.StringGetAsync("metrics:cpu");
```

Some clients probe with `CLUSTER` or `CONFIG` commands at connect time. PepperX answers the handshake
commands (`HELLO`, `PING`, `CLIENT`, `COMMAND`, `CONFIG GET`, `INFO`) with enough to satisfy them.

---

## Supported commands

### Connection

| Command | Notes |
|---|---|
| `PING [message]` | |
| `ECHO message` | |
| `HELLO [protover]` | RESP2 and RESP3 |
| `SELECT index` | Switches container |
| `CLIENT ID` / `CLIENT GETNAME` / `CLIENT SETNAME` | |
| `COMMAND` / `CONFIG GET` / `INFO` | Minimal replies, for client handshakes |
| `QUIT` | |

### Strings

| Command | Notes |
|---|---|
| `GET key` | |
| `SET key value [NX \| XX]` | Other options rejected — see below |
| `SETNX key value` | |
| `GETSET key value` | |
| `GETDEL key` | |
| `MGET key [key ...]` | |
| `MSET key value [key value ...]` | |
| `STRLEN key` | |
| `INCR` / `DECR` / `INCRBY` / `DECRBY` / `INCRBYFLOAT` | Compare-and-swap; see below |

### Keyspace

| Command | Notes |
|---|---|
| `EXISTS key [key ...]` | |
| `DEL key [key ...]` / `UNLINK key [key ...]` | `UNLINK` is a synonym; deletes are not lazy |
| `TYPE key` | Always `string` or `none` |
| `KEYS pattern` | Glob matching |
| `SCAN cursor [MATCH pattern] [COUNT n]` | Cursor-based iteration |
| `DBSIZE` | Objects in the current container |
| `FLUSHDB` | **Deletes every object in the current container** |

---

## What is not supported

### Expiry

`EXPIRE`, `PEXPIRE`, `EXPIREAT`, and `PEXPIREAT` return an error. `TTL` and `PTTL` answer `-1`
(no expiry) for keys that exist.

PepperX has no TTL concept: extents live until deleted. Silently accepting `EXPIRE` and never
expiring the key would be worse than refusing — an application relying on expiry to bound growth
would find out only when the volume filled. The `SET` options `EX`, `PX`, `EXAT`, `PXAT`, and
`KEEPTTL` are rejected for the same reason.

### Data types

Only strings. Lists, hashes, sets, sorted sets, streams, bitmaps, HyperLogLog, and geospatial types
are not implemented — a PepperX value is an opaque byte payload, and modeling server-side data
structures over immutable extents would mean rewriting the whole extent on every element mutation.

### Server features

Pub/sub, transactions (`MULTI`/`EXEC`), Lua scripting, `WATCH`, blocking commands, replication,
cluster mode, keyspace notifications, and persistence commands (`SAVE`, `BGSAVE`) are not
implemented.

---

## Notes on semantics

### Values are size-capped

`Resp.MaxValueBytes` (64 MiB by default) bounds a single value. Larger payloads belong on the REST or
S3 surface, which stream rather than buffering the value in the protocol layer.

### The INCR family is compare-and-swap, not atomic

Redis increments in memory under a single-threaded loop. PepperX has to read the extent, compute, and
write a new one, and other nodes may be doing the same thing to the same key. The implementation
reads, computes, and writes conditionally on the value not having changed, retrying up to
`Resp.CasRetryCount` times (8 by default) before returning an error.

The result is correct under contention but not free, and a caller hammering one counter from many
connections will see occasional failures rather than unbounded waiting. If you need a high-rate
counter, this is the wrong store for it.

### FLUSHDB is a real delete

There is no in-memory dataset to drop. `FLUSHDB` deletes every object in the container, going through
the same tombstone-and-drain path as any other delete. On a large container it takes time proportional
to the object count.

### Keys are not namespaced beyond the container

A RESP key maps directly to an object key inside `resp{n}`. Colon-delimited conventions like
`user:1000:profile` work exactly as they do in Redis — they are just keys — and are visible over REST
as objects with that literal key.

---

## See also

- [`REST_API.md`](REST_API.md) — labels, tags, metadata objects, and search
- [`S3_API.md`](S3_API.md) · [`WEBSOCKETS_API.md`](WEBSOCKETS_API.md) · [`MCP_API.md`](MCP_API.md)
