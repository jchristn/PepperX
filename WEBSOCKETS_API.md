# PepperX WebSockets API

REST-equivalent operations over a single persistent connection. This exists for clients that issue
many small operations and would otherwise pay connection setup on each one, and for clients that want
several requests in flight at once without a connection pool.

Default port: **8002**. Endpoint: `ws://localhost:8002/`.

---

## Contents

- [Envelopes](#envelopes)
- [Correlation and concurrency](#correlation-and-concurrency)
- [Operations](#operations)
- [Binary payloads](#binary-payloads)
- [Examples](#examples)
- [Errors](#errors)

---

## Envelopes

Every message is a JSON object. Clients send request envelopes; the server replies with response
envelopes.

### Request

```json
{
  "RequestId": "req-1",
  "Operation": "ObjectWrite",
  "Container": "telemetry",
  "Key": "metrics/cpu.json",
  "Body": {
    "ContentType": "application/json",
    "DataBase64": "eyJjcHUiOiAwLjQyfQ==",
    "Labels": ["metric", "cpu"],
    "Tags": { "resolution": "1m" },
    "Object": { "pipeline": "ingest" }
  }
}
```

| Field | Type | Notes |
|---|---|---|
| `RequestId` | string | Echoed back on the response. Required if you want to correlate. |
| `Operation` | enum | See [operations](#operations) |
| `Container` | string | Container name, when the operation needs one |
| `Key` | string | Object key, when the operation needs one |
| `Body` | object | Operation-specific payload |

### Response

```json
{
  "RequestId": "req-1",
  "Success": true,
  "StatusCode": 200,
  "Result": { "ExtentId": "ext_...", "Key": "metrics/cpu.json", "SizeBytes": 12 },
  "Error": null,
  "DataBase64": null
}
```

| Field | Type | Notes |
|---|---|---|
| `RequestId` | string | The value from the request |
| `Success` | bool | Whether the operation succeeded |
| `StatusCode` | int | The HTTP status the equivalent REST call would return |
| `Result` | object | Operation-specific result, absent on failure |
| `Error` | object | `{ Error, Message, StatusCode }` on failure, `null` otherwise |
| `DataBase64` | string | Object payload for `ObjectRead`, `null` otherwise |

The status code is carried explicitly so a client can reuse whatever REST error handling it already
has rather than learning a second error vocabulary.

---

## Correlation and concurrency

Responses are **not** guaranteed to arrive in request order. A large read issued first may complete
after a small one issued second, and serializing them would defeat the point of the connection.

Match responses to requests by `RequestId`. Generate one per request; the server treats it as an
opaque string and only echoes it. A request sent without a `RequestId` still gets a response, but with
several in flight you will not be able to tell which one it belongs to.

The reference SDK clients ([C#](sdk/csharp), [Python](sdk/python), [JavaScript](sdk/js)) implement
this with a pending-request map keyed by `RequestId`, which is the pattern to copy.

---

## Operations

| Operation | Container | Key | Body | Equivalent REST |
|---|:---:|:---:|---|---|
| `Health` | | | | `GET /` |
| `ContainerCreate` | | | `{ Name, Tags }` | `PUT /v1.0/containers` |
| `ContainerRead` | yes | | | `GET /v1.0/containers/{c}` |
| `ContainerExists` | yes | | | `HEAD /v1.0/containers/{c}` |
| `ContainerList` | | | enumeration query | `GET /v1.0/containers` |
| `ContainerEnumerate` | | | enumeration query | `POST /v1.0/containers/enumerate` |
| `ContainerUpdateTags` | yes | | tag map | `PUT /v1.0/containers/{c}/tags` |
| `ContainerDelete` | yes | | `{ Force }` | `DELETE /v1.0/containers/{c}` |
| `ObjectWrite` | yes | yes | write body (below) | `POST /v1.0/containers/{c}/object` |
| `ObjectRead` | yes | yes | | `GET .../object` |
| `ObjectExists` | yes | yes | | `HEAD .../object` |
| `ObjectReadMetadata` | yes | yes | | `GET .../object/metadata` |
| `ObjectUpdateMetadata` | yes | yes | `{ Labels, Tags, Object, ClearObject }` | `PUT .../object/metadata` |
| `ObjectDelete` | yes | yes | | `DELETE .../object` |
| `ObjectList` | yes | | enumeration query | `GET .../objects` |
| `ObjectEnumerate` | yes | | enumeration query | `POST .../objects/enumerate` |
| `SearchEnumerate` | | | enumeration query | `POST /v1.0/objects/enumerate` |
| `AdminStats` | | | | `GET /v1.0/admin/stats` |
| `AdminNodes` | | | | `GET /v1.0/admin/nodes` |

The enumeration query body is identical to REST — see
[search and enumeration](REST_API.md#search-and-enumeration).

### `ObjectWrite` body

| Field | Type | Notes |
|---|---|---|
| `DataBase64` | string | Payload, base64-encoded |
| `ContentType` | string | MIME type |
| `Labels` | array | Flat list of strings |
| `Tags` | object | String-to-string map |
| `Object` | any | Freeform JSON metadata |
| `NoOverwrite` | bool | Fail rather than replace an existing key |

---

## Binary payloads

Payloads are base64-encoded in both directions, because the envelope is JSON and JSON has no binary
type. That costs about a third in transfer size and requires the whole payload in memory on both
ends.

For large objects use REST, which streams the body with no encoding overhead. `Websocket.MaxMessageBytes`
(128 MiB by default) caps a single message and is the practical ceiling here.

---

## Examples

### Browser / Node

```javascript
const socket = new WebSocket('ws://localhost:8002/');
const pending = new Map();
let nextId = 0;

socket.addEventListener('message', (event) => {
  const response = JSON.parse(event.data);
  const resolver = pending.get(response.RequestId);
  if (!resolver) return;
  pending.delete(response.RequestId);
  if (response.Success) resolver.resolve(response);
  else resolver.reject(new Error(response.Error?.Message ?? 'Request failed'));
});

function send(request) {
  const RequestId = `req-${++nextId}`;
  return new Promise((resolve, reject) => {
    pending.set(RequestId, { resolve, reject });
    socket.send(JSON.stringify({ RequestId, ...request }));
  });
}

await send({ Operation: 'ContainerCreate', Body: { Name: 'telemetry' } });

await send({
  Operation: 'ObjectWrite',
  Container: 'telemetry',
  Key: 'metrics/cpu.json',
  Body: {
    ContentType: 'application/json',
    DataBase64: btoa('{"cpu":0.42}'),
    Labels: ['metric', 'cpu'],
  },
});

const read = await send({ Operation: 'ObjectRead', Container: 'telemetry', Key: 'metrics/cpu.json' });
console.log(atob(read.DataBase64));
```

### Python

```python
import asyncio
from pepperx import AsyncPepperXWebsocketClient

async def main():
    async with AsyncPepperXWebsocketClient("ws://localhost:8002/") as client:
        await client.create_container("telemetry")
        await client.write_object("telemetry", "metrics/cpu.json", b'{"cpu":0.42}',
                                  labels=["metric", "cpu"])
        print(await client.read_object("telemetry", "metrics/cpu.json"))

asyncio.run(main())
```

The SDKs handle envelopes and correlation for you — see [`sdk/README.md`](sdk/README.md).

---

## Errors

A failed operation returns a response envelope, not a closed connection:

```json
{
  "RequestId": "req-7",
  "Success": false,
  "StatusCode": 404,
  "Result": null,
  "Error": {
    "Error": "NotFound",
    "Message": "The specified object does not exist.",
    "StatusCode": 404
  }
}
```

`Error.Error` uses the same vocabulary as REST — see [errors](REST_API.md#errors).

Malformed JSON, or an envelope the server cannot parse at all, also produces an error response rather
than a disconnect. The connection stays usable; only that request failed.

---

## See also

- [`REST_API.md`](REST_API.md) — the complete surface
- [`S3_API.md`](S3_API.md) · [`RESP_API.md`](RESP_API.md) · [`MCP_API.md`](MCP_API.md)
- [`sdk/README.md`](sdk/README.md) — clients that implement this protocol
