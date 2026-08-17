# mcp-logging-stderr-otel-migration — Retire unsafe protocol logging

**row:** `mcp-logging-stderr-otel-migration` · **pri:** `High` · **size:** `M` · **deps:** `server-structured-observability-sink,host-assembly-marker-foundation,host-assembly-marker-wire-test-migration`

## Anchors

- `src/RoslynMcp.Host.Stdio/McpLoggingProvider.cs`
- `src/RoslynMcp.Host.Stdio/Program.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs` — stale public `Logging: true` capability.
- `src/RoslynMcp.Core/Models/GateMetricsDto.cs` — stale client `notifications/message` guidance.
- `src/RoslynMcp.Host.Stdio/README.md`
- `skills/mcp-server-surface-test/prompts/full.md` — shipped provider-specific debug-channel claim.
- New `tests/RoslynMcp.Tests/McpLoggingLifecycleWireTests.cs`.
- `docs/stdio-client-integration.md`
- `docs/upgrade-matrix.md`
- `ai_docs/references/mcp-server-best-practices.md`

## Acceptance

- [ ] Emit zero `notifications/message` frames before initialization completes and zero unsolicited protocol logging after the bridge is retired.
- [ ] Keep operational logs on stderr without writing non-protocol bytes to stdout.
- [ ] Require `server-structured-observability-sink` to preserve internal diagnostics before this bridge is removed; do not reimplement its sink contract here.
- [ ] Never serialize `Exception.ToString()`, raw messages, stacks, paths, or secret sentinels onto the MCP wire.
- [ ] Remove fire-and-forget protocol sends and catch-all suppression with the bridge.
- [ ] Remove `McpLoggingProvider`, the MCP9005 suppression, stale logging capability claims, and obsolete `logging/setLevel` behavior.
- [ ] Report `Logging: false` in `server_info` and remove provider-specific compatibility guidance from the shipped surface-test prompt and all anchored documentation.
- [ ] One raw lifecycle regression proves no pre-negotiation frames and no post-retirement logging notifications while stderr still receives operational events.
- [ ] Add the required deprecation/migration record for published consumers.

## Evidence

- A current raw host emitted exactly five `notifications/message` frames before any initialize request.
- The provider hard-codes an Information floor, serializes `exception.ToString()`, fire-and-forgets every send, and swallows all send failures.
- SDK 2.1 deprecates protocol logging; explicit 2026-07-28 requests reject `logging/setLevel`, while the legacy method remains functional only on older negotiated sessions until this bridge is retired.
- Program and four test files use the provider type only as an assembly marker; the two marker-migration rows must remove that accidental ownership before deletion.
