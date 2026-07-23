"""Tests for the PepperX Python SDK.

These exercise a live node. Point them at one with ``PEPPERX_URL`` and ``PEPPERX_WS_URL``; when no
node is reachable the tests skip rather than fail.
"""

from __future__ import annotations

import os
import uuid
from typing import Iterator

import pytest

from pepperx import (
    ApiError,
    AsyncPepperXClient,
    AsyncPepperXWebsocketClient,
    EnumerationQuery,
    PepperXClient,
    PepperXError,
)

REST_URL = os.environ.get("PEPPERX_URL", "http://127.0.0.1:8000")
WS_URL = os.environ.get("PEPPERX_WS_URL", "ws://127.0.0.1:8002/")


def _node_available() -> bool:
    """Return True when a PepperX node answers a health check."""
    try:
        with PepperXClient(REST_URL, timeout=3.0) as client:
            return client.health()
    except Exception:
        return False


requires_node = pytest.mark.skipif(not _node_available(), reason=f"No PepperX node at {REST_URL}")


def _name() -> str:
    """Generate a unique, valid container name."""
    return "py" + uuid.uuid4().hex[:18]


@pytest.fixture
def client() -> Iterator[PepperXClient]:
    """A synchronous client bound to the node under test."""
    with PepperXClient(REST_URL) as instance:
        yield instance


@pytest.fixture
def container(client: PepperXClient) -> Iterator[str]:
    """A container that is created for the test and removed afterward."""
    name = _name()
    client.create_container(name)
    try:
        yield name
    finally:
        client.delete_container(name, force=True)


@requires_node
def test_health(client: PepperXClient) -> None:
    """The node reports healthy."""
    assert client.health() is True


@requires_node
def test_container_lifecycle(client: PepperXClient) -> None:
    """Containers can be created, read, tagged, enumerated, and deleted."""
    name = _name()
    created = client.create_container(name, tags={"team": "python"})
    assert created.name == name
    assert created.tags["team"] == "python"

    assert client.container_exists(name) is True
    read = client.read_container(name)
    assert read is not None and read.id == created.id

    updated = client.update_container_tags(name, {"tier": "gold"})
    assert updated.tags == {"tier": "gold"}

    page = client.enumerate_containers(EnumerationQuery(max_results=1000))
    assert page.total_records >= 1

    client.delete_container(name)
    assert client.container_exists(name) is False
    assert client.read_container(name) is None


@requires_node
def test_object_lifecycle(client: PepperXClient, container: str) -> None:
    """Objects round-trip with their payload, labels, tags, and metadata object."""
    written = client.write_object(
        container,
        "a/b/c.txt",
        b"python-payload",
        content_type="text/plain",
        labels=["sdk", "python"],
        tags={"lang": "python"},
        metadata_object={"nested": {"value": 1}},
    )
    assert written.extent_id
    assert written.size_bytes == len(b"python-payload")

    read = client.read_object(container, "a/b/c.txt")
    assert read is not None
    assert read.data == b"python-payload"
    assert read.sha256 == written.sha256

    metadata = client.read_object_metadata(container, "a/b/c.txt")
    assert metadata is not None
    assert sorted(metadata.labels) == ["python", "sdk"]
    assert metadata.tags["lang"] == "python"
    assert metadata.metadata_object == {"nested": {"value": 1}}

    assert client.object_exists(container, "a/b/c.txt") is True
    assert client.delete_object(container, "a/b/c.txt") is True
    assert client.object_exists(container, "a/b/c.txt") is False
    assert client.read_object(container, "a/b/c.txt") is None


@requires_node
def test_binary_payload(client: PepperXClient, container: str) -> None:
    """Binary payloads survive a round trip byte for byte."""
    payload = bytes(range(256)) * 64
    client.write_object(container, "binary.bin", payload)
    read = client.read_object(container, "binary.bin")
    assert read is not None and read.data == payload


@requires_node
def test_streaming_write(client: PepperXClient, container: str) -> None:
    """A chunked iterator can be streamed as an object payload."""
    chunks = [bytes([i % 251]) * 4096 for i in range(16)]
    expected = b"".join(chunks)

    client.write_object_stream(container, "streamed.bin", iter(chunks))
    read = client.read_object(container, "streamed.bin")
    assert read is not None and read.data == expected


@requires_node
def test_metadata_update(client: PepperXClient, container: str) -> None:
    """Updating metadata preserves the payload."""
    client.write_object(container, "k", b"keep-me", labels=["before"])
    client.update_object_metadata(container, "k", labels=["after"], tags={"t": "v"})

    metadata = client.read_object_metadata(container, "k")
    assert metadata is not None
    assert metadata.labels == ["after"]
    assert metadata.tags == {"t": "v"}

    read = client.read_object(container, "k")
    assert read is not None and read.data == b"keep-me"


@requires_node
def test_label_and_tag_search(client: PepperXClient, container: str) -> None:
    """Label and tag filters use AND semantics."""
    client.write_object(container, "match", b"a", labels=["red", "blue"], tags={"tier": "gold"})
    client.write_object(container, "partial", b"b", labels=["red"], tags={"tier": "gold"})
    client.write_object(container, "other", b"c", labels=["red", "blue"], tags={"tier": "silver"})

    found = client.enumerate_objects(
        container, EnumerationQuery(labels=["red", "blue"], tags={"tier": "gold"})
    )
    assert found.total_records == 1
    assert found.objects[0].key == "match"


@requires_node
def test_no_overwrite_conflict(client: PepperXClient, container: str) -> None:
    """A no-overwrite write against an existing key raises a typed conflict."""
    client.write_object(container, "once", b"first")

    with pytest.raises(PepperXError) as raised:
        client.write_object(container, "once", b"second", no_overwrite=True)

    assert raised.value.error_type == ApiError.CONFLICT
    assert raised.value.status_code == 409


@requires_node
def test_duplicate_container_conflict(client: PepperXClient, container: str) -> None:
    """Creating an existing container raises a typed conflict."""
    with pytest.raises(PepperXError) as raised:
        client.create_container(container)

    assert raised.value.error_type == ApiError.CONFLICT


@requires_node
def test_statistics_and_nodes(client: PepperXClient) -> None:
    """Statistics and node listings return typed objects."""
    stats = client.statistics()
    assert stats.container_count >= 0
    assert len(client.nodes()) >= 1


@requires_node
@pytest.mark.asyncio
async def test_async_client() -> None:
    """The async client mirrors the synchronous surface."""
    async with AsyncPepperXClient(REST_URL) as client:
        assert await client.health() is True

        name = _name()
        await client.create_container(name)
        try:
            await client.write_object(name, "async-key", b"async-payload", content_type="text/plain")
            read = await client.read_object(name, "async-key")
            assert read is not None and read.data == b"async-payload"

            page = await client.enumerate_objects(name)
            assert page.total_records == 1
        finally:
            await client.delete_container(name, force=True)


@requires_node
@pytest.mark.asyncio
async def test_websocket_client() -> None:
    """The WebSocket client round-trips objects over one connection."""
    async with AsyncPepperXWebsocketClient(WS_URL) as client:
        assert await client.health() is True

        name = _name()
        await client.create_container(name)
        try:
            await client.write_object(name, "ws-key", b"ws-payload", content_type="text/plain", labels=["ws"])
            assert await client.read_object(name, "ws-key") == b"ws-payload"

            metadata = await client.read_object_metadata(name, "ws-key")
            assert metadata is not None and metadata.labels == ["ws"]

            assert await client.delete_object(name, "ws-key") is True
            assert await client.read_object(name, "ws-key") is None
        finally:
            await client.delete_container(name)


@requires_node
@pytest.mark.asyncio
async def test_websocket_concurrency() -> None:
    """Concurrent operations on one WebSocket connection correlate correctly."""
    import asyncio

    async with AsyncPepperXWebsocketClient(WS_URL) as client:
        results = await asyncio.gather(*[client.health() for _ in range(16)])
        assert all(results)
