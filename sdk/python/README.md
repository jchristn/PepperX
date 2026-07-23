# PepperX Python SDK

Typed Python client for [PepperX](../../README.md). Three clients ship in the package:

- **`PepperXClient`** — synchronous REST client.
- **`AsyncPepperXClient`** — the same surface, `async`/`await`.
- **`AsyncPepperXWebsocketClient`** — operations over one persistent connection, many in flight at once.

Every method returns a dataclass rather than a raw dict. Server errors raise `PepperXError`, which
carries the server's `ApiError` classification and HTTP status code, so you branch on values rather
than parsing message text.

PepperX is unauthenticated by design — it is backend infrastructure, and access control belongs to
the application in front of it. There are no credentials to configure.

## Install

```bash
pip install pepperx
```

Requires Python 3.9 or newer.

## Quickstart

```python
from pepperx import PepperXClient

with PepperXClient("http://localhost:8000") as client:
    client.create_container("photos")

    written = client.write_object(
        "photos",
        "2026/07/cat.jpg",                    # keys may contain slashes
        open("cat.jpg", "rb").read(),
        content_type="image/jpeg",
        labels=["animal", "cute"],
        tags={"team": "mammals"},
        metadata_object={"camera": "X100V", "iso": 400},   # freeform JSON metadata
    )

    read = client.read_object("photos", "2026/07/cat.jpg")
    print(len(read.data), "bytes, sha256", read.sha256)
```

### Searching by labels and tags

Both filters use AND semantics: every label listed must be present, and every tag key must be
present with the given value.

```python
from pepperx import EnumerationQuery

found = client.enumerate_objects(
    "photos",
    EnumerationQuery(labels=["animal"], tags={"team": "mammals"}, max_results=50),
)

print(found.total_records, "matches,", found.records_remaining, "remaining")
for item in found.objects:
    print(item.key, item.size_bytes, item.labels)
```

`client.search(...)` runs the same query across every container; set
`EnumerationQuery.containers` to narrow it.

### Streaming uploads

Pass an iterator of byte chunks to stream a payload you do not want to hold in memory:

```python
def chunks(path, size=1024 * 1024):
    with open(path, "rb") as handle:
        while block := handle.read(size):
            yield block

client.write_object_stream("backups", "archive.tar", chunks("archive.tar"))
```

An iterator sends `Transfer-Encoding: chunked`, which the server materializes before storing (still
subject to the configured object-size limit). Sending `bytes` instead declares a length and streams
straight through to storage.

### Handling errors

```python
from pepperx import ApiError, PepperXError

try:
    client.create_container("photos")
except PepperXError as exc:
    if exc.error_type is ApiError.CONFLICT:
        pass  # already exists
    else:
        raise
```

Reads of missing resources return `None` rather than raising: `read_container`, `read_object`, and
`read_object_metadata`.

### Async and WebSockets

```python
import asyncio
from pepperx import AsyncPepperXClient, AsyncPepperXWebsocketClient

async def main():
    async with AsyncPepperXClient("http://localhost:8000") as client:
        await client.create_container("events")
        await client.write_object("events", "e-1", b"payload")

    async with AsyncPepperXWebsocketClient("ws://localhost:8002/") as ws:
        # Concurrent calls on one connection are correlated by request id.
        results = await asyncio.gather(*[ws.health() for _ in range(16)])

asyncio.run(main())
```

## Other protocols

PepperX also speaks S3, Redis RESP, and MCP. Those surfaces are best consumed with their standard
clients — `boto3`, `redis-py`, and any MCP client — rather than through this SDK. See the protocol
references in the [repository root](../../README.md).

## Development

```bash
pip install -e ".[dev]"

export PEPPERX_URL=http://localhost:8000
export PEPPERX_WS_URL=ws://localhost:8002/

pytest                        # skips when no node is reachable
python examples/console.py    # narrated end-to-end walkthrough
ruff check . && mypy pepperx
```
