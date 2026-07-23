/**
 * Typed REST client for PepperX, built on the platform `fetch` API.
 *
 * PepperX is unauthenticated by design, so there are no credentials to configure.
 */

import { PepperXError } from './errors.js';
import type {
  ApiError,
  Container,
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

/** Payload types accepted when writing an object. */
export type ObjectPayload = Uint8Array | ArrayBuffer | string | Blob | ReadableStream<Uint8Array>;

/** Options accepted by the client constructor. */
export interface PepperXClientOptions {
  /** Request timeout in milliseconds. Default 100000. */
  timeoutMs?: number;
  /** Custom fetch implementation, for environments without a global one. */
  fetch?: typeof globalThis.fetch;
}

interface WireEnumerationResult<T> {
  Success?: boolean;
  MaxResults?: number;
  ContinuationToken?: string | null;
  EndOfResults?: boolean;
  TotalRecords?: number;
  RecordsRemaining?: number;
  Objects?: T[];
}

function toDate(value: unknown): Date {
  return typeof value === 'string' ? new Date(value) : new Date(0);
}

function toContainer(data: Record<string, any>): Container {
  return {
    id: data.Id ?? '',
    name: data.Name ?? '',
    tags: data.Tags ?? {},
    objectCount: data.ObjectCount ?? 0,
    totalBytes: data.TotalBytes ?? 0,
    createdUtc: toDate(data.CreatedUtc),
    lastUpdateUtc: toDate(data.LastUpdateUtc),
  };
}

function toObjectMetadata(data: Record<string, any>): ObjectMetadata {
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
    createdUtc: toDate(data.CreatedUtc),
  };
}

function toWriteResult(data: Record<string, any>): ObjectWriteResult {
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

function toPage<TWire, TResult>(
  payload: WireEnumerationResult<TWire>,
  map: (item: TWire) => TResult,
): EnumerationResult<TResult> {
  return {
    success: payload.Success ?? true,
    maxResults: payload.MaxResults ?? 100,
    continuationToken: payload.ContinuationToken ?? null,
    endOfResults: payload.EndOfResults ?? true,
    totalRecords: payload.TotalRecords ?? 0,
    recordsRemaining: payload.RecordsRemaining ?? 0,
    objects: (payload.Objects ?? []).map(map),
  };
}

function toWireQuery(query: EnumerationQuery = {}): Record<string, unknown> {
  const body: Record<string, unknown> = {
    MaxResults: query.maxResults ?? 100,
    Skip: query.skip ?? 0,
    Ordering: query.ordering ?? 'CreatedDescending',
    CaseInsensitive: query.caseInsensitive ?? false,
  };
  if (query.continuationToken != null) body.ContinuationToken = query.continuationToken;
  if (query.prefix != null) body.Prefix = query.prefix;
  if (query.suffix != null) body.Suffix = query.suffix;
  if (query.createdAfterUtc) body.CreatedAfterUtc = query.createdAfterUtc.toISOString();
  if (query.createdBeforeUtc) body.CreatedBeforeUtc = query.createdBeforeUtc.toISOString();
  if (query.labels?.length) body.Labels = query.labels;
  if (query.tags && Object.keys(query.tags).length > 0) body.Tags = query.tags;
  if (query.containers?.length) body.Containers = query.containers;
  return body;
}

function encodeTags(tags: Record<string, string>): string {
  return Object.entries(tags)
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
    .join('&');
}

function toBase64(text: string): string {
  if (typeof btoa === 'function') {
    return btoa(unescape(encodeURIComponent(text)));
  }
  return Buffer.from(text, 'utf-8').toString('base64');
}

/**
 * Typed client for the PepperX native REST API.
 *
 * Every method returns a typed object. Failures raise {@link PepperXError}; reads of missing
 * resources resolve to `null` instead.
 */
export class PepperXClient {
  /** Base URL of the target node. */
  readonly baseUrl: string;

  private readonly timeoutMs: number;
  private readonly fetchImpl: typeof globalThis.fetch;

  /**
   * @param baseUrl Base URL, for example `http://localhost:8000`.
   * @param options Optional timeout and fetch overrides.
   */
  constructor(baseUrl = 'http://localhost:8000', options: PepperXClientOptions = {}) {
    if (!baseUrl) throw new Error('baseUrl is required.');
    this.baseUrl = baseUrl.replace(/\/+$/, '');
    this.timeoutMs = options.timeoutMs ?? 100000;
    const impl = options.fetch ?? globalThis.fetch;
    if (!impl) throw new Error('No fetch implementation is available; pass one via options.fetch.');
    this.fetchImpl = impl.bind(globalThis);
  }

  // --------------------------------------------------------------------- health

  /** Resolve true when the node responds successfully. */
  async health(): Promise<boolean> {
    try {
      const response = await this.send('GET', '/v1.0/api/health');
      return response.ok;
    } catch {
      return false;
    }
  }

  // ----------------------------------------------------------------- containers

  /**
   * Create a container.
   * @throws {PepperXError} When the name is already taken.
   */
  async createContainer(name: string, tags?: Record<string, string>): Promise<Container> {
    const body: Record<string, unknown> = { Name: name };
    if (tags) body.Tags = tags;
    const response = await this.send('PUT', '/v1.0/containers', { json: body });
    await this.throwIfError(response);
    return toContainer(await response.json());
  }

  /** Read a container, or resolve null when it does not exist. */
  async readContainer(name: string): Promise<Container | null> {
    const response = await this.send('GET', `/v1.0/containers/${encodeURIComponent(name)}`);
    if (response.status === 404) return null;
    await this.throwIfError(response);
    return toContainer(await response.json());
  }

  /** Resolve true when the container exists. */
  async containerExists(name: string): Promise<boolean> {
    const response = await this.send('HEAD', `/v1.0/containers/${encodeURIComponent(name)}`);
    return response.status !== 404;
  }

  /** Enumerate containers. */
  async enumerateContainers(query: EnumerationQuery = {}): Promise<EnumerationResult<Container>> {
    const response = await this.send('POST', '/v1.0/containers/enumerate', { json: toWireQuery(query) });
    await this.throwIfError(response);
    return toPage(await response.json(), toContainer);
  }

  /** Replace a container's tags. */
  async updateContainerTags(name: string, tags: Record<string, string>): Promise<Container> {
    const response = await this.send('PUT', `/v1.0/containers/${encodeURIComponent(name)}/tags`, {
      json: tags ?? {},
    });
    await this.throwIfError(response);
    return toContainer(await response.json());
  }

  /**
   * Delete a container.
   * @param force Delete the container's objects first.
   */
  async deleteContainer(name: string, force = false): Promise<void> {
    let path = `/v1.0/containers/${encodeURIComponent(name)}`;
    if (force) path += '?force=true';
    const response = await this.send('DELETE', path);
    await this.throwIfError(response);
  }

  // -------------------------------------------------------------------- objects

  /**
   * Write an object. Keys may contain any characters, including slashes.
   *
   * A `ReadableStream` payload is sent with chunked transfer-encoding, which the server materializes
   * before storing; the other payload types declare a length and stream straight through.
   */
  async writeObject(
    container: string,
    key: string,
    payload: ObjectPayload,
    options: WriteObjectOptions = {},
  ): Promise<ObjectWriteResult> {
    let path = this.objectPath(container, key);
    if (options.noOverwrite) path += '&nooverwrite=true';

    const headers: Record<string, string> = {
      'Content-Type': options.contentType ?? 'application/octet-stream',
    };
    if (options.labels?.length) headers['x-pepperx-labels'] = options.labels.join(',');
    if (options.tags && Object.keys(options.tags).length > 0) {
      headers['x-pepperx-tags'] = encodeTags(options.tags);
    }
    if (options.metadataObject !== undefined) {
      headers['x-pepperx-object'] = toBase64(JSON.stringify(options.metadataObject));
    }

    const response = await this.send('PUT', path, { body: payload as BodyInit, headers });
    await this.throwIfError(response);
    return toWriteResult(await response.json());
  }

  /** Read an object into memory, or resolve null when it does not exist. */
  async readObject(container: string, key: string): Promise<ObjectReadResult | null> {
    const response = await this.send('GET', this.objectPath(container, key));
    if (response.status === 404) return null;
    await this.throwIfError(response);

    return {
      data: new Uint8Array(await response.arrayBuffer()),
      contentType: response.headers.get('content-type'),
      extentId: response.headers.get('x-pepperx-extent-id'),
      sha256: response.headers.get('x-pepperx-sha256'),
      hasMetadataObject: response.headers.get('x-pepperx-object-available') === 'true',
    };
  }

  /**
   * Open an object as a stream, without buffering it in memory. Resolves null when the object does
   * not exist.
   */
  async openObject(container: string, key: string): Promise<ReadableStream<Uint8Array> | null> {
    const response = await this.send('GET', this.objectPath(container, key));
    if (response.status === 404) return null;
    await this.throwIfError(response);
    return response.body;
  }

  /** Read an object's metadata, or resolve null when it does not exist. */
  async readObjectMetadata(container: string, key: string): Promise<ObjectMetadata | null> {
    const response = await this.send('GET', this.objectMetadataPath(container, key));
    if (response.status === 404) return null;
    await this.throwIfError(response);
    return toObjectMetadata(await response.json());
  }

  /** Update an object's metadata, preserving its payload. */
  async updateObjectMetadata(
    container: string,
    key: string,
    options: UpdateMetadataOptions = {},
  ): Promise<ObjectWriteResult> {
    const body: Record<string, unknown> = { ClearObject: options.clearObject ?? false };
    if (options.labels !== undefined) body.Labels = options.labels;
    if (options.tags !== undefined) body.Tags = options.tags;
    if (options.metadataObject !== undefined) body.Object = options.metadataObject;

    const response = await this.send('PUT', this.objectMetadataPath(container, key), { json: body });
    await this.throwIfError(response);
    return toWriteResult(await response.json());
  }

  /** Resolve true when the object exists. */
  async objectExists(container: string, key: string): Promise<boolean> {
    const response = await this.send('HEAD', this.objectPath(container, key));
    return response.status !== 404;
  }

  /** Delete an object. Resolves false when it did not exist. */
  async deleteObject(container: string, key: string): Promise<boolean> {
    const response = await this.send('DELETE', this.objectPath(container, key));
    if (response.status === 404) return false;
    await this.throwIfError(response);
    return true;
  }

  /** Enumerate or search objects within a container. */
  async enumerateObjects(
    container: string,
    query: EnumerationQuery = {},
  ): Promise<EnumerationResult<ObjectMetadata>> {
    const response = await this.send(
      'POST',
      `/v1.0/containers/${encodeURIComponent(container)}/objects/enumerate`,
      { json: toWireQuery(query) },
    );
    await this.throwIfError(response);
    return toPage(await response.json(), toObjectMetadata);
  }

  /** Search objects across containers. */
  async search(query: EnumerationQuery = {}): Promise<EnumerationResult<ObjectMetadata>> {
    const response = await this.send('POST', '/v1.0/objects/enumerate', { json: toWireQuery(query) });
    await this.throwIfError(response);
    return toPage(await response.json(), toObjectMetadata);
  }

  // ---------------------------------------------------------------------- admin

  /** Read aggregate statistics. */
  async statistics(): Promise<Statistics> {
    const response = await this.send('GET', '/v1.0/admin/stats');
    await this.throwIfError(response);
    const data = await response.json();
    return {
      containerCount: data.ContainerCount ?? 0,
      objectCount: data.ObjectCount ?? 0,
      totalBytes: data.TotalBytes ?? 0,
      containers: (data.Containers ?? []).map((c: Record<string, any>) => ({
        id: c.Id ?? '',
        name: c.Name ?? '',
        objectCount: c.ObjectCount ?? 0,
        totalBytes: c.TotalBytes ?? 0,
      })),
      storageTotalBytes: data.StorageTotalBytes ?? 0,
      storageFreeBytes: data.StorageFreeBytes ?? 0,
      databaseSizeBytes: data.DatabaseSizeBytes ?? 0,
      nodes: (data.Nodes ?? []).map(
        (n: Record<string, any>): Node => ({
          id: n.Id ?? '',
          hostname: n.Hostname ?? null,
          heartbeatAgeSeconds: n.HeartbeatAgeSeconds ?? 0,
          isAlive: n.IsAlive ?? true,
        }),
      ),
    };
  }

  /** List cluster nodes. */
  async nodes(): Promise<Node[]> {
    const response = await this.send('GET', '/v1.0/admin/nodes');
    await this.throwIfError(response);
    const data = await response.json();
    return (data ?? []).map(
      (n: Record<string, any>): Node => ({
        id: n.Id ?? '',
        hostname: n.Hostname ?? null,
        heartbeatAgeSeconds: n.HeartbeatAgeSeconds ?? 0,
        isAlive: n.IsAlive ?? true,
      }),
    );
  }

  // -------------------------------------------------------------------- private

  private objectPath(container: string, key: string): string {
    return `/v1.0/containers/${encodeURIComponent(container)}/object?key=${encodeURIComponent(key)}`;
  }

  private objectMetadataPath(container: string, key: string): string {
    return `/v1.0/containers/${encodeURIComponent(container)}/object/metadata?key=${encodeURIComponent(key)}`;
  }

  private async send(
    method: string,
    path: string,
    options: { json?: unknown; body?: BodyInit; headers?: Record<string, string> } = {},
  ): Promise<Response> {
    const headers: Record<string, string> = { ...(options.headers ?? {}) };
    let body: BodyInit | undefined = options.body;

    if (options.json !== undefined) {
      headers['Content-Type'] = 'application/json';
      body = JSON.stringify(options.json);
    }

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), this.timeoutMs);

    try {
      const init: RequestInit = { method, headers, body, signal: controller.signal };
      if (body instanceof ReadableStream) {
        // Streaming request bodies require half-duplex mode in undici-based runtimes.
        (init as RequestInit & { duplex?: string }).duplex = 'half';
      }
      return await this.fetchImpl(this.baseUrl + path, init);
    } finally {
      clearTimeout(timer);
    }
  }

  private async throwIfError(response: Response): Promise<void> {
    if (response.ok) return;

    const body = await response.text().catch(() => '');
    let errorType: ApiError = 'InternalError';
    let message = `Request failed with status ${response.status}.`;

    if (body) {
      try {
        const parsed = JSON.parse(body);
        if (parsed?.Error) errorType = parsed.Error as ApiError;
        if (parsed?.Message) message = parsed.Message;
      } catch {
        // Not a typed error payload; keep the status-derived message.
      }
    }

    throw new PepperXError(errorType, response.status, message, body || null);
  }
}
