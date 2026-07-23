# PepperX SDKs

Client libraries for the PepperX native REST and WebSocket APIs. All three cover the same surface
and agree on the wire format — an object written by one reads back identically through the others,
including binary payloads, labels, tags, and the freeform JSON metadata object.

| Language | Package | Clients | Docs |
|---|---|---|---|
| C# | `PepperX.Sdk` | `PepperXRestClient`, `PepperXWebsocketClient` | [csharp/](csharp/README.md) |
| Python | `pepperx` | `PepperXClient`, `AsyncPepperXClient`, `AsyncPepperXWebsocketClient` | [python/](python/README.md) |
| JavaScript | `@pepperx/sdk` | `PepperXClient`, `PepperXWebsocketClient` | [js/](js/README.md) |

Shared conventions across all three:

- Methods return typed objects, never raw response bodies.
- Server errors raise a typed error carrying the server's classification and HTTP status code.
- Reads of missing resources return null rather than raising.
- Label and tag filters use AND semantics.
- No credentials: PepperX is unauthenticated backend infrastructure, and access control belongs to
  the application in front of it.

## Which client to use

Reach for the **REST client** by default. Reach for the **WebSocket client** when you are issuing
many operations and want to avoid per-request connection overhead — requests are correlated by
identifier, so many can be in flight on one connection.

## S3, Redis, and MCP

PepperX also speaks three protocols that already have mature client ecosystems, so no PepperX-specific
SDK is provided for them. Use the standard clients:

| Protocol | C# | Python | JavaScript |
|---|---|---|---|
| S3 | `AWSSDK.S3` | `boto3` | `@aws-sdk/client-s3` |
| Redis RESP | `StackExchange.Redis` | `redis-py` | `ioredis`, `node-redis` |
| MCP | any MCP client | any MCP client | any MCP client |

Point them at the relevant PepperX port and use path-style addressing for S3. See
[S3_API.md](../S3_API.md), [RESP_API.md](../RESP_API.md), and [MCP_API.md](../MCP_API.md).
