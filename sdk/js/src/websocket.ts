/**
 * Typed WebSocket client for PepperX.
 *
 * Requests are correlated to responses by identifier, so many operations may be in flight on one
 * connection. Works with the browser `WebSocket` global, or with a Node implementation such as `ws`
 * passed via options.
 */

import { PepperXError } from './errors.js';
import type {
  ApiError,
  Container,
  EnumerationQuery,
  EnumerationResult,
  ObjectMetadata,
  ObjectWriteResult,
  Statistics,
  WriteObjectOptions,
} from './types.js';

/** Minimal WebSocket surface the client depends on. */
export interface WebSocketLike {
  send(data: string): void;
  close(): void;
  addEventListener(type: string, listener: (event: any) => void): void;
}

/** Options accepted by the WebSocket client constructor. */
export interface PepperXWebsocketClientOptions {
  /** How long an operation waits for its correlated response, in milliseconds. Default 100000. */
  timeoutMs?: number;
  /** WebSocket constructor, for environments without a global one (for example Node with `ws`). */
  WebSocketImpl?: new (url: string) => WebSocketLike;
}

interface ResponseEnvelope {
  RequestId?: string;
  Success?: boolean;
  StatusCode?: number;
  Error?: { Error?: string; Message?: string } | null;
  Result?: Record<string, any> | null;
  DataBase64?: string | null;
}

function decodeBase64(value: string): Uint8Array {
  if (typeof atob === 'function') {
    const binary = atob(value);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
  }
  return new Uint8Array(Buffer.from(value, 'base64'));
}

function encodeBase64(bytes: Uint8Array): string {
  if (typeof btoa === 'function') {
    let binary = '';
    for (const byte of bytes) binary += String.fromCharCode(byte);
    return btoa(binary);
  }
  return Buffer.from(bytes).toString('base64');
}

function toWireQuery(query: EnumerationQuery = {}): Record<string, unknown> {
  const body: Record<string, unknown> = {
    MaxResults: query.maxResults ?? 100,
    Skip: query.skip ?? 0,
    Ordering: query.ordering ?? 'CreatedDescending',
    CaseInsensitive: query.caseInsensitive ?? false,
  };
  if (query.prefix != null) body.Prefix = query.prefix;
  if (query.labels?.length) body.Labels = query.labels;
  if (query.tags && Object.keys(query.tags).length > 0) body.Tags = query.tags;
  if (query.containers?.length) body.Containers = query.containers;
  return body;
}

/**
 * Typed client for the PepperX WebSocket surface. Call {@link connect} before issuing operations.
 */
export class PepperXWebsocketClient {
  /** WebSocket URL of the target node. */
  readonly url: string;

  private readonly timeoutMs: number;
  private readonly WebSocketImpl: new (url: string) => WebSocketLike;
  private readonly pending = new Map<
    string,
    { resolve: (value: ResponseEnvelope) => void; reject: (reason: unknown) => void; timer: ReturnType<typeof setTimeout> }
  >();

  private socket: WebSocketLike | null = null;

  /**
   * @param url WebSocket URL, for example `ws://localhost:8002/`.
   * @param options Optional timeout and WebSocket implementation.
   */
  constructor(url = 'ws://localhost:8002/', options: PepperXWebsocketClientOptions = {}) {
    if (!url) throw new Error('url is required.');
    this.url = url;
    this.timeoutMs = options.timeoutMs ?? 100000;

    const impl = options.WebSocketImpl ?? (globalThis as any).WebSocket;
    if (!impl) {
      throw new Error('No WebSocket implementation is available; pass one via options.WebSocketImpl.');
    }
    this.WebSocketImpl = impl;
  }

  /** Whether the client currently holds an open connection. */
  get isConnected(): boolean {
    return this.socket !== null;
  }

  /** Connect to the server and start receiving responses. */
  async connect(): Promise<void> {
    if (this.socket) return;

    await new Promise<void>((resolve, reject) => {
      const socket = new this.WebSocketImpl(this.url);

      socket.addEventListener('open', () => {
        this.socket = socket;
        resolve();
      });

      socket.addEventListener('error', (event: any) => {
        reject(new Error(`WebSocket connection failed: ${event?.message ?? 'unknown error'}`));
      });

      socket.addEventListener('message', (event: any) => {
        this.onMessage(typeof event.data === 'string' ? event.data : String(event.data));
      });

      socket.addEventListener('close', () => {
        this.socket = null;
        this.failAll(new PepperXError('InternalError', 499, 'The WebSocket connection closed.'));
      });
    });
  }

  /** Close the connection and fail any in-flight operations. */
  async close(): Promise<void> {
    const socket = this.socket;
    this.socket = null;
    if (socket) socket.close();
    this.failAll(new PepperXError('InternalError', 499, 'The client closed before a response arrived.'));
  }

  /** Resolve true when the node responds successfully. */
  async health(): Promise<boolean> {
    const response = await this.send('Health');
    return response.Success === true;
  }

  /** Create a container. */
  async createContainer(name: string, tags?: Record<string, string>): Promise<Container> {
    const body: Record<string, unknown> = { Name: name };
    if (tags) body.Tags = tags;
    const data = this.require(await this.send('ContainerCreate', { body }));
    return {
      id: data.Id ?? '',
      name: data.Name ?? '',
      tags: data.Tags ?? {},
      objectCount: data.ObjectCount ?? 0,
      totalBytes: data.TotalBytes ?? 0,
      createdUtc: new Date(data.CreatedUtc ?? 0),
      lastUpdateUtc: new Date(data.LastUpdateUtc ?? 0),
    };
  }

  /** Delete an empty container. */
  async deleteContainer(name: string): Promise<void> {
    this.throwIfError(await this.send('ContainerDelete', { container: name }));
  }

  /** Write an object. */
  async writeObject(
    container: string,
    key: string,
    payload: Uint8Array,
    options: WriteObjectOptions = {},
  ): Promise<ObjectWriteResult> {
    const body: Record<string, unknown> = {
      DataBase64: encodeBase64(payload),
      NoOverwrite: options.noOverwrite ?? false,
    };
    if (options.contentType) body.ContentType = options.contentType;
    if (options.labels?.length) body.Labels = options.labels;
    if (options.tags && Object.keys(options.tags).length > 0) body.Tags = options.tags;
    if (options.metadataObject !== undefined) body.Object = options.metadataObject;

    const data = this.require(await this.send('ObjectWrite', { container, key, body }));
    return {
      extentId: data.ExtentId ?? '',
      key: data.Key ?? '',
      containerId: data.ContainerId ?? '',
      sizeBytes: data.SizeBytes ?? 0,
      sha256: data.Sha256 ?? '',
      contentType: data.ContentType ?? null,
      replaced: data.Replaced ?? false,
    };
  }

  /** Read an object's payload, or resolve null when it does not exist. */
  async readObject(container: string, key: string): Promise<Uint8Array | null> {
    const response = await this.send('ObjectRead', { container, key });
    if (response.StatusCode === 404) return null;
    this.throwIfError(response);
    return response.DataBase64 ? decodeBase64(response.DataBase64) : new Uint8Array();
  }

  /** Read an object's metadata, or resolve null when it does not exist. */
  async readObjectMetadata(container: string, key: string): Promise<ObjectMetadata | null> {
    const response = await this.send('ObjectReadMetadata', { container, key });
    if (response.StatusCode === 404) return null;
    const data = this.require(response);
    return {
      key: data.Key ?? '',
      extentId: data.ExtentId ?? '',
      containerId: data.ContainerId ?? '',
      containerName: data.ContainerName ?? null,
      sizeBytes: data.SizeBytes ?? 0,
      sha256: data.Sha256 ?? '',
      contentType: data.ContentType ?? null,
      labels: data.Labels ?? [],
      tags: data.Tags ?? {},
      metadataObject: data.Object ?? null,
      hasMetadataObject: data.HasMetadataObject ?? false,
      createdUtc: new Date(data.CreatedUtc ?? 0),
    };
  }

  /** Delete an object. Resolves false when it did not exist. */
  async deleteObject(container: string, key: string): Promise<boolean> {
    const response = await this.send('ObjectDelete', { container, key });
    if (response.StatusCode === 404) return false;
    this.throwIfError(response);
    return true;
  }

  /** Enumerate or search objects within a container. */
  async enumerateObjects(
    container: string,
    query: EnumerationQuery = {},
  ): Promise<EnumerationResult<ObjectMetadata>> {
    const data = this.require(
      await this.send('ObjectEnumerate', { container, body: toWireQuery(query) }),
    );
    return {
      success: data.Success ?? true,
      maxResults: data.MaxResults ?? 100,
      continuationToken: data.ContinuationToken ?? null,
      endOfResults: data.EndOfResults ?? true,
      totalRecords: data.TotalRecords ?? 0,
      recordsRemaining: data.RecordsRemaining ?? 0,
      objects: (data.Objects ?? []).map((o: Record<string, any>) => ({
        key: o.Key ?? '',
        extentId: o.ExtentId ?? '',
        containerId: o.ContainerId ?? '',
        containerName: o.ContainerName ?? null,
        sizeBytes: o.SizeBytes ?? 0,
        sha256: o.Sha256 ?? '',
        contentType: o.ContentType ?? null,
        labels: o.Labels ?? [],
        tags: o.Tags ?? {},
        metadataObject: o.Object ?? null,
        hasMetadataObject: o.HasMetadataObject ?? false,
        createdUtc: new Date(o.CreatedUtc ?? 0),
      })),
    };
  }

  /** Read aggregate statistics. */
  async statistics(): Promise<Statistics> {
    const data = this.require(await this.send('AdminStats'));
    return {
      containerCount: data.ContainerCount ?? 0,
      objectCount: data.ObjectCount ?? 0,
      totalBytes: data.TotalBytes ?? 0,
      containers: [],
      storageTotalBytes: data.StorageTotalBytes ?? 0,
      storageFreeBytes: data.StorageFreeBytes ?? 0,
      databaseSizeBytes: data.DatabaseSizeBytes ?? 0,
      nodes: [],
    };
  }

  // -------------------------------------------------------------------- private

  private send(
    operation: string,
    options: { container?: string; key?: string; body?: unknown } = {},
  ): Promise<ResponseEnvelope> {
    if (!this.socket) throw new Error('The client is not connected; call connect() first.');

    const requestId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
    const envelope: Record<string, unknown> = { RequestId: requestId, Operation: operation };
    if (options.container !== undefined) envelope.Container = options.container;
    if (options.key !== undefined) envelope.Key = options.key;
    if (options.body !== undefined) envelope.Body = options.body;

    return new Promise<ResponseEnvelope>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(requestId);
        reject(new PepperXError('InternalError', 504, 'Timed out waiting for a WebSocket response.'));
      }, this.timeoutMs);

      this.pending.set(requestId, { resolve, reject, timer });
      this.socket!.send(JSON.stringify(envelope));
    });
  }

  private onMessage(raw: string): void {
    let envelope: ResponseEnvelope;
    try {
      envelope = JSON.parse(raw);
    } catch {
      return;
    }

    const id = envelope.RequestId;
    if (!id) return;

    const entry = this.pending.get(id);
    if (!entry) return;

    clearTimeout(entry.timer);
    this.pending.delete(id);
    entry.resolve(envelope);
  }

  private failAll(error: unknown): void {
    for (const entry of this.pending.values()) {
      clearTimeout(entry.timer);
      entry.reject(error);
    }
    this.pending.clear();
  }

  private throwIfError(response: ResponseEnvelope): void {
    if (response.Success) return;
    const errorType = (response.Error?.Error ?? 'InternalError') as ApiError;
    const status = response.StatusCode ?? 500;
    const message = response.Error?.Message ?? `The operation failed with status ${status}.`;
    throw new PepperXError(errorType, status, message);
  }

  private require(response: ResponseEnvelope): Record<string, any> {
    this.throwIfError(response);
    if (!response.Result) {
      throw new PepperXError('InternalError', response.StatusCode ?? 500, 'The server returned no result payload.');
    }
    return response.Result;
  }
}
