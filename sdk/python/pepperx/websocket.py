"""Typed WebSocket client for PepperX.

Operations are correlated to responses by identifier, so many may be in flight on one connection.
"""

from __future__ import annotations

import asyncio
import base64
import json
import uuid
from typing import Any, Dict, List, Optional

import websockets

from .errors import PepperXError
from .models import (
    ApiError,
    Container,
    EnumerationQuery,
    EnumerationResult,
    ObjectMetadata,
    ObjectWriteResult,
    Statistics,
)

_DEFAULT_TIMEOUT = 100.0


class AsyncPepperXWebsocketClient:
    """Asynchronous client for the PepperX WebSocket surface.

    Call :meth:`connect` before issuing operations, then :meth:`close` when finished, or use the
    client as an async context manager.
    """

    def __init__(self, url: str = "ws://localhost:8002/", timeout: float = _DEFAULT_TIMEOUT) -> None:
        if not url:
            raise ValueError("url is required.")
        self.url = url
        self.timeout = timeout
        self._socket: Optional[Any] = None
        self._pending: Dict[str, asyncio.Future] = {}
        self._reader: Optional[asyncio.Task] = None

    async def __aenter__(self) -> "AsyncPepperXWebsocketClient":
        await self.connect()
        return self

    async def __aexit__(self, *args: Any) -> None:
        await self.close()

    @property
    def is_connected(self) -> bool:
        """Whether the client currently holds an open connection."""
        return self._socket is not None

    async def connect(self) -> None:
        """Connect to the server and start the receive loop."""
        if self._socket is not None:
            return
        self._socket = await websockets.connect(self.url, max_size=None)
        self._reader = asyncio.create_task(self._receive_loop())

    async def close(self) -> None:
        """Close the connection and fail any in-flight operations."""
        socket, self._socket = self._socket, None

        if self._reader is not None:
            self._reader.cancel()
            try:
                await self._reader
            except (asyncio.CancelledError, Exception):
                pass
            self._reader = None

        if socket is not None:
            await socket.close()

        for future in self._pending.values():
            if not future.done():
                future.set_exception(
                    PepperXError(ApiError.INTERNAL_ERROR, 499, "The client closed before a response arrived.")
                )
        self._pending.clear()

    # ------------------------------------------------------------- operations

    async def health(self) -> bool:
        """Return True when the node responds successfully."""
        response = await self._send("Health")
        return bool(response.get("Success"))

    async def create_container(self, name: str, tags: Optional[Dict[str, str]] = None) -> Container:
        """Create a container."""
        body: Dict[str, Any] = {"Name": name}
        if tags:
            body["Tags"] = tags
        response = await self._send("ContainerCreate", body=body)
        return Container.from_wire(self._require(response))

    async def read_container(self, name: str) -> Optional[Container]:
        """Read a container, or return None when it does not exist."""
        response = await self._send("ContainerRead", container=name)
        if response.get("StatusCode") == 404:
            return None
        return Container.from_wire(self._require(response))

    async def delete_container(self, name: str) -> None:
        """Delete an empty container."""
        self._raise(await self._send("ContainerDelete", container=name))

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
        body: Dict[str, Any] = {
            "DataBase64": base64.b64encode(data).decode("ascii"),
            "NoOverwrite": no_overwrite,
        }
        if content_type is not None:
            body["ContentType"] = content_type
        if labels:
            body["Labels"] = labels
        if tags:
            body["Tags"] = tags
        if metadata_object is not None:
            body["Object"] = metadata_object

        response = await self._send("ObjectWrite", container=container, key=key, body=body)
        return ObjectWriteResult.from_wire(self._require(response))

    async def read_object(self, container: str, key: str) -> Optional[bytes]:
        """Read an object's payload, or return None when it does not exist."""
        response = await self._send("ObjectRead", container=container, key=key)
        if response.get("StatusCode") == 404:
            return None
        self._raise(response)
        payload = response.get("DataBase64")
        return base64.b64decode(payload) if payload else b""

    async def read_object_metadata(self, container: str, key: str) -> Optional[ObjectMetadata]:
        """Read an object's metadata, or return None when it does not exist."""
        response = await self._send("ObjectReadMetadata", container=container, key=key)
        if response.get("StatusCode") == 404:
            return None
        return ObjectMetadata.from_wire(self._require(response))

    async def delete_object(self, container: str, key: str) -> bool:
        """Delete an object. Returns False when it did not exist."""
        response = await self._send("ObjectDelete", container=container, key=key)
        if response.get("StatusCode") == 404:
            return False
        self._raise(response)
        return True

    async def enumerate_objects(
        self, container: str, query: Optional[EnumerationQuery] = None
    ) -> EnumerationResult[ObjectMetadata]:
        """Enumerate or search objects within a container."""
        response = await self._send(
            "ObjectEnumerate", container=container, body=(query or EnumerationQuery()).to_wire()
        )
        payload = self._require(response)
        return EnumerationResult(
            success=payload.get("Success", True),
            max_results=payload.get("MaxResults", 100),
            continuation_token=payload.get("ContinuationToken"),
            end_of_results=payload.get("EndOfResults", True),
            total_records=payload.get("TotalRecords", 0),
            records_remaining=payload.get("RecordsRemaining", 0),
            objects=[ObjectMetadata.from_wire(o) for o in (payload.get("Objects") or [])],
        )

    async def statistics(self) -> Statistics:
        """Read aggregate statistics."""
        response = await self._send("AdminStats")
        return Statistics.from_wire(self._require(response))

    # ---------------------------------------------------------------- internal

    async def _send(
        self,
        operation: str,
        container: Optional[str] = None,
        key: Optional[str] = None,
        body: Optional[Any] = None,
    ) -> Dict[str, Any]:
        """Send one envelope and await its correlated response."""
        if self._socket is None:
            raise RuntimeError("The client is not connected; call connect() first.")

        request_id = uuid.uuid4().hex
        envelope: Dict[str, Any] = {"RequestId": request_id, "Operation": operation}
        if container is not None:
            envelope["Container"] = container
        if key is not None:
            envelope["Key"] = key
        if body is not None:
            envelope["Body"] = body

        loop = asyncio.get_running_loop()
        future: asyncio.Future = loop.create_future()
        self._pending[request_id] = future

        try:
            await self._socket.send(json.dumps(envelope))
            return await asyncio.wait_for(future, timeout=self.timeout)
        except asyncio.TimeoutError as exc:
            raise PepperXError(
                ApiError.INTERNAL_ERROR, 504, "Timed out waiting for a WebSocket response."
            ) from exc
        finally:
            self._pending.pop(request_id, None)

    async def _receive_loop(self) -> None:
        """Read responses and complete their pending futures."""
        try:
            while self._socket is not None:
                message = await self._socket.recv()
                envelope = json.loads(message)
                request_id = envelope.get("RequestId")
                if request_id and request_id in self._pending:
                    future = self._pending[request_id]
                    if not future.done():
                        future.set_result(envelope)
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            for future in self._pending.values():
                if not future.done():
                    future.set_exception(
                        PepperXError(ApiError.INTERNAL_ERROR, 500, f"The WebSocket connection failed: {exc}")
                    )
            self._pending.clear()

    @staticmethod
    def _raise(response: Dict[str, Any]) -> None:
        """Raise when the server reported a failure."""
        if response.get("Success"):
            return

        error = response.get("Error") or {}
        raw = error.get("Error")
        try:
            error_type = ApiError(raw) if raw else ApiError.INTERNAL_ERROR
        except ValueError:
            error_type = ApiError.INTERNAL_ERROR

        status = response.get("StatusCode", 500)
        message = error.get("Message") or f"The operation failed with status {status}."
        raise PepperXError(error_type, status, message)

    @classmethod
    def _require(cls, response: Dict[str, Any]) -> Dict[str, Any]:
        """Raise on failure, then return the result payload."""
        cls._raise(response)
        result = response.get("Result")
        if result is None:
            raise PepperXError(
                ApiError.INTERNAL_ERROR, response.get("StatusCode", 500), "The server returned no result payload."
            )
        return result
