# server-structured-observability-sink — Add a server-side structured observability sink

**row:** `server-structured-observability-sink` · **pri:** `High` · **size:** `M` · **deps:** `public-exception-detail-policy,request-correlation-context-lifecycle`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- New focused observability options/provider under `src/RoslynMcp.Host.Stdio/`.
- New `tests/RoslynMcp.Tests/ServerObservabilitySinkTests.cs`.
- `docs/stdio-client-integration.md`

## Acceptance

- [ ] Add an opt-in configured server-side sink; the disabled default makes no network call and adds no stdout traffic.
- [ ] Consume `public-exception-detail-policy` for one captured tool failure, preserving level, category, event/correlation identifiers, and secret-safe exception type/inner/stack structure without duplicating redaction logic.
- [ ] Consume the request-scoped identifier owned by `request-correlation-context-lifecycle`; do not create a second ambient context or identifier lifecycle.
- [ ] Never emit secret-bearing values; exclude user-code content by default and allow explicit opt-in only for richer payloads that are classified non-secret before emission.
- [ ] Sink failures fall back once to stderr without recursion, swallowed tasks, or impact on the MCP response.
- [ ] Keep the sink independent of deprecated MCP logging capabilities and protocol versions.

## Evidence

- Retiring `McpLoggingProvider` is required for wire safety, but client-safe error redaction still needs an internal destination for secret-safe diagnostic structure.
- The current provider conflates server observability with client protocol notifications.
