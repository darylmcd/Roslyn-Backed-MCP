# server-start-time-source-consolidation — Single-source server process start time

**row:** `server-start-time-source-consolidation` · **pri:** `Medium` · **size:** `M` · **deps:** `mcp-logging-stderr-otel-migration`

## Anchors

- New `src/RoslynMcp.Host.Stdio/Runtime/ServerProcessMetadata.cs`.
- `src/RoslynMcp.Host.Stdio/Program.cs` — startup registry metadata.
- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs` — info/heartbeat connection DTOs.
- `tests/RoslynMcp.Tests/HostProcessMetadataTests.cs`
- `tests/RoslynMcp.Tests/ServerHeartbeatTests.cs`

## Acceptance

- [ ] Resolve the process start timestamp once and inject/use that value for registry, `server_info`, and `server_heartbeat`.
- [ ] Narrow expected `Process.StartTime` access failures and describe the wall-clock fallback accurately; unexpected failures are not swallowed.
- [ ] Emit only secret-safe fallback diagnostics and never report the fallback as monotonic time.
- [ ] One source/fallback matrix proves all three public surfaces publish the same timestamp.

## Evidence

- `Program` and `ServerTools` independently read `Process.StartTime`, duplicate fallback logic, and can publish different values; the `ServerTools` comment incorrectly calls `DateTime.UtcNow` monotonic.
