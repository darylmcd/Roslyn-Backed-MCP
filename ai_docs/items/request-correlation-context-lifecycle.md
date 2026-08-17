# request-correlation-context-lifecycle — Own request correlation at the MCP boundary

**row:** `request-correlation-context-lifecycle` · **pri:** `High` · **size:** `M` · **deps:** `host-assembly-marker-foundation`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs` — global incoming-message filter registration.
- `src/RoslynMcp.Host.Stdio/McpLoggingProvider.cs` — extract the current lazy provider-owned context.
- New focused correlation context/filter under `src/RoslynMcp.Host.Stdio/Middleware/`.
- New `tests/RoslynMcp.Tests/RequestCorrelationContextTests.cs`.

## Acceptance

- [ ] Move correlation ownership out of `McpLoggingProvider`; reading the current identifier never lazily creates a process-lifetime value.
- [ ] Register one global boundary through `WithMessageFilters(filters => filters.AddIncomingFilter(...))`; begin a unique scope before incoming request/message dispatch and clear/dispose it in `finally` after success, failure, or cancellation.
- [ ] Keep the context request-scoped with no static/session response cache; expose only the current identifier to downstream diagnostics.
- [ ] One concurrency/lifecycle matrix proves simultaneous requests receive distinct identifiers and a later sequential request inherits neither identifier.
- [ ] Preserve the same identifier across one request's public error reference and server-side diagnostic event without putting diagnostic detail on stdout.

## Evidence

- `CorrelationContext` currently lives inside the retiring logging provider, lazily creates an `AsyncLocal` value on read, and has no explicit request begin/clear lifecycle.
- The provider is its only current consumer, so retiring it without a dedicated owner would also discard correlation or encourage duplicated ambient-state implementations.
