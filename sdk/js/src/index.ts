/**
 * PepperX client SDK.
 *
 * A typed TypeScript client for PepperX, a high-performance key-value store with rich metadata.
 * PepperX is unauthenticated by design, so no credentials are required.
 *
 * @example
 * ```ts
 * import { PepperXClient } from '@pepperx/sdk';
 *
 * const client = new PepperXClient('http://localhost:8000');
 * await client.createContainer('photos');
 * await client.writeObject('photos', '2026/07/cat.jpg', bytes, { labels: ['animal'] });
 * ```
 */

export { PepperXClient } from './client.js';
export type { ObjectPayload, PepperXClientOptions } from './client.js';
export { PepperXError } from './errors.js';
export { PepperXWebsocketClient } from './websocket.js';
export type { PepperXWebsocketClientOptions, WebSocketLike } from './websocket.js';
export type {
  ApiError,
  Container,
  ContainerStatistics,
  EnumerationOrder,
  EnumerationQuery,
  EnumerationResult,
  Node,
  ObjectMetadata,
  ObjectReadResult,
  ObjectWriteResult,
  Statistics,
  UpdateMetadataOptions,
  WriteObjectOptions,
} from './types.js';
