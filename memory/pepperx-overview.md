---
name: pepperx-overview
description: "What PepperX is, its resolved design decisions, and where the plan lives"
metadata: 
  node_type: memory
  type: project
  originSessionId: e4769af8-0705-4f2e-b377-9a9a4a4e533f
  modified: 2026-07-23T17:28:57.367Z
---

PepperX is a high-performance, **unauthenticated** key-value store (backend infra; the consuming app handles auth). Built C#/Watson7 backend + React dashboard + C#/Python/JS SDKs.

Core model: a **Container** holds immutable **Extents** (one extent = one key/value). Value = arbitrary binary payload + metadata in three forms: Labels (string list), Tags (string→string dict), Object (freeform JSON). PostgreSQL is the authoritative metadata store (labels/tags/extent location — NOT the JSON Object); extent files are self-describing ("PXE1" format) so the DB can be fully rehydrated from raw storage. Stateless scale-out nodes coordinate only through Postgres.

Five protocol surfaces in one process: native REST (Watson7, :8000, most complete + OpenAPI), S3 (S3Server, :8001, buckets/objects/tags only), Redis RESP (RedisRespServer, :6379), WebSockets (WatsonWebsocket, :8002), MCP (Voltaic, :8003 HTTP / :8004 TCP).

Resolved decisions (owner, 2026-07-23): binary payload + metadata sidecar; NO tenants/auth (explicit divergence from AUTHENTICATION.md); overwrite = atomic replace (extent stays immutable); Postgresql-only behind IMetadataDatabaseDriver; delete-blocks-reads uses **full cross-node coordination** via per-read DB leases with TTL; SDKs cover REST + WebSockets only (S3/RESP users use standard clients).

The full actionable build plan is `C:\Code\PepperX\PEPPERX_PLAN.md` — 17 phases (P00–P16), 133 checkbox tasks, per-phase conformance gates, decision log, route inventory. Product spec is `PEPPERX.md`. Follows [[agents-requirements-and-reference-apps]].
