---
name: pepperx-gotchas
description: "Non-obvious PepperX behaviors and platform traps found during implementation"
metadata:
  node_type: memory
  type: project
---

Things about PepperX that cost real debugging time and are not visible from the code. Verified
2026-07-23 against the Docker stack.

**Watson response defaults leak request headers.** `WebserverSettings.Headers.DefaultHeaders` ships
seeded with Accept, Accept-Language, Accept-Charset, Cache-Control, Connection, and Host — all
request headers. Echoing `Connection: close` on a keep-alive connection makes Node's HTTP parser
reject the entire response ("Data after `Connection: close`"), which broke every Node client on
object reads while curl tolerated it. `PepperXServer` now clears the collection wholesale.

**Never hand-roll chunked framing in a Watson typed route.** Returning `null!` after
`SendChunk(..., final: true)` makes the route wrapper send an empty response too, emitting a second
`0\r\n\r\n`. Use `Response.Send(length, stream)` instead.

**Watson's OpenAPI handler adds its own CORS header** and runs after pre-routing, so adding ours as
well produced `Access-Control-Allow-Origin: *, *` and browsers rejected it. `/openapi.json` is
special-cased to defer.

**Wildcard hostnames need `+`, and Windows will not allow it unelevated.** `Hostname: "*"` must map
to the HttpListener `+` prefix to bind all interfaces; binding `localhost` leaves a listener
unreachable from outside a container while REST and S3 on the same node work. But `+` needs a
`netsh http add urlacl` reservation on Windows, so both the WebSocket and MCP handlers try the
wildcard and fall back to loopback. Do **not** bind `+` together with `localhost`/`127.0.0.1` — that
double-binds and fails with "address in use". The in-process test suite binds loopback and therefore
cannot catch this class of bug; `docker/README.md` has the reachability check that does.

**MCP arguments are camelCase.** Tool schemas declare camelCase, clients emit camelCase, and
`McpToolArgs` is PascalCase — default case-sensitive binding left every property null and calls
failed deep inside a service. The suite missed it for the same reason it existed: the tests spoke the
server's internal casing. Binding is now case-insensitive, and Voltaic validates arguments against
the published schema, so off-schema names are refused outright.

**RESP needs proper array framing.** Inline commands (`PING\r\n`) hang; send
`*1\r\n$4\r\nPING\r\n`. Use `redis-cli` for manual probing.

**Postgres on 5432 collides with most dev machines.** The compose stack publishes it on 5442; the
nodes reach it over the compose network and do not need the mapping at all.

See [[pepperx-overview]].
