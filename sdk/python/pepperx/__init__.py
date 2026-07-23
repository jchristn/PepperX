"""PepperX client SDK.

A typed Python client for PepperX, a high-performance key-value store with rich metadata.
PepperX is unauthenticated by design, so no credentials are required.

Example:
    >>> from pepperx import PepperXClient
    >>> with PepperXClient("http://localhost:8000") as client:
    ...     client.create_container("photos")
    ...     client.write_object("photos", "2026/07/cat.jpg", b"...", labels=["animal"])
"""

from .errors import PepperXError
from .models import (
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
)
from .rest import AsyncPepperXClient, PepperXClient
from .websocket import AsyncPepperXWebsocketClient

__version__ = "1.0.0"

__all__ = [
    "ApiError",
    "AsyncPepperXClient",
    "AsyncPepperXWebsocketClient",
    "Container",
    "ContainerStatistics",
    "EnumerationOrder",
    "EnumerationQuery",
    "EnumerationResult",
    "Node",
    "ObjectMetadata",
    "ObjectReadResult",
    "ObjectWriteResult",
    "PepperXClient",
    "PepperXError",
    "Statistics",
    "__version__",
]
