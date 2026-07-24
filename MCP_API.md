# PepperX MCP API

PepperX is an MCP server, so an LLM agent can store, search, and retrieve objects as tool calls
without anyone writing an integration. Built on [Voltaic](https://www.nuget.org/packages/Voltaic).

Two transports, both serving the same 16 tools:

| Transport | Default | Endpoint |
|---|---|---|
| Streamable HTTP | port **8003** | `http://localhost:8003/mcp/rpc` (events at `/mcp/events`) |
| TCP JSON-RPC | port **8004** | line-delimited JSON-RPC |

---

## Contents

- [Binding](#binding)
- [Connecting](#connecting)
- [Tools](#tools)
- [Arguments and results](#arguments-and-results)
- [Limits](#limits)
- [Errors](#errors)

---

## Binding

`Mcp.Hostname` defaults to `localhost`, which binds loopback only. That is the right default for a
developer machine and wrong for a container: the port is published but nothing outside can reach the
listener.

Set it to `*` to bind all interfaces:

```json
"Mcp": { "Enabled": true, "Hostname": "*", "HttpPort": 8003, "TcpPort": 8004 }
```

The shipped Docker configuration already does this. On Windows, binding all interfaces needs a
`netsh http add urlacl` reservation or an elevated process — which is why loopback remains the
default rather than the other way around.

---

## Connecting

### Claude Code / Claude Desktop

```bash
claude mcp add --transport http pepperx http://localhost:8003/mcp/rpc
```

Or in an MCP client configuration file:

```json
{
  "mcpServers": {
    "pepperx": {
      "type": "http",
      "url": "http://localhost:8003/mcp/rpc"
    }
  }
}
```

### By hand

MCP is JSON-RPC 2.0. Initialize, then call tools:

```bash
curl -s http://localhost:8003/mcp/rpc \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{
    "jsonrpc": "2.0", "id": 1, "method": "initialize",
    "params": {
      "protocolVersion": "2025-03-26",
      "capabilities": {},
      "clientInfo": { "name": "probe", "version": "1.0" }
    }
  }'
```

The `Accept` header must include both `application/json` and `text/event-stream`; Streamable HTTP may
answer either way and a request that accepts only one is rejected.

List and call:

```bash
curl -s http://localhost:8003/mcp/rpc \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'

curl -s http://localhost:8003/mcp/rpc \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{
    "jsonrpc": "2.0", "id": 3, "method": "tools/call",
    "params": {
      "name": "pepperx_object_write",
      "arguments": {
        "container": "telemetry",
        "key": "notes/summary.txt",
        "dataBase64": "aGVsbG8gZnJvbSBNQ1A=",
        "contentType": "text/plain",
        "labels": ["note"],
        "tags": { "source": "agent" }
      }
    }
  }'
```

---

## Tools

### Containers

| Tool | Required | Notes |
|---|---|---|
| `pepperx_container_create` | `name` | Optional `tags` |
| `pepperx_container_read` | `container` | |
| `pepperx_container_list` | — | Optional `prefix`, `maxResults` |
| `pepperx_container_enumerate` | — | Same as list |
| `pepperx_container_update_tags` | `container`, `tags` | Replaces the whole tag map |
| `pepperx_container_delete` | `container` | `force: true` deletes its objects too |

### Objects

| Tool | Required | Notes |
|---|---|---|
| `pepperx_object_write` | `container`, `key` | `dataBase64`, `contentType`, `labels`, `tags`, `object`, `noOverwrite` |
| `pepperx_object_read` | `container`, `key` | Returns the payload base64-encoded |
| `pepperx_object_read_metadata` | `container`, `key` | Metadata only, no payload |
| `pepperx_object_update_metadata` | `container`, `key` | `labels`, `tags`, `object` |
| `pepperx_object_exists` | `container`, `key` | |
| `pepperx_object_delete` | `container`, `key` | |
| `pepperx_object_enumerate` | `container` | `prefix`, `labelsFilter`, `tagsFilter`, `maxResults` |

### Search and admin

| Tool | Required | Notes |
|---|---|---|
| `pepperx_search` | — | `containers`, `prefix`, `labelsFilter`, `tagsFilter`, `maxResults` |
| `pepperx_stats` | — | Aggregate statistics |
| `pepperx_nodes` | — | Cluster nodes and liveness |

Voltaic also registers its own `ping`, `echo`, `getTime`, and `getSessions` diagnostics.

Every tool publishes a full JSON Schema with per-argument descriptions, so an agent can discover how
to call it from `tools/list` alone. Prefer `pepperx_object_read_metadata` over `pepperx_object_read`
when you only need to know what an object is — it avoids pulling the payload into the context window.

---

## Arguments and results

### Argument names are camelCase

The published schemas declare camelCase (`dataBase64`, `labelsFilter`, `noOverwrite`) and arguments
are validated against them. Names outside the schema are rejected with a "missing required property"
error rather than silently ignored, so a typo fails loudly instead of producing an empty write.

### Filters are conjunctive

`labelsFilter: ["metric","cpu"]` matches objects carrying **both** labels, and `tagsFilter` requires
every listed pair to match. There is no "any of" — issue separate calls and merge if you need it.

### Results are structured

Tool results carry the same JSON shapes the REST API returns, so
[`REST_API.md`](REST_API.md) documents the fields. `pepperx_object_read` additionally returns
`dataBase64` alongside the metadata.

---

## Limits

`Mcp.MaxInlineBytes` (8 MiB by default) caps what `pepperx_object_read` will return. Larger objects
return an error directing you to the REST API rather than base64-encoding tens of megabytes into a
JSON-RPC response — which would exhaust an agent's context window well before it exhausted the
server's memory.

Writes are bounded by the same limit, since the payload arrives base64-encoded in the request.

---

## Errors

Tool failures come back as MCP tool errors — a result with `isError: true` and a text explanation —
rather than JSON-RPC protocol errors:

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "content": [{ "type": "text", "text": "Object not found." }],
    "isError": true
  }
}
```

This is deliberate: an agent should see a failed tool call as something it can react to and retry
differently, not as a transport fault. Genuine protocol problems — an unknown method, a malformed
request, arguments that violate the schema — do return JSON-RPC errors.

---

## See also

- [`REST_API.md`](REST_API.md) — result shapes and the complete surface
- [`S3_API.md`](S3_API.md) · [`RESP_API.md`](RESP_API.md) · [`WEBSOCKETS_API.md`](WEBSOCKETS_API.md)
