---
name: agents-requirements-and-reference-apps
description: Location of the non-negotiable requirements docs and which C:\Code app to imitate per pattern
metadata: 
  node_type: memory
  type: reference
  originSessionId: e4769af8-0705-4f2e-b377-9a9a4a4e533f
  modified: 2026-07-23T18:10:34.770Z
---

Non-negotiable requirements docs live in `C:\code\agents\requirements\`: CODE_STYLE, BACKEND_ARCHITECTURE, BACKEND_TEST_ARCHITECTURE, FRONTEND_ARCHITECTURE, DASHBOARD_STYLE_AND_USABILITY, REPOSITORY_REQUIREMENTS, AUTHENTICATION, I18N, WRITING_DOCUMENTS, EXAMPLE_APPLICATIONS. Read the relevant ones before building the matching layer; they override defaults and divergence must be explicit + justified.

Reference apps under `C:\Code` (imitate these, don't reinvent):
- Watson7 hosting + fluent OpenAPI annotation + request-history capture: `RecallDB\RecallDB\src\RecallDb.Server\RecallDbServer.cs`
- DB driver base/factory/provider-folders (Implementations/ + Queries/, handwritten SQL): Verbex, NetLedger `src\...\Database\`
- EnumerationQuery/EnumerationResult<T> (incl. Labels/Tags filters): `LiteGraph\HnswLite\...\Classes\EnumerationQuery.cs`, `LiteGraph\litegraph\src\LiteGraph\EnumerationResult.cs`
- Touchstone Test.Shared/Automated/Xunit/Nunit: Conductor, Tempo (`Test.Shared\TempoSuites.cs`)
- S3 protocol lib (callback surface): `Less3\S3Server-7.0\src\S3Server\Callbacks\*`
- RESP protocol lib: `RedisRespServer\src\Redish.Server`, `Test.StackExchangeRedis`
- MCP via Voltaic: `LiteGraph\litegraph\src\LiteGraph.McpServer`, `Tablix\src\Tablix.Server\Mcp`, `C:\Code\Voltaic\README.md`
- Dashboard: Tempo (shell/tables), Hydra (request history, API explorer, i18n), Conductor (setup/tour)

Pinned versions (2026-07-23): Watson 7.0.15, S3Server 7.3.0, RedisRespServer 0.1.1, WatsonWebsocket 4.1.8, Voltaic 0.4.0 (owner-confirmed latest; split into Voltaic.Core/.Mcp/.A2A), PrettyId 2.0.1, SerializationHelper 2.0.3, SyslogLogging 2.1.0, Touchstone.* 0.1.12. Touchstone source: `C:\code\touchstone`. (Watson 7 supports greedy `{param+}` route segments.)

Code style essentials: no var, no tuples, usings inside namespace, one type per file, XML docs on public only, `_PascalCase` privates, ConfigureAwait(false), CancellationToken on async, no Console.WriteLine in libraries, TreatWarningsAsErrors.
