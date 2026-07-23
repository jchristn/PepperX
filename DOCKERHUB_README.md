# PepperX

High-performance, horizontally scalable key-value store with rich metadata,
immutable self-describing storage, and five protocol surfaces (REST, S3, Redis
RESP, WebSockets, MCP). PepperX is unauthenticated backend infrastructure —
access control belongs to the application in front of it.

> This is a working stub. Full Docker Hub documentation, including image tags,
> environment variables, volumes, and a compose example, is produced in Phase 15
> of the implementation plan. Referenced images use absolute repository URLs,
> for example:
> `https://raw.githubusercontent.com/jchristn/pepperx/main/assets/logo.png`

## Images

- `jchristn/pepperx` — server node (REST 8000, S3 8001, RESP 6379, WS 8002, MCP 8003/8004)
- `jchristn/pepperx-dashboard` — React admin dashboard

## Quickstart

See the repository `docker/compose.yaml` and `docker/factory/reset.sh`.
