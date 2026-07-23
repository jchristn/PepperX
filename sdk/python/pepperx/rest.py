"""Typed REST clients for PepperX.

Two clients are provided with the same surface: :class:`PepperXClient` (synchronous) and
:class:`AsyncPepperXClient`. PepperX is unauthenticated by design, so there are no credentials to
configure.
"""

from __future__ import annotations

import base64
import json
from typing import Any, Dict, Iterator, List, Optional
from urllib.parse import quote, urlencode

import httpx

from .errors import PepperXError
from .models import (
    ApiError,
    Container,
    EnumerationQuery,
    EnumerationResult,
    Node,
    ObjectMetadata,
    ObjectReadResult,
    ObjectWriteResult,
    Statistics,
)

_DEFAULT_TIMEOUT = 100.0


def _object_path(container: str, key: str) -> str:
    """Build the single-object path, encoding the key so it may contain any characters."""
    return f"/v1.0/containers/{quote(container, safe='')}/object?key={quote(key, safe='')}"


def _object_metadata_path(container: str, key: str) -> str:
    """Build the object-metadata path."""
    return f"/v1.0/containers/{quote(container, safe='')}/object/metadata?key={quote(key, safe='')}"


def _write_headers(
    content_type: Optional[str],
    labels: Optional[List[str]],
    tags: Optional[Dict[str, str]],
    metadata_object: Optional[Any],
) -> Dict[str, str]:
    """Build the x-pepperx-* headers that carry metadata alongside a raw payload."""
    headers: Dict[str, str] = {}
    headers["Content-Type"] = content_type or "application/octet-stream"
    if labels:
        headers["x-pepperx-labels"] = ",".join(labels)
    if tags:
        headers["x-pepperx-tags"] = urlencode(tags)
    if metadata_object is not None:
        encoded = json.dumps(metadata_object).encode("utf-8")
        headers["x-pepperx-object"] = base64.b64encode(encoded).decode("ascii")
    return headers


def _raise_for_status(response: httpx.Response) -> None:
    """Translate an error response into a :class:`PepperXError`."""
    if response.is_success:
        return

    body = response.text
    error_type = ApiError.INTERNAL_ERROR
    message = f"Request failed with status {response.status_code}."

    if body:
        try:
            payload = json.loads(body)
            raw = payload.get("Error")
            if raw:
                try:
                    error_type = ApiError(raw)
                except ValueError:
                    error_type = ApiError.INTERNAL_ERROR
            if payload.get("Message"):
                message = payload["Message"]
        except (ValueError, AttributeError):
            pass

    raise PepperXError(error_type, response.status_code, message, body)


def _read_result(response: httpx.Response, data: bytes) -> ObjectReadResult:
    """Build a read result from a response and its payload."""
    return ObjectReadResult(
        data=data,
        content_type=response.headers.get("content-type"),
        extent_id=response.headers.get("x-pepperx-extent-id"),
        sha256=response.headers.get("x-pepperx-sha256"),
        has_metadata_object=response.headers.get("x-pepperx-object-available") == "true",
    )


def _containers_page(payload: Dict[str, Any]) -> EnumerationResult[Container]:
    """Build a container page from the server's JSON."""
    return EnumerationResult(
        success=payload.get("Success", True),
        max_results=payload.get("MaxResults", 100),
        continuation_token=payload.get("ContinuationToken"),
        end_of_results=payload.get("EndOfResults", True),
        total_records=payload.get("TotalRecords", 0),
        records_remaining=payload.get("RecordsRemaining", 0),
        objects=[Container.from_wire(c) for c in (payload.get("Objects") or [])],
    )


def _objects_page(payload: Dict[str, Any]) -> EnumerationResult[ObjectMetadata]:
    """Build an object page from the server's JSON."""
    return EnumerationResult(
        success=payload.get("Success", True),
        max_results=payload.get("MaxResults", 100),
        continuation_token=payload.get("ContinuationToken"),
        end_of_results=payload.get("EndOfResults", True),
        total_records=payload.get("TotalRecords", 0),
        records_remaining=payload.get("RecordsRemaining", 0),
        objects=[ObjectMetadata.from_wire(o) for o in (payload.get("Objects") or [])],
    )


class PepperXClient:
    """Synchronous client for the PepperX native REST API.

    Use as a context manager, or call :meth:`close` when finished.
    """

    def __init__(self, base_url: str = "http://localhost:8000", timeout: float = _DEFAULT_TIMEOUT) -> None:
        if not base_url:
            raise ValueError("base_url is required.")
        self.base_url = base_url.rstrip("/")
        self._http = httpx.Client(base_url=self.base_url, timeout=timeout)

    def __enter__(self) -> "PepperXClient":
        return self

    def __exit__(self, *args: Any) -> None:
        self.close()

    def close(self) -> None:
        """Close the underlying HTTP connection pool."""
        self._http.close()

    # ------------------------------------------------------------------ health

    def health(self) -> bool:
        """Return True when the node responds successfully."""
        try:
            return self._http.get("/v1.0/api/health").is_success
        except httpx.HTTPError:
            return False

    # -------------------------------------------------------------- containers

    def create_container(self, name: str, tags: Optional[Dict[str, str]] = None) -> Container:
        """Create a container. Raises :class:`PepperXError` when the name is taken."""
        body: Dict[str, Any] = {"Name": name}
        if tags:
            body["Tags"] = tags
        response = self._http.put("/v1.0/containers", json=body)
        _raise_for_status(response)
        return Container.from_wire(response.json())

    def read_container(self, name: str) -> Optional[Container]:
        """Read a container, or return None when it does not exist."""
        response = self._http.get(f"/v1.0/containers/{quote(name, safe='')}")
        if response.status_code == 404:
            return None
        _raise_for_status(response)
        return Container.from_wire(response.json())

    def container_exists(self, name: str) -> bool:
        """Return True when the container exists."""
        return self._http.head(f"/v1.0/containers/{quote(name, safe='')}").status_code != 404

    def enumerate_containers(self, query: Optional[EnumerationQuery] = None) -> EnumerationResult[Container]:
        """Enumerate containers."""
        response = self._http.post("/v1.0/containers/enumerate", json=(query or EnumerationQuery()).to_wire())
        _raise_for_status(response)
        return _containers_page(response.json())

    def update_container_tags(self, name: str, tags: Dict[str, str]) -> Container:
        """Replace a container's tags."""
        response = self._http.put(f"/v1.0/containers/{quote(name, safe='')}/tags", json=tags or {})
        _raise_for_status(response)
        return Container.from_wire(response.json())

    def delete_container(self, name: str, force: bool = False) -> None:
        """Delete a container, optionally deleting its objects first."""
        path = f"/v1.0/containers/{quote(name, safe='')}"
        if force:
            path += "?force=true"
        _raise_for_status(self._http.delete(path))

    # ----------------------------------------------------------------- objects

    def write_object(
        self,
        container: str,
        key: str,
        data: bytes,
        content_type: Optional[str] = None,
        labels: Optional[List[str]] = None,
        tags: Optional[Dict[str, str]] = None,
        metadata_object: Optional[Any] = None,
        no_overwrite: bool = False,
    ) -> ObjectWriteResult:
        """Write an object. Keys may contain any characters, including slashes."""
        path = _object_path(container, key)
        if no_overwrite:
            path += "&nooverwrite=true"
        response = self._http.put(
            path,
            content=data,
            headers=_write_headers(content_type, labels, tags, metadata_object),
        )
        _raise_for_status(response)
        return ObjectWriteResult.from_wire(response.json())

    def write_object_stream(
        self,
        container: str,
        key: str,
        stream: Iterator[bytes],
        content_type: Optional[str] = None,
        labels: Optional[List[str]] = None,
        tags: Optional[Dict[str, str]] = None,
        metadata_object: Optional[Any] = None,
        no_overwrite: bool = False,
    ) -> ObjectWriteResult:
        """Write an object from an iterator of byte chunks, without buffering it in memory."""
        path = _object_path(container, key)
        if no_overwrite:
            path += "&nooverwrite=true"
        response = self._http.put(
            path,
            content=stream,
            headers=_write_headers(content_type, labels, tags, metadata_object),
        )
        _raise_for_status(response)
        return ObjectWriteResult.from_wire(response.json())

    def read_object(self, container: str, key: str) -> Optional[ObjectReadResult]:
        """Read an object into memory, or return None when it does not exist."""
        response = self._http.get(_object_path(container, key))
        if response.status_code == 404:
            return None
        _raise_for_status(response)
        return _read_result(response, response.content)

    def read_object_metadata(self, container: str, key: str) -> Optional[ObjectMetadata]:
        """Read an object's metadata, or return None when it does not exist."""
        response = self._http.get(_object_metadata_path(container, key))
        if response.status_code == 404:
            return None
        _raise_for_status(response)
        return ObjectMetadata.from_wire(response.json())

    def update_object_metadata(
        self,
        container: str,
        key: str,
        labels: Optional[List[str]] = None,
        tags: Optional[Dict[str, str]] = None,
        metadata_object: Optional[Any] = None,
        clear_object: bool = False,
    ) -> ObjectWriteResult:
        """Update an object's metadata, preserving its payload."""
        body: Dict[str, Any] = {"ClearObject": clear_object}
        if labels is not None:
            body["Labels"] = labels
        if tags is not None:
            body["Tags"] = tags
        if metadata_object is not None:
            body["Object"] = metadata_object
        response = self._http.put(_object_metadata_path(container, key), json=body)
        _raise_for_status(response)
        return ObjectWriteResult.from_wire(response.json())

    def object_exists(self, container: str, key: str) -> bool:
        """Return True when the object exists."""
        return self._http.head(_object_path(container, key)).status_code != 404

    def delete_object(self, container: str, key: str) -> bool:
        """Delete an object. Returns False when it did not exist."""
        response = self._http.delete(_object_path(container, key))
        if response.status_code == 404:
            return False
        _raise_for_status(response)
        return True

    def enumerate_objects(
        self, container: str, query: Optional[EnumerationQuery] = None
    ) -> EnumerationResult[ObjectMetadata]:
        """Enumerate or search objects within a container."""
        response = self._http.post(
            f"/v1.0/containers/{quote(container, safe='')}/objects/enumerate",
            json=(query or EnumerationQuery()).to_wire(),
        )
        _raise_for_status(response)
        return _objects_page(response.json())

    def search(self, query: Optional[EnumerationQuery] = None) -> EnumerationResult[ObjectMetadata]:
        """Search objects across containers."""
        response = self._http.post("/v1.0/objects/enumerate", json=(query or EnumerationQuery()).to_wire())
        _raise_for_status(response)
        return _objects_page(response.json())

    # ------------------------------------------------------------------- admin

    def statistics(self) -> Statistics:
        """Read aggregate statistics."""
        response = self._http.get("/v1.0/admin/stats")
        _raise_for_status(response)
        return Statistics.from_wire(response.json())

    def nodes(self) -> List[Node]:
        """List cluster nodes."""
        response = self._http.get("/v1.0/admin/nodes")
        _raise_for_status(response)
        return [Node.from_wire(n) for n in response.json()]


class AsyncPepperXClient:
    """Asynchronous client for the PepperX native REST API.

    Mirrors :class:`PepperXClient`. Use as an async context manager, or await :meth:`close`.
    """

    def __init__(self, base_url: str = "http://localhost:8000", timeout: float = _DEFAULT_TIMEOUT) -> None:
        if not base_url:
            raise ValueError("base_url is required.")
        self.base_url = base_url.rstrip("/")
        self._http = httpx.AsyncClient(base_url=self.base_url, timeout=timeout)

    async def __aenter__(self) -> "AsyncPepperXClient":
        return self

    async def __aexit__(self, *args: Any) -> None:
        await self.close()

    async def close(self) -> None:
        """Close the underlying HTTP connection pool."""
        await self._http.aclose()

    async def health(self) -> bool:
        """Return True when the node responds successfully."""
        try:
            response = await self._http.get("/v1.0/api/health")
            return response.is_success
        except httpx.HTTPError:
            return False

    async def create_container(self, name: str, tags: Optional[Dict[str, str]] = None) -> Container:
        """Create a container."""
        body: Dict[str, Any] = {"Name": name}
        if tags:
            body["Tags"] = tags
        response = await self._http.put("/v1.0/containers", json=body)
        _raise_for_status(response)
        return Container.from_wire(response.json())

    async def read_container(self, name: str) -> Optional[Container]:
        """Read a container, or return None when it does not exist."""
        response = await self._http.get(f"/v1.0/containers/{quote(name, safe='')}")
        if response.status_code == 404:
            return None
        _raise_for_status(response)
        return Container.from_wire(response.json())

    async def delete_container(self, name: str, force: bool = False) -> None:
        """Delete a container, optionally deleting its objects first."""
        path = f"/v1.0/containers/{quote(name, safe='')}"
        if force:
            path += "?force=true"
        _raise_for_status(await self._http.delete(path))

    async def write_object(
        self,
        container: str,
        key: str,
        data: bytes,
        content_type: Optional[str] = None,
        labels: Optional[List[str]] = None,
        tags: Optional[Dict[str, str]] = None,
        metadata_object: Optional[Any] = None,
        no_overwrite: bool = False,
    ) -> ObjectWriteResult:
        """Write an object."""
        path = _object_path(container, key)
        if no_overwrite:
            path += "&nooverwrite=true"
        response = await self._http.put(
            path,
            content=data,
            headers=_write_headers(content_type, labels, tags, metadata_object),
        )
        _raise_for_status(response)
        return ObjectWriteResult.from_wire(response.json())

    async def read_object(self, container: str, key: str) -> Optional[ObjectReadResult]:
        """Read an object into memory, or return None when it does not exist."""
        response = await self._http.get(_object_path(container, key))
        if response.status_code == 404:
            return None
        _raise_for_status(response)
        return _read_result(response, response.content)

    async def read_object_metadata(self, container: str, key: str) -> Optional[ObjectMetadata]:
        """Read an object's metadata, or return None when it does not exist."""
        response = await self._http.get(_object_metadata_path(container, key))
        if response.status_code == 404:
            return None
        _raise_for_status(response)
        return ObjectMetadata.from_wire(response.json())

    async def delete_object(self, container: str, key: str) -> bool:
        """Delete an object. Returns False when it did not exist."""
        response = await self._http.delete(_object_path(container, key))
        if response.status_code == 404:
            return False
        _raise_for_status(response)
        return True

    async def enumerate_objects(
        self, container: str, query: Optional[EnumerationQuery] = None
    ) -> EnumerationResult[ObjectMetadata]:
        """Enumerate or search objects within a container."""
        response = await self._http.post(
            f"/v1.0/containers/{quote(container, safe='')}/objects/enumerate",
            json=(query or EnumerationQuery()).to_wire(),
        )
        _raise_for_status(response)
        return _objects_page(response.json())

    async def search(self, query: Optional[EnumerationQuery] = None) -> EnumerationResult[ObjectMetadata]:
        """Search objects across containers."""
        response = await self._http.post("/v1.0/objects/enumerate", json=(query or EnumerationQuery()).to_wire())
        _raise_for_status(response)
        return _objects_page(response.json())

    async def statistics(self) -> Statistics:
        """Read aggregate statistics."""
        response = await self._http.get("/v1.0/admin/stats")
        _raise_for_status(response)
        return Statistics.from_wire(response.json())
