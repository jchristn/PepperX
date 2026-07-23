"""A narrated walkthrough of the PepperX Python SDK against a live node.

Point it at a node with ``PEPPERX_URL`` and ``PEPPERX_WS_URL``:

    python examples/console.py
"""

from __future__ import annotations

import asyncio
import os
import uuid

from pepperx import (
    AsyncPepperXWebsocketClient,
    EnumerationQuery,
    PepperXClient,
    PepperXError,
)

REST_URL = os.environ.get("PEPPERX_URL", "http://127.0.0.1:8000")
WS_URL = os.environ.get("PEPPERX_WS_URL", "ws://127.0.0.1:8002/")


def step(message: str) -> None:
    """Print a step heading."""
    print(f"> {message}")


def detail(message: str) -> None:
    """Print an indented detail line."""
    print(f"    {message}")


def rest_walkthrough(container: str) -> None:
    """Exercise the synchronous REST client."""
    with PepperXClient(REST_URL) as client:
        step("Checking node health")
        if not client.health():
            raise RuntimeError(f"No healthy PepperX node at {REST_URL}")
        detail("healthy")

        step(f"Creating container '{container}'")
        created = client.create_container(container, tags={"demo": "python"})
        detail(f"id {created.id}")

        step("Writing an object with labels, tags, and a metadata object")
        written = client.write_object(
            container,
            "reports/2026/summary.txt",
            b"PepperX stores this payload verbatim.",
            content_type="text/plain",
            labels=["report", "annual"],
            tags={"year": "2026", "team": "platform"},
            metadata_object={"author": "demo", "revision": 3},
        )
        detail(f"extent {written.extent_id}, {written.size_bytes} bytes, sha256 {written.sha256[:12]}...")

        step("Reading the payload back")
        read = client.read_object(container, "reports/2026/summary.txt")
        assert read is not None
        detail(f'"{read.data.decode()}"')

        step("Reading full metadata")
        metadata = client.read_object_metadata(container, "reports/2026/summary.txt")
        assert metadata is not None
        detail(
            f"labels {metadata.labels}, {len(metadata.tags)} tags, "
            f"metadata object present: {metadata.metadata_object is not None}"
        )

        step("Searching by label and tag")
        found = client.enumerate_objects(
            container, EnumerationQuery(labels=["report"], tags={"year": "2026"})
        )
        detail(f"{found.total_records} match(es)")

        step("Reading statistics")
        stats = client.statistics()
        detail(f"{stats.container_count} containers, {stats.object_count} objects, {stats.total_bytes} bytes")

        step("Deleting the container and its contents")
        client.delete_container(container, force=True)
        detail("deleted")


async def websocket_walkthrough(container: str) -> None:
    """Exercise the WebSocket client."""
    print()
    async with AsyncPepperXWebsocketClient(WS_URL) as client:
        step("Connected over WebSockets")
        detail("connected")

        step(f"Creating container '{container}'")
        await client.create_container(container)
        detail("created")

        step("Writing and reading an object")
        await client.write_object(container, "ws-key", b"delivered over a persistent connection", content_type="text/plain")
        payload = await client.read_object(container, "ws-key")
        assert payload is not None
        detail(f'"{payload.decode()}"')

        step("Issuing 8 concurrent operations on one connection")
        results = await asyncio.gather(*[client.health() for _ in range(8)])
        detail(f"all {len(results)} correlated correctly")

        step("Cleaning up")
        await client.delete_object(container, "ws-key")
        await client.delete_container(container)
        detail("deleted")


def main() -> int:
    """Run both walkthroughs."""
    suffix = uuid.uuid4().hex[:12]
    print("PepperX Python SDK walkthrough")
    print(f"  REST      : {REST_URL}")
    print(f"  WebSockets: {WS_URL}")
    print()

    try:
        rest_walkthrough(f"pydemo{suffix}")
        asyncio.run(websocket_walkthrough(f"pydemows{suffix}"))
        print()
        print("Walkthrough complete.")
        return 0
    except PepperXError as exc:
        print()
        print(f"PepperX returned an error: {exc}")
        return 1
    except Exception as exc:  # noqa: BLE001 - a demo should explain any failure plainly
        print()
        print(f"Failed: {exc}")
        print("Is a PepperX node running? Set PEPPERX_URL to point at one.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
