# PepperX MCP API

> Stub — the full transport and tool reference is written in Phase 15. See
> `PEPPERX_PLAN.md` §11.6.

PepperX exposes a Model Context Protocol server (built on Voltaic) over two
transports: Streamable HTTP at `/mcp` (port 8003) and TCP JSON-RPC (port 8004).
It advertises the `tools` capability and provides REST-equivalent operations as
MCP tools (`pepperx_container_*`, `pepperx_object_*`, `pepperx_search`,
`pepperx_stats`, `pepperx_nodes`).
