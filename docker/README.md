# Docker

A complete PepperX deployment: PostgreSQL, two nodes, and the dashboard.

```bash
cd docker
docker compose up -d
```

| | |
|---|---|
| Dashboard | http://localhost:3000 — connect to `http://localhost:8000` |
| node1 REST | http://localhost:8000 (Swagger UI at `/swagger`) |
| node2 REST | http://localhost:8010 |
| PostgreSQL | `localhost:5442` |

---

## Layout

| Path | Contents |
|---|---|
| `compose.yaml` | The full stack: PostgreSQL, two nodes, dashboard |
| `compose.test.yaml` | PostgreSQL only, for running the test suite |
| `server/Dockerfile` | Server image |
| `dashboard/Dockerfile` | Dashboard image (Vite build → nginx) |
| `config/` | Settings files mounted into each node |
| `factory/` | Reset-and-seed scripts |

---

## Images

`compose.yaml` **pulls** published images rather than building them:

| Service | Image |
|---|---|
| node1, node2 | `jchristn77/pepperx-server:v0.1.0` |
| dashboard | `jchristn77/pepperx-dashboard:v0.1.0` |

That means this directory works from a bare checkout of just `docker/`, and that everyone running the
stack gets the same bits rather than whatever their local build produced.

### Building them yourself

From the **repository root**, not from `docker/`:

```bat
build-all.bat v0.1.0          :: both images
build-server.bat v0.1.0       :: just the server
build-dashboard.bat v0.1.0    :: just the dashboard
```

These build for `linux/amd64` and `linux/arm64/v8` on Docker Build Cloud and push both the version tag
and `latest`. Multi-architecture manifests cannot be loaded into the local daemon, which is why they
push rather than `--load`.

To build a single-architecture image locally for testing instead:

```bash
docker build -f docker/server/Dockerfile -t pepperx-server:local .
docker build -f docker/dashboard/Dockerfile -t pepperx-dashboard:local .
```

Both take the repository root as context — the server image needs `src/` and `pepperx.json`, the
dashboard image needs `dashboard/` and `docker/dashboard/nginx.conf`. Point `compose.yaml` at the
`:local` tags if you want the stack to run what you just built.

---

## Why two nodes

A single-node stack hides the mistakes that only appear with more than one. The important one:
**both nodes mount the same extent volume**. Nodes are stateless and interchangeable, so any node may
be asked to serve any object — point them at separate volumes and reads will intermittently 404
depending on which node the load balancer picked.

Verify the cluster is really working by writing through one node and reading through the other:

```bash
curl -X PUT 'http://localhost:8000/v1.0/containers/telemetry/object?key=cross.txt' \
  -H 'Content-Type: text/plain' --data-binary 'written on node1'

curl 'http://localhost:8010/v1.0/containers/telemetry/object?key=cross.txt'
# -> written on node1
```

Both nodes appear in `GET /v1.0/admin/nodes` and in the dashboard's Capacity view.

---

## Verifying protocol reachability

Worth running after changing any listener configuration, and the reason this section exists: the
WebSocket and MCP listeners bound loopback when configured with `Hostname: "*"`, which made them
unreachable from outside a container while REST and S3 on the same node worked. The ports were
published and nothing was listening on the interface Docker forwards to.

The in-process test suite cannot catch that — it binds loopback by nature, and on Windows the
wildcard prefix needs a `urlacl` reservation a test run cannot assume. So this check belongs here:

```bash
# REST, S3
curl -fsS http://localhost:8000/v1.0/api/health && echo "REST ok"
curl -fsS -o /dev/null http://localhost:8001/ && echo "S3 ok"

# RESP
redis-cli -p 6379 PING

# MCP — expects a JSON-RPC result, not a connection refusal
curl -fsS http://localhost:8003/mcp/rpc \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | head -c 80

# WebSockets — expects 101 Switching Protocols.
# --max-time is required: after a successful upgrade curl holds the connection open forever,
# so without it this command never returns.
curl -s -o /dev/null -w '%{http_code}\n' --max-time 3 \
  -H 'Connection: Upgrade' -H 'Upgrade: websocket' \
  -H 'Sec-WebSocket-Version: 13' -H 'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==' \
  http://localhost:8002/
```

A connection refusal from any of these while REST answers means that listener bound the wrong
interface. Check `Hostname` for that protocol in `config/nodeN.json` — it must be `"*"` in a
container.

---

## Ports

`node2` publishes the same protocols on a `+10` offset so both can run on one host:

| Protocol | node1 | node2 |
|---|---|---|
| REST | 8000 | 8010 |
| S3 | 8001 | 8011 |
| WebSockets | 8002 | 8012 |
| MCP (HTTP) | 8003 | 8013 |
| MCP (TCP) | 8004 | 8014 |
| RESP | 6379 | 6380 |

PostgreSQL is published on **5442**, not 5432. The nodes reach it over the compose network and do not
need the mapping at all — it exists so you can inspect metadata with `psql` — and using the default
port would collide with the PostgreSQL most developers already run locally, making the stack refuse
to start over a mapping nothing depends on.

---

## Configuration

See [`config/README.md`](config/README.md). In short: edit `config/nodeN.json`, then
`docker compose restart nodeN`. Settings are read once at startup.

The dashboard is configured by environment variable instead, since it is a static bundle:

| Variable | Default | Notes |
|---|---|---|
| `PEPPERX_SERVER_URL` | `http://localhost:8000` | Pre-filled on the connect screen |

The container writes it into `config.json` in the web root at startup and the app reads that before
rendering. Note that the **browser** resolves the address: a compose service name like
`http://node1:8000` resolves inside the network and nowhere else. It must also be the REST port
(8000) — `docker ps` shows 6379 on the same container, but that is the Redis RESP listener.

An operator's own last-used endpoint takes precedence over this default, so it only affects a
first visit.

Keep production settings out of the repository and mount your own file:

```yaml
volumes:
  - /etc/pepperx/production.json:/app/pepperx.json:ro
```

---

## Factory reset

[`factory/reset.sh`](factory/) (POSIX) and [`factory/reset.bat`](factory/) (Windows) tear the stack
down **including its volumes**, bring it back up clean, and seed sample data:

```bash
./factory/reset.sh           # reset and seed
./factory/reset.sh --empty   # reset, leave empty
```

This deletes every object in the deployment. It is for development and demos.

The seed data exists so the dashboard has something to show — a freshly reset node with an empty Home
view tells you nothing about whether the deployment actually works. It creates four containers, eight
objects exercising all three metadata forms, and enough traffic (including deliberate 404s) to give
the activity chart both series.

---

## Persistence

| Volume | Contents |
|---|---|
| `postgres-data` | Metadata index |
| `extent-data` | Object payloads |

`docker compose down` keeps both. `docker compose down -v` destroys them.

Losing `postgres-data` is recoverable — the extents are self-describing, so
`POST /v1.0/admin/rehydrate` with `{"Mode":"Rebuild"}` reconstructs the entire database by reading
extent headers off disk. Losing `extent-data` is not: that is the actual data.

---

## Security

Nothing in this stack is authenticated, and the credentials in `config/` are public knowledge —
they are checked into the repository. This is a development and evaluation stack. Read
[Security](../README.md#security) before putting any of it on a network you do not control.
