/**
 * The dashboard's single point of backend access.
 *
 * Views call named methods here rather than building URLs or calling fetch directly, so paging,
 * filtering, and error shapes stay consistent across the app. PepperX is unauthenticated, so there
 * are no credentials to attach.
 */

/** A normalized API error carrying the server's typed classification. */
export class ApiError extends Error {
  constructor(status, errorType, message, body = null) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.errorType = errorType;
    this.body = body;
  }

  /** Whether the failure looks like the server being unreachable rather than rejecting a request. */
  get isNetworkError() {
    return this.status === 0;
  }
}

/** Build a query string from a plain object, dropping empty values. */
export function buildQuery(params = {}) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue;
    search.append(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}

/**
 * Normalize the server's enumeration envelope into one paging shape used by every table.
 */
function toPage(payload) {
  return {
    items: payload?.Objects ?? [],
    totalCount: payload?.TotalRecords ?? 0,
    remaining: payload?.RecordsRemaining ?? 0,
    continuationToken: payload?.ContinuationToken ?? null,
    endOfResults: payload?.EndOfResults ?? true,
    maxResults: payload?.MaxResults ?? 100,
  };
}

/** Translate the dashboard's filter state into the server's enumeration query body. */
export function toEnumerationBody(filters = {}) {
  const body = {
    MaxResults: filters.pageSize ?? 25,
    Skip: filters.skip ?? 0,
    Ordering: filters.ordering ?? 'CreatedDescending',
    CaseInsensitive: filters.caseInsensitive ?? false,
  };
  if (filters.prefix) body.Prefix = filters.prefix;
  if (filters.suffix) body.Suffix = filters.suffix;
  if (filters.labels?.length) body.Labels = filters.labels;
  if (filters.tags && Object.keys(filters.tags).length > 0) body.Tags = filters.tags;
  if (filters.containers?.length) body.Containers = filters.containers;
  if (filters.createdAfterUtc) body.CreatedAfterUtc = filters.createdAfterUtc;
  if (filters.createdBeforeUtc) body.CreatedBeforeUtc = filters.createdBeforeUtc;
  return body;
}

/** Client for a single PepperX node. */
export default class ApiClient {
  constructor(baseUrl) {
    this.baseUrl = (baseUrl || '').replace(/\/+$/, '');
  }

  // ------------------------------------------------------------------ core

  async _request(method, path, { body, headers = {}, raw = false, signal } = {}) {
    const init = { method, headers: { ...headers }, signal };

    if (body !== undefined && body !== null && !(body instanceof Blob) && !(body instanceof Uint8Array)) {
      init.headers['Content-Type'] = 'application/json';
      init.body = JSON.stringify(body);
    } else if (body !== undefined && body !== null) {
      init.body = body;
    }

    let response;
    try {
      response = await fetch(this.baseUrl + path, init);
    } catch (error) {
      throw new ApiError(0, 'Unreachable', `Could not reach ${this.baseUrl}: ${error.message}`);
    }

    if (!response.ok) {
      const text = await response.text().catch(() => '');
      let errorType = 'InternalError';
      let message = `Request failed with status ${response.status}.`;
      try {
        const parsed = JSON.parse(text);
        if (parsed?.Error) errorType = parsed.Error;
        if (parsed?.Message) message = parsed.Message;
      } catch {
        // Not a typed payload; keep the status-derived message.
      }
      throw new ApiError(response.status, errorType, message, text || null);
    }

    if (raw) return response;
    if (response.status === 204) return null;

    const text = await response.text();
    return text ? JSON.parse(text) : null;
  }

  // ---------------------------------------------------------------- health

  /** Validate that the configured URL points at a live PepperX node. */
  async validate() {
    const info = await this._request('GET', '/');
    if (!info || info.Name !== 'PepperX') {
      throw new ApiError(0, 'Unreachable', 'That URL responded, but it is not a PepperX node.');
    }
    return info;
  }

  serverInfo() {
    return this._request('GET', '/');
  }

  // ------------------------------------------------------------ containers

  createContainer(name, tags, cache, respIndex) {
    const body = { Name: name };
    if (tags && Object.keys(tags).length > 0) body.Tags = tags;
    // Omitting Cache lets the server apply its own defaults; the create modal sends one explicitly so
    // the operator sees and controls what those defaults are.
    if (cache) body.Cache = cache;
    // A RESP database index is optional; only send it when the operator picked one. `null`/undefined
    // leaves the container unaddressable by an explicit SELECT index.
    if (respIndex !== undefined && respIndex !== null) body.RespDatabaseIndex = respIndex;
    return this._request('PUT', '/v1.0/containers', { body });
  }

  readContainer(name) {
    return this._request('GET', `/v1.0/containers/${encodeURIComponent(name)}`);
  }

  async enumerateContainers(filters = {}) {
    const payload = await this._request('POST', '/v1.0/containers/enumerate', {
      body: toEnumerationBody(filters),
    });
    return toPage(payload);
  }

  updateContainerTags(name, tags) {
    return this._request('PUT', `/v1.0/containers/${encodeURIComponent(name)}/tags`, { body: tags });
  }

  deleteContainer(name, force = false) {
    return this._request(
      'DELETE',
      `/v1.0/containers/${encodeURIComponent(name)}${force ? '?force=true' : ''}`,
    );
  }

  /** Read a container's cache settings together with its live per-node statistics. */
  containerCache(name) {
    return this._request('GET', `/v1.0/containers/${encodeURIComponent(name)}/cache`);
  }

  /** Replace a container's cache settings, returning the updated settings and statistics. */
  updateContainerCache(name, settings) {
    return this._request('PUT', `/v1.0/containers/${encodeURIComponent(name)}/cache`, { body: settings });
  }

  /**
   * Claim or clear a container's RESP (Redis) database index. Pass an integer to claim that index, or
   * `null` to clear it. Returns the updated container. Fails 409 if another container already holds
   * the index, 400 if it is negative.
   */
  updateContainerRespIndex(name, index) {
    return this._request('PUT', `/v1.0/containers/${encodeURIComponent(name)}/resp-index`, {
      body: { Index: index },
    });
  }

  /**
   * List a container's in-progress multipart uploads — those initiated but never completed or
   * aborted. Returns the server's envelope ({ uploads, isTruncated, nextKeyMarker, nextUploadIdMarker });
   * the cap is high enough that the console shows every upload without paging. Fails 404 if the
   * container does not exist.
   */
  containerMultipartUploads(name) {
    return this._request(
      'GET',
      `/v1.0/containers/${encodeURIComponent(name)}/multipart-uploads?maxUploads=1000`,
    );
  }

  /**
   * Abort an in-progress multipart upload, discarding any parts already staged. Idempotent: aborting
   * an upload that no longer exists still succeeds (204).
   */
  abortMultipartUpload(name, uploadId) {
    return this._request(
      'DELETE',
      `/v1.0/containers/${encodeURIComponent(name)}/multipart-uploads/${encodeURIComponent(uploadId)}`,
    );
  }

  /**
   * List the parts already staged for one in-progress multipart upload. Returns the server's envelope
   * ({ Parts, IsTruncated, NextPartNumberMarker }); the cap is high enough that the console shows every
   * part without paging. Fails 404 if the container or upload does not exist.
   */
  multipartUploadParts(name, uploadId) {
    return this._request(
      'GET',
      `/v1.0/containers/${encodeURIComponent(name)}/multipart-uploads/${encodeURIComponent(uploadId)}/parts?maxParts=1000`,
    );
  }

  /**
   * Read a single staged part's metadata (PartNumber, ETag, Md5, Sha256, SizeBytes, CreatedUtc). Fails
   * 404 if the container, upload, or part does not exist.
   */
  multipartUploadPart(name, uploadId, partNumber) {
    return this._request(
      'GET',
      `/v1.0/containers/${encodeURIComponent(name)}/multipart-uploads/${encodeURIComponent(uploadId)}/parts/${encodeURIComponent(partNumber)}`,
    );
  }

  /**
   * Delete a single staged part, discarding its data. Completing the upload will fail until the part is
   * re-uploaded. Fails 404 if the container, upload, or part does not exist.
   */
  deleteMultipartUploadPart(name, uploadId, partNumber) {
    return this._request(
      'DELETE',
      `/v1.0/containers/${encodeURIComponent(name)}/multipart-uploads/${encodeURIComponent(uploadId)}/parts/${encodeURIComponent(partNumber)}`,
    );
  }

  // --------------------------------------------------------------- objects

  objectUrl(container, key) {
    return `${this.baseUrl}/v1.0/containers/${encodeURIComponent(container)}/object?key=${encodeURIComponent(key)}`;
  }

  writeObject(container, key, data, { contentType, labels, tags, metadataObject, noOverwrite } = {}) {
    const headers = { 'Content-Type': contentType || 'application/octet-stream' };
    if (labels?.length) headers['x-pepperx-labels'] = labels.join(',');
    if (tags && Object.keys(tags).length > 0) {
      headers['x-pepperx-tags'] = Object.entries(tags)
        .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`)
        .join('&');
    }
    if (metadataObject !== undefined && metadataObject !== null) {
      headers['x-pepperx-object'] = btoa(unescape(encodeURIComponent(JSON.stringify(metadataObject))));
    }

    const path =
      `/v1.0/containers/${encodeURIComponent(container)}/object?key=${encodeURIComponent(key)}` +
      (noOverwrite ? '&nooverwrite=true' : '');

    return this._request('PUT', path, { body: data, headers });
  }

  readObjectMetadata(container, key) {
    return this._request(
      'GET',
      `/v1.0/containers/${encodeURIComponent(container)}/object/metadata?key=${encodeURIComponent(key)}`,
    );
  }

  updateObjectMetadata(container, key, update) {
    return this._request(
      'PUT',
      `/v1.0/containers/${encodeURIComponent(container)}/object/metadata?key=${encodeURIComponent(key)}`,
      { body: update },
    );
  }

  deleteObject(container, key) {
    return this._request(
      'DELETE',
      `/v1.0/containers/${encodeURIComponent(container)}/object?key=${encodeURIComponent(key)}`,
    );
  }

  async enumerateObjects(container, filters = {}) {
    const payload = await this._request(
      'POST',
      `/v1.0/containers/${encodeURIComponent(container)}/objects/enumerate`,
      { body: toEnumerationBody(filters) },
    );
    return toPage(payload);
  }

  async search(filters = {}) {
    const payload = await this._request('POST', '/v1.0/objects/enumerate', {
      body: toEnumerationBody(filters),
    });
    return toPage(payload);
  }

  // ----------------------------------------------------------------- admin

  statistics() {
    return this._request('GET', '/v1.0/admin/stats');
  }

  nodes() {
    return this._request('GET', '/v1.0/admin/nodes');
  }

  serverSettings() {
    return this._request('GET', '/v1.0/admin/settings');
  }

  /** Persist a partial settings update. Changes take effect after a restart. */
  updateServerSettings(update) {
    return this._request('PUT', '/v1.0/admin/settings', { body: update });
  }

  /** The complete settings document, every field, for full editing. */
  rawServerSettings() {
    return this._request('GET', '/v1.0/admin/settings/raw');
  }

  /** Replace the entire settings document. `settings` is a complete settings object. */
  updateRawServerSettings(settings) {
    return this._request('PUT', '/v1.0/admin/settings/raw', { body: settings });
  }

  /** Ask the node to exit so a container restart policy brings it back up on the new settings. */
  restartServer() {
    return this._request('POST', '/v1.0/admin/restart', { body: {} });
  }

  rehydrate(mode) {
    return this._request('POST', '/v1.0/admin/rehydrate', { body: { Mode: mode } });
  }

  // -------------------------------------------------------- request history

  requestHistory(filters = {}) {
    return this._request(
      'GET',
      `/v1.0/api/request-history${buildQuery({
        method: filters.method,
        statusCode: filters.statusCode,
        pathContains: filters.pathContains,
        fromUtc: filters.fromUtc,
        toUtc: filters.toUtc,
        pageNumber: filters.pageNumber ?? 1,
        pageSize: filters.pageSize ?? 25,
      })}`,
    );
  }

  requestHistorySummary({ fromUtc, toUtc, bucketMinutes, ...filters } = {}) {
    return this._request(
      'GET',
      `/v1.0/api/request-history/summary${buildQuery({
        fromUtc,
        toUtc,
        bucketMinutes,
        method: filters.method,
        statusCode: filters.statusCode,
        pathContains: filters.pathContains,
      })}`,
    );
  }

  requestHistoryEntry(id) {
    return this._request('GET', `/v1.0/api/request-history/${encodeURIComponent(id)}`);
  }

  deleteRequestHistoryEntry(id) {
    return this._request('DELETE', `/v1.0/api/request-history/${encodeURIComponent(id)}`);
  }

  deleteRequestHistoryBulk(filters = {}) {
    return this._request(
      'DELETE',
      `/v1.0/api/request-history${buildQuery({
        method: filters.method,
        statusCode: filters.statusCode,
        pathContains: filters.pathContains,
        fromUtc: filters.fromUtc,
        toUtc: filters.toUtc,
      })}`,
    );
  }

  // --------------------------------------------------------------- openapi

  openApiSpec() {
    return this._request('GET', '/openapi.json');
  }

  /**
   * Execute an arbitrary request for the API Explorer, returning the raw Response so the caller can
   * inspect status, headers, and timing.
   */
  executeExplorer({ method, path, query, headers, body }) {
    const url = this.baseUrl + path + buildQuery(query);
    const init = { method, headers: { ...(headers || {}) } };
    if (body !== undefined && body !== null && body !== '') {
      init.body = body;
      if (!init.headers['Content-Type']) init.headers['Content-Type'] = 'application/json';
    }
    return fetch(url, init);
  }
}
