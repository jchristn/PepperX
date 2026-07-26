#!/usr/bin/env python3
"""Seed a PepperX node with sample data.

Creates a handful of containers and objects that exercise all three metadata forms -- labels, tags,
and a freeform JSON object -- then issues enough traffic that the dashboard's charts and request
history have something to show. A freshly reset node with an empty Home view tells you nothing about
whether the deployment works.

Uses only the standard library so it runs anywhere Python does, with no install step.

    python3 seed.py [base-url]
"""

import base64
import http.client
import json
import random
import sys
import urllib.parse

# The server enables per-container caching by default when no cache block is supplied. The seed sends the
# defaults explicitly so a fresh stack is self-documenting -- the dashboard's cache view shows real
# settings rather than leaving you to guess what "default" means -- and so one container can demonstrate a
# non-default policy.
CACHE_DEFAULT = {
    "Enabled": True,
    "Policy": "LRU",
    "MaxObjects": 1000,
    "MaxMemoryBytes": 268435456,   # 256 MiB
    "EvictCount": 10,
    "MaxCacheableObjectBytes": 1048576,   # 1 MiB
}

CACHE_FIFO = dict(CACHE_DEFAULT, Policy="FIFO", MaxObjects=500)

CONTAINERS = [
    ("telemetry", {"team": "platform", "env": "prod"}, CACHE_DEFAULT),
    ("documents", {"team": "legal", "retention": "7y"}, CACHE_DEFAULT),
    ("images", {"env": "staging"}, CACHE_FIFO),
    ("backups", {}, CACHE_DEFAULT),
]

OBJECTS = [
    ("telemetry", "metrics/2026/07/cpu.json", "application/json", ["metric", "cpu"], {"resolution": "1m"}),
    ("telemetry", "metrics/2026/07/memory.json", "application/json", ["metric", "memory"], {"resolution": "1m"}),
    ("telemetry", "traces/2026-07-20.ndjson", "application/x-ndjson", ["trace"], {"sampled": "true"}),
    ("documents", "contracts/2026/acme-msa.pdf", "application/pdf", ["contract", "signed"], {"counterparty": "acme"}),
    ("documents", "policies/retention.md", "text/markdown", ["policy"], {"owner": "legal"}),
    ("images", "brand/logo.svg", "image/svg+xml", ["asset", "brand"], {"variant": "primary"}),
    ("images", "marketing/hero-2026.jpg", "image/jpeg", ["asset", "marketing"], {"campaign": "spring"}),
    ("backups", "postgres/2026-07-22-full.dump", "application/octet-stream", ["backup", "database"], {"kind": "full"}),
]


class Client:
    """Minimal REST client over one keep-alive connection."""

    def __init__(self, base_url):
        parsed = urllib.parse.urlparse(base_url)
        self._host = parsed.hostname
        self._port = parsed.port or (443 if parsed.scheme == "https" else 80)
        self._secure = parsed.scheme == "https"
        self._connect()

    def _connect(self):
        factory = http.client.HTTPSConnection if self._secure else http.client.HTTPConnection
        self._conn = factory(self._host, self._port, timeout=30)

    def request(self, method, path, body=None, headers=None):
        headers = dict(headers or {})
        if body is not None and not isinstance(body, (bytes, bytearray)):
            body = json.dumps(body).encode("utf-8")
            headers.setdefault("Content-Type", "application/json")

        try:
            self._conn.request(method, path, body, headers)
            response = self._conn.getresponse()
            return response.status, response.read()
        except (http.client.HTTPException, OSError):
            # A dropped keep-alive connection is not a seeding failure; reconnect and retry once.
            self._connect()
            self._conn.request(method, path, body, headers)
            response = self._conn.getresponse()
            return response.status, response.read()


def encode_tags(tags):
    return "&".join(
        f"{urllib.parse.quote(key)}={urllib.parse.quote(value)}" for key, value in tags.items()
    )


def main():
    base_url = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8000"
    client = Client(base_url)

    status, _ = client.request("GET", "/")
    if status != 200:
        print(f"No PepperX node at {base_url} (status {status})", file=sys.stderr)
        return 1

    for name, tags, cache in CONTAINERS:
        body = {"Name": name}
        if tags:
            body["Tags"] = tags
        if cache:
            body["Cache"] = cache
        status, payload = client.request("PUT", "/v1.0/containers", body)
        if status not in (200, 201, 409):
            print(f"  container {name}: {status} {payload[:120]!r}", file=sys.stderr)
        else:
            print(f"  container {name}")

    for index, (container, key, content_type, labels, tags) in enumerate(OBJECTS):
        payload = (f"PepperX sample payload for {key}\n" * random.randint(5, 80)).encode("utf-8")
        metadata = {
            "seeded": True,
            "index": index,
            "source": {"pipeline": "factory-seed", "version": 1},
        }
        headers = {
            "Content-Type": content_type,
            "x-pepperx-labels": ",".join(labels),
            "x-pepperx-tags": encode_tags(tags),
            "x-pepperx-object": base64.b64encode(json.dumps(metadata).encode("utf-8")).decode("ascii"),
        }
        path = f"/v1.0/containers/{container}/object?key={urllib.parse.quote(key)}"
        status, response = client.request("PUT", path, payload, headers)
        if status not in (200, 201):
            print(f"  object {key}: {status} {response[:120]!r}", file=sys.stderr)
        else:
            print(f"  object {container}/{key} ({len(payload)} bytes)")

    # Traffic so the activity chart and request history are not empty. The deliberate 404s give the
    # failure series something to plot, which is how you can tell the chart is working at all.
    print("  generating traffic")
    for index in range(150):
        client.request("POST", "/v1.0/containers/enumerate", {"MaxResults": 10})
        client.request("GET", "/v1.0/admin/stats")
        if index % 8 == 0:
            client.request("GET", "/v1.0/containers/no-such-container")
        if index % 11 == 0:
            client.request("GET", "/v1.0/containers/telemetry/object?key=absent.json")

    print("Seeded.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
