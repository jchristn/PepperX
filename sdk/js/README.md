# PepperX JavaScript SDK

Typed TypeScript client for [PepperX](../../README.md). Two clients ship in the package:

- **`PepperXClient`** — the native REST surface, built on the platform `fetch` API (no `axios`).
- **`PepperXWebsocketClient`** — the same operations over one persistent connection, with many
  requests in flight at once.

Every method returns a typed object. Server errors throw `PepperXError`, which carries the server's
`ApiError` classification and HTTP status code, so you branch on values rather than parsing message
text.

PepperX is unauthenticated by design — it is backend infrastructure, and access control belongs to
the application in front of it. There are no credentials to configure.

## Install

```bash
npm install @pepperx/sdk
```

Requires Node 18 or newer (for the built-in `fetch`), or any modern browser.

## Quickstart

```ts
import { PepperXClient } from '@pepperx/sdk';

const client = new PepperXClient('http://localhost:8000');

await client.createContainer('photos');

const written = await client.writeObject(
  'photos',
  '2026/07/cat.jpg',                       // keys may contain slashes
  new Uint8Array(await file.arrayBuffer()),
  {
    contentType: 'image/jpeg',
    labels: ['animal', 'cute'],
    tags: { team: 'mammals' },
    metadataObject: { camera: 'X100V', iso: 400 },   // freeform JSON metadata
  },
);

const read = await client.readObject('photos', '2026/07/cat.jpg');
console.log(read!.data.length, 'bytes, sha256', read!.sha256);
```

### Searching by labels and tags

Both filters use AND semantics: every label listed must be present, and every tag key must be
present with the given value.

```ts
const found = await client.enumerateObjects('photos', {
  labels: ['animal'],
  tags: { team: 'mammals' },
  maxResults: 50,
});

console.log(found.totalRecords, 'matches,', found.recordsRemaining, 'remaining');
for (const item of found.objects) {
  console.log(item.key, item.sizeBytes, item.labels);
}
```

`client.search({ ... })` runs the same query across every container; set `containers` to narrow it.

### Streaming

Reading returns a `ReadableStream`, so large objects never have to be held in memory:

```ts
const stream = await client.openObject('backups', 'archive.tar');
const reader = stream!.getReader();
for (;;) {
  const { done, value } = await reader.read();
  if (done) break;
  process(value);
}
```

Passing a `ReadableStream` to `writeObject` sends chunked transfer-encoding, which the server
materializes before storing. Passing a `Uint8Array`, `Blob`, or string declares a length and streams
straight through to storage.

### Handling errors

```ts
import { PepperXError } from '@pepperx/sdk';

try {
  await client.createContainer('photos');
} catch (error) {
  if (error instanceof PepperXError && error.errorType === 'Conflict') {
    // Already exists.
  } else {
    throw error;
  }
}
```

Reads of missing resources resolve to `null` rather than throwing: `readContainer`, `readObject`,
`readObjectMetadata`, and `openObject`.

## WebSockets

Browsers supply a `WebSocket` global; on Node, pass an implementation such as [`ws`](https://www.npmjs.com/package/ws):

```ts
import WebSocket from 'ws';
import { PepperXWebsocketClient } from '@pepperx/sdk';

const client = new PepperXWebsocketClient('ws://localhost:8002/', { WebSocketImpl: WebSocket });
await client.connect();

await client.createContainer('events');
await client.writeObject('events', 'e-1', new TextEncoder().encode('payload'));

// Concurrent calls on one connection are correlated by request id.
await Promise.all(Array.from({ length: 16 }, () => client.health()));

await client.close();
```

## Other protocols

PepperX also speaks S3, Redis RESP, and MCP. Those surfaces are best consumed with their standard
clients — the AWS SDK for JavaScript, `ioredis` or `node-redis`, and any MCP client — rather than
through this SDK. See the protocol references in the [repository root](../../README.md).

## Development

```bash
npm install
npm run build          # emits dist/ with .d.ts declarations
npm run lint           # type-check without emitting

export PEPPERX_URL=http://localhost:8000
export PEPPERX_WS_URL=ws://localhost:8002/

npm test                        # skips when no node is reachable
node examples/console.mjs       # narrated end-to-end walkthrough
```
