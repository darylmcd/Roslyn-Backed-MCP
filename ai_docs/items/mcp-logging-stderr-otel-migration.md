# mcp-logging-stderr-otel-migration — Retire protocol logging compatibility

**row:** `mcp-logging-stderr-otel-migration` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/McpLoggingProvider.cs`
- `src/RoslynMcp.Host.Stdio/Program.cs`
- `tests/RoslynMcp.Tests/StartupDiagnosticsTests.cs`
- `docs/stdio-client-integration.md`

## Acceptance

- [ ] Operational logs continue to reach stderr without writing non-protocol bytes to stdout.
- [ ] Structured observability uses OpenTelemetry or another configured server-side sink before `notifications/message` is retired.
- [ ] Fire-and-forget send failures are observable at a non-recursive fallback boundary instead of silently swallowed.
- [ ] Public migration notes identify the release that stops emitting deprecated logging notifications.
- [ ] The legacy MCP9005 Logging suppression and `McpLoggingProvider` are removed.

## Evidence

- ModelContextProtocol 2.1 deprecates protocol Logging and recommends stderr for stdio plus OpenTelemetry for structured observability.
- `McpLoggingProvider.SendLogAsync` currently swallows all send failures, leaving no signal when the compatibility notification path breaks.
