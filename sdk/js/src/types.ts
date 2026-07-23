/**
 * Typed models mirroring the PepperX wire contracts.
 */

/** Machine-readable error classification returned by the server. */
export type ApiError =
  | 'BadRequest'
  | 'NotFound'
  | 'Conflict'
  | 'NotEmpty'
  | 'TooLarge'
  | 'Deleting'
  | 'NotImplemented'
  | 'InternalError';

/** Sort ordering for an enumeration result set. */
export type EnumerationOrder =
  | 'CreatedAscending'
  | 'CreatedDescending'
  | 'KeyAscending'
  | 'KeyDescending';

/**
 * Query parameters for paginated, filtered enumeration.
 *
 * `labels` and `tags` both use AND semantics: every label listed must be present, and every tag key
 * must be present with the given value.
 */
export interface EnumerationQuery {
  /** Maximum results per page; the server clamps this to 1..1000. Default 100. */
  maxResults?: number;
  /** Records to skip. Mutually exclusive with `continuationToken`. */
  skip?: number;
  /** Continuation token from a previous page; valid only with creation ordering. */
  continuationToken?: string | null;
  /** Result ordering. Default `CreatedDescending`. */
  ordering?: EnumerationOrder;
  /** Keep only records whose key or name starts with this prefix. */
  prefix?: string;
  /** Keep only records whose key or name ends with this suffix. */
  suffix?: string;
  /** Keep only records created strictly after this time. */
  createdAfterUtc?: Date;
  /** Keep only records created strictly before this time. */
  createdBeforeUtc?: Date;
  /** Label filter (AND semantics). */
  labels?: string[];
  /** Tag filter (AND semantics). */
  tags?: Record<string, string>;
  /** Compare labels and tags case-insensitively. Default false. */
  caseInsensitive?: boolean;
  /** Restrict a cross-container search to these container names. */
  containers?: string[];
}

/** A container: the top-level scope that holds objects. */
export interface Container {
  /** Container identifier. */
  id: string;
  /** Container name. */
  name: string;
  /** Container tags. */
  tags: Record<string, string>;
  /** Number of active objects. */
  objectCount: number;
  /** Total bytes across active objects. */
  totalBytes: number;
  /** Creation time. */
  createdUtc: Date;
  /** Last update time. */
  lastUpdateUtc: Date;
}

/**
 * Metadata for a stored object. `metadataObject` holds the freeform JSON attached to the object;
 * listings omit it for performance, so read an object's metadata directly to retrieve it.
 */
export interface ObjectMetadata {
  /** Object key. */
  key: string;
  /** Backing extent identifier. */
  extentId: string;
  /** Owning container identifier. */
  containerId: string;
  /** Owning container name, when resolved. */
  containerName: string | null;
  /** Payload size in bytes. */
  sizeBytes: number;
  /** Lowercase hex SHA-256 of the payload. */
  sha256: string;
  /** Content type, when set. */
  contentType: string | null;
  /** Labels attached to the object. */
  labels: string[];
  /** Tags attached to the object. */
  tags: Record<string, string>;
  /** Freeform JSON metadata, when present and requested. */
  metadataObject: unknown;
  /** Whether the object carries a metadata object. */
  hasMetadataObject: boolean;
  /** Creation time. */
  createdUtc: Date;
}

/** The result of writing (creating or replacing) an object. */
export interface ObjectWriteResult {
  /** Identifier of the extent that now backs the key. */
  extentId: string;
  /** Object key. */
  key: string;
  /** Owning container identifier. */
  containerId: string;
  /** Payload size in bytes. */
  sizeBytes: number;
  /** Lowercase hex SHA-256 of the payload. */
  sha256: string;
  /** Content type, when set. */
  contentType: string | null;
  /** Whether the write replaced an existing object. */
  replaced: boolean;
}

/** An object's payload plus the identifying headers the server returned. */
export interface ObjectReadResult {
  /** Payload bytes. */
  data: Uint8Array;
  /** Content type reported by the server. */
  contentType: string | null;
  /** Identifier of the extent that served the read. */
  extentId: string | null;
  /** Lowercase hex SHA-256 of the payload. */
  sha256: string | null;
  /** Whether the object carries a freeform metadata object. */
  hasMetadataObject: boolean;
}

/** Metadata to attach when writing an object. */
export interface WriteObjectOptions {
  /** Content type of the payload. */
  contentType?: string;
  /** Labels to attach. */
  labels?: string[];
  /** Tags to attach. */
  tags?: Record<string, string>;
  /** Freeform JSON metadata to attach. */
  metadataObject?: unknown;
  /** Fail instead of overwriting when the key exists. Default false. */
  noOverwrite?: boolean;
}

/** Metadata changes to apply to an existing object; the payload is preserved. */
export interface UpdateMetadataOptions {
  /** New label set, or omit to leave labels unchanged. */
  labels?: string[];
  /** New tag set, or omit to leave tags unchanged. */
  tags?: Record<string, string>;
  /** New metadata object, or omit to leave it unchanged. */
  metadataObject?: unknown;
  /** Remove the metadata object. Default false. */
  clearObject?: boolean;
}

/** A page of enumeration results. */
export interface EnumerationResult<T> {
  /** Whether the enumeration succeeded. */
  success: boolean;
  /** Maximum records requested per page. */
  maxResults: number;
  /** Continuation token for the next page, when more remain. */
  continuationToken: string | null;
  /** Whether this page is the last. */
  endOfResults: boolean;
  /** Total records matching the query. */
  totalRecords: number;
  /** Records not yet returned. */
  recordsRemaining: number;
  /** Records in this page. */
  objects: T[];
}

/** Per-container rollup within a statistics response. */
export interface ContainerStatistics {
  /** Container identifier. */
  id: string;
  /** Container name. */
  name: string;
  /** Number of active objects. */
  objectCount: number;
  /** Total bytes across active objects. */
  totalBytes: number;
}

/** A cluster node and its liveness. */
export interface Node {
  /** Node identifier. */
  id: string;
  /** Host name reported by the node. */
  hostname: string | null;
  /** Seconds since the node's last heartbeat. */
  heartbeatAgeSeconds: number;
  /** Whether the node is considered alive. */
  isAlive: boolean;
}

/** Aggregate statistics across containers, storage, database, and cluster nodes. */
export interface Statistics {
  /** Total containers. */
  containerCount: number;
  /** Total active objects. */
  objectCount: number;
  /** Total bytes stored. */
  totalBytes: number;
  /** Per-container rollups. */
  containers: ContainerStatistics[];
  /** Storage volume capacity in bytes. */
  storageTotalBytes: number;
  /** Storage volume free space in bytes. */
  storageFreeBytes: number;
  /** Approximate metadata database size in bytes. */
  databaseSizeBytes: number;
  /** Cluster nodes. */
  nodes: Node[];
}
