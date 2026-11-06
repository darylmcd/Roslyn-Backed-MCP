# roslyn-mcp-transport-closes-during-audit — MCP transport closes during audit

**row:** `roslyn-mcp-transport-closes-during-audit` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- `tests/RoslynMcp.Tests/HostShutdownLifecycleTests.cs`

## Acceptance

- [ ] Reproduce or isolate the stdio transport closure after loading the solution and running sustained semantic reads; capture process-exit and server diagnostics without leaking source or secrets.
- [ ] Correct the owning shutdown, lifetime, or client-boundary fault when proven; add a regression for that mechanism.
- [ ] Verify `server_info` or `server_heartbeat` remains available, or returns a deliberate recoverable error, after the same workload.

## Evidence

- 2026-11-05 live session: server_info succeeded, workspace_load for `D:/Roslyn-Backed-MCP/RoslynMcp.slnx` reached ready, and semantic source calls worked before later `get_source_text` failed with `tool call error ... Caused by: Transport closed`. A repeat `server_info` and separate heartbeat returned the same transport error. The cause was not established.
- A prior `RateLimited` response was an expected 120/60 s execution gate and was not counted as this defect.
