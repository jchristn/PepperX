<div align="center">
  <img src="https://raw.githubusercontent.com/jchristn/PepperX/main/assets/logo.png" alt="PepperX" width="140" height="140">
</div>

# PepperX

> **Alpha — v0.1.0.** Interfaces and on-disk formats may change between releases.

A high-performance, horizontally scalable key-value store with rich metadata and immutable,
self-describing storage — reachable over five protocols at once: **REST**, **S3**, **Redis RESP**,
**WebSockets**, and **MCP**.

⚠️ **PepperX is unauthenticated by design.** It is backend infrastructure meant to run inside a
trusted network behind a service that performs its own access control. Do not publish these ports to
the internet.

- Source and full documentation: https://github.com/jchristn/PepperX
- License: MIT

---

## Images

| Image | Contents |
|---|---|
| `jchristn77/pepperx-server` | Server node |
| `jchristn77/pepperx-dashboard` | React admin dashboard (static, served by nginx) |

Tags: `v0.1.0` for a pinned version, `latest` for the newest build. Pin the version in anything you
depend on — this is alpha software and `latest` will move under you.

Both images are multi-architecture: `linux/amd64` and `linux/arm64/v8`, so they run on Intel/AMD
servers and on ARM (Apple silicon, Graviton, Raspberry Pi) without a platform override.

---

## Quick start

PepperX needs PostgreSQL for metadata. The fastest complete path:

```yaml
# compose.yaml
name: pepperx

services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_USER: pepperx
      POSTGRES_PASSWORD: pepperx
      POSTGRES_DB: pepperx
    volumes:
      - postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U pepperx -d pepperx"]
      interval: 5s
      retries: 20

  pepperx:
    image: jchristn77/pepperx-server:v0.1.0
    depends_on:
      postgres: { condition: service_healthy }
    ports:
      - "8000:8000"   # REST
      - "8001:8001"   # S3
      - "8002:8002"   # WebSockets
      - "8003:8003"   # MCP over HTTP
      - "8004:8004"   # MCP over TCP
      - "6379:6379"   # Redis RESP
    volumes:
      - ./pepperx.json:/app/pepperx.json:ro
      - extent-data:/app/data/extents

  dashboard:
    image: jchristn77/pepperx-dashboard:v0.1.0
    environment:
      # Pre-filled on the connect screen. Resolved by the browser, not the container.
      PEPPERX_SERVER_URL: http://localhost:8000
    ports:
      - "3000:80"

volumes:
  postgres-data:
  extent-data:
```

```bash
curl -O https://raw.githubusercontent.com/jchristn/PepperX/main/pepperx.json
# edit Database.Hostname to "postgres"
docker compose up -d
```

Then open http://localhost:3000 and connect the dashboard to `http://localhost:8000`.

---

## Configuration

The server reads **`pepperx.json`** from its working directory (`/app`). The image ships no settings
file of its own — mount yours:

```yaml
volumes:
  - /etc/pepperx/production.json:/app/pepperx.json:ro
```

Configuration is deliberately file-based rather than environment-variable-based: the settings are
nested and typed, and flattening them into dozens of variables would make a full configuration harder
to read, not easier. Start from the
[default file](https://raw.githubusercontent.com/jchristn/PepperX/main/pepperx.json), which documents
every setting inline.

The two settings you must change for a container deployment:

| Setting | Why |
|---|---|
| `Database.Hostname` | Point at your PostgreSQL service name, not `localhost` |
| `Mcp.Hostname` | Set to `"*"`; the default `localhost` binds loopback and is unreachable from outside the container |

Settings are read once at startup. Restart the container after changing them.

### Dashboard

The dashboard image takes one variable:

| Variable | Default | Notes |
|---|---|---|
| `PEPPERX_SERVER_URL` | `http://localhost:8000` | Pre-filled on the connect screen |

A static bundle cannot read environment variables, so the container writes this into `config.json`
in the web root at startup and the app fetches it before rendering. That keeps one image usable
against any node instead of requiring a rebuild to repoint the console.

The **browser** resolves this address, not the container — so a compose service name like
`http://node1:8000` will not work, and it must be the node's REST port (8000), not the RESP port
(6379) that also appears in `docker ps` for the same container.

---

## Volumes

| Path | Contents |
|---|---|
| `/app/data/extents` | Object payloads. **Mount a volume.** |
| `/app/pepperx.json` | Settings file, mounted read-only |

Without a volume on `/app/data/extents`, removing the container destroys every object while the
metadata database keeps describing them. If you run more than one node, **every node must mount the
same extent storage** — nodes are interchangeable and any node may be asked to serve any object.

---

## Ports

| Port | Protocol |
|---|---|
| 8000 | REST (plus Swagger UI at `/swagger`) |
| 8001 | S3 |
| 8002 | WebSockets |
| 8003 | MCP over Streamable HTTP |
| 8004 | MCP over TCP JSON-RPC |
| 6379 | Redis RESP |

All are configurable in `pepperx.json`; these are the defaults.

---

## Scaling out

Nodes are stateless and coordinate through PostgreSQL and shared extent storage. To add one, start
another container with the same database and the same extent volume — no additional configuration,
no leader election. Put a load balancer in front and they are interchangeable.

Deletes coordinate cluster-wide: an extent is tombstoned, in-flight read leases on every node drain,
and only then is the payload destroyed, so a read that has begun always completes.

---

## Health

```
GET /v1.0/api/health
```

The image declares a `HEALTHCHECK` against this endpoint. It reports process liveness only — it does
not probe PostgreSQL or storage, so use `GET /v1.0/admin/stats` when you need to know whether the
node's dependencies are actually reachable.

---

## Security

There is no authentication anywhere in PepperX. Every endpoint is reachable by anyone who can reach
the port, including container deletion.

The S3 static credentials are **not** a security control — they exist because most S3 clients refuse
to send an unsigned request. Signatures are accepted, not verified.

Bind these ports to a private network and put your own authenticated service in front.

---

## Documentation

- [README](https://github.com/jchristn/PepperX#readme)
- [REST API](https://github.com/jchristn/PepperX/blob/main/REST_API.md) ·
  [S3](https://github.com/jchristn/PepperX/blob/main/S3_API.md) ·
  [RESP](https://github.com/jchristn/PepperX/blob/main/RESP_API.md) ·
  [WebSockets](https://github.com/jchristn/PepperX/blob/main/WEBSOCKETS_API.md) ·
  [MCP](https://github.com/jchristn/PepperX/blob/main/MCP_API.md)
- [SDKs](https://github.com/jchristn/PepperX/blob/main/sdk/README.md) for C#, Python, and JavaScript
- [Changelog](https://github.com/jchristn/PepperX/blob/main/CHANGELOG.md)
