"""Typed models mirroring the PepperX wire contracts."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any, Dict, Generic, List, Optional, TypeVar

T = TypeVar("T")


class ApiError(str, Enum):
    """Machine-readable error classification returned by the server."""

    BAD_REQUEST = "BadRequest"
    NOT_FOUND = "NotFound"
    CONFLICT = "Conflict"
    NOT_EMPTY = "NotEmpty"
    TOO_LARGE = "TooLarge"
    DELETING = "Deleting"
    NOT_IMPLEMENTED = "NotImplemented"
    INTERNAL_ERROR = "InternalError"


class EnumerationOrder(str, Enum):
    """Sort ordering for an enumeration result set."""

    CREATED_ASCENDING = "CreatedAscending"
    CREATED_DESCENDING = "CreatedDescending"
    KEY_ASCENDING = "KeyAscending"
    KEY_DESCENDING = "KeyDescending"


def _parse_datetime(value: Optional[str]) -> datetime:
    """Parse an ISO-8601 timestamp from the server, tolerating a trailing Z."""
    if not value:
        return datetime.min
    text = value.replace("Z", "+00:00")
    try:
        return datetime.fromisoformat(text)
    except ValueError:
        return datetime.min


@dataclass
class EnumerationQuery:
    """Query parameters for paginated, filtered enumeration of containers or objects.

    ``labels`` and ``tags`` both use AND semantics: every label listed must be present, and every
    tag key must be present with the given value.
    """

    max_results: int = 100
    skip: int = 0
    continuation_token: Optional[str] = None
    ordering: EnumerationOrder = EnumerationOrder.CREATED_DESCENDING
    prefix: Optional[str] = None
    suffix: Optional[str] = None
    created_after_utc: Optional[datetime] = None
    created_before_utc: Optional[datetime] = None
    labels: Optional[List[str]] = None
    tags: Optional[Dict[str, str]] = None
    case_insensitive: bool = False
    containers: Optional[List[str]] = None

    def to_wire(self) -> Dict[str, Any]:
        """Serialize to the JSON shape the server expects."""
        body: Dict[str, Any] = {
            "MaxResults": max(1, min(self.max_results, 1000)),
            "Skip": self.skip,
            "Ordering": self.ordering.value,
            "CaseInsensitive": self.case_insensitive,
        }
        if self.continuation_token is not None:
            body["ContinuationToken"] = self.continuation_token
        if self.prefix is not None:
            body["Prefix"] = self.prefix
        if self.suffix is not None:
            body["Suffix"] = self.suffix
        if self.created_after_utc is not None:
            body["CreatedAfterUtc"] = self.created_after_utc.isoformat()
        if self.created_before_utc is not None:
            body["CreatedBeforeUtc"] = self.created_before_utc.isoformat()
        if self.labels:
            body["Labels"] = self.labels
        if self.tags:
            body["Tags"] = self.tags
        if self.containers:
            body["Containers"] = self.containers
        return body


@dataclass
class Container:
    """A container: the top-level scope that holds objects."""

    id: str = ""
    name: str = ""
    tags: Dict[str, str] = field(default_factory=dict)
    object_count: int = 0
    total_bytes: int = 0
    created_utc: datetime = field(default_factory=lambda: datetime.min)
    last_update_utc: datetime = field(default_factory=lambda: datetime.min)

    @staticmethod
    def from_wire(data: Dict[str, Any]) -> "Container":
        """Build from the server's JSON representation."""
        return Container(
            id=data.get("Id", ""),
            name=data.get("Name", ""),
            tags=data.get("Tags") or {},
            object_count=data.get("ObjectCount", 0),
            total_bytes=data.get("TotalBytes", 0),
            created_utc=_parse_datetime(data.get("CreatedUtc")),
            last_update_utc=_parse_datetime(data.get("LastUpdateUtc")),
        )


@dataclass
class ObjectMetadata:
    """Metadata for a stored object.

    ``metadata_object`` holds the freeform JSON attached to the object. Listings omit it for
    performance; read an object's metadata directly to retrieve it.
    """

    key: str = ""
    extent_id: str = ""
    container_id: str = ""
    container_name: Optional[str] = None
    size_bytes: int = 0
    sha256: str = ""
    content_type: Optional[str] = None
    labels: List[str] = field(default_factory=list)
    tags: Dict[str, str] = field(default_factory=dict)
    metadata_object: Optional[Any] = None
    has_metadata_object: bool = False
    created_utc: datetime = field(default_factory=lambda: datetime.min)

    @staticmethod
    def from_wire(data: Dict[str, Any]) -> "ObjectMetadata":
        """Build from the server's JSON representation."""
        return ObjectMetadata(
            key=data.get("Key", ""),
            extent_id=data.get("ExtentId", ""),
            container_id=data.get("ContainerId", ""),
            container_name=data.get("ContainerName"),
            size_bytes=data.get("SizeBytes", 0),
            sha256=data.get("Sha256", ""),
            content_type=data.get("ContentType"),
            labels=data.get("Labels") or [],
            tags=data.get("Tags") or {},
            metadata_object=data.get("Object"),
            has_metadata_object=data.get("HasMetadataObject", False),
            created_utc=_parse_datetime(data.get("CreatedUtc")),
        )


@dataclass
class ObjectWriteResult:
    """The result of writing (creating or replacing) an object."""

    extent_id: str = ""
    key: str = ""
    container_id: str = ""
    size_bytes: int = 0
    sha256: str = ""
    content_type: Optional[str] = None
    replaced: bool = False

    @staticmethod
    def from_wire(data: Dict[str, Any]) -> "ObjectWriteResult":
        """Build from the server's JSON representation."""
        return ObjectWriteResult(
            extent_id=data.get("ExtentId", ""),
            key=data.get("Key", ""),
            container_id=data.get("ContainerId", ""),
            size_bytes=data.get("SizeBytes", 0),
            sha256=data.get("Sha256", ""),
            content_type=data.get("ContentType"),
            replaced=data.get("Replaced", False),
        )


@dataclass
class ObjectReadResult:
    """An object's payload plus the identifying headers the server returned."""

    data: bytes = b""
    content_type: Optional[str] = None
    extent_id: Optional[str] = None
    sha256: Optional[str] = None
    has_metadata_object: bool = False


@dataclass
class EnumerationResult(Generic[T]):
    """A page of enumeration results."""

    success: bool = True
    max_results: int = 100
    continuation_token: Optional[str] = None
    end_of_results: bool = True
    total_records: int = 0
    records_remaining: int = 0
    objects: List[T] = field(default_factory=list)


@dataclass
class ContainerStatistics:
    """Per-container rollup within a statistics response."""

    id: str = ""
    name: str = ""
    object_count: int = 0
    total_bytes: int = 0


@dataclass
class Node:
    """A cluster node and its liveness."""

    id: str = ""
    hostname: Optional[str] = None
    heartbeat_age_seconds: float = 0.0
    is_alive: bool = True

    @staticmethod
    def from_wire(data: Dict[str, Any]) -> "Node":
        """Build from the server's JSON representation."""
        return Node(
            id=data.get("Id", ""),
            hostname=data.get("Hostname"),
            heartbeat_age_seconds=data.get("HeartbeatAgeSeconds", 0.0),
            is_alive=data.get("IsAlive", True),
        )


@dataclass
class Statistics:
    """Aggregate statistics across containers, storage, database, and cluster nodes."""

    container_count: int = 0
    object_count: int = 0
    total_bytes: int = 0
    containers: List[ContainerStatistics] = field(default_factory=list)
    storage_total_bytes: int = 0
    storage_free_bytes: int = 0
    database_size_bytes: int = 0
    nodes: List[Node] = field(default_factory=list)

    @staticmethod
    def from_wire(data: Dict[str, Any]) -> "Statistics":
        """Build from the server's JSON representation."""
        return Statistics(
            container_count=data.get("ContainerCount", 0),
            object_count=data.get("ObjectCount", 0),
            total_bytes=data.get("TotalBytes", 0),
            containers=[
                ContainerStatistics(
                    id=c.get("Id", ""),
                    name=c.get("Name", ""),
                    object_count=c.get("ObjectCount", 0),
                    total_bytes=c.get("TotalBytes", 0),
                )
                for c in (data.get("Containers") or [])
            ],
            storage_total_bytes=data.get("StorageTotalBytes", 0),
            storage_free_bytes=data.get("StorageFreeBytes", 0),
            database_size_bytes=data.get("DatabaseSizeBytes", 0),
            nodes=[Node.from_wire(n) for n in (data.get("Nodes") or [])],
        )
