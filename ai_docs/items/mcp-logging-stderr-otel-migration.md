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
2026-08-13 mcp-audit evidence: McpLoggingProvider.cs:32 hardcodes an Information floor — client logging/setLevel is ignored, so the declared logging capability overstates conformance. Do NOT patch in place (surface deprecated under protocol 2026-07-28); fold into this migration. This row is also the prerequisite for sdk-2x-upgrade: SDK 2.x marks the MCP logging surface obsolete (MCP9005), a build break under warnings-as-errors. See ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md §5.
