# PepperX WebSockets API

> Stub — the full envelope schema and operation catalog are written in Phase 15.
> See `PEPPERX_PLAN.md` §11.5.

The WebSockets API offers REST-equivalent operations over a single persistent
connection on port 8002. Clients send JSON request envelopes (`RequestId`,
`Operation`, `Container`, `Key`, `Body`, `Query`) and receive correlated response
envelopes. Multiple requests may be in flight concurrently on one connection.
