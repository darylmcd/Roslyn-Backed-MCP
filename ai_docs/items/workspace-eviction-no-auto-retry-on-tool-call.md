# workspace-eviction-no-auto-retry-on-tool-call — transparently reload + retry once on WorkspaceEvictedException

**row:** `workspace-eviction-no-auto-retry-on-tool-call` · **pri:** `High` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` (`GetRequiredSession` throws `WorkspaceEvictedException`; LRU eviction picks the session with the smallest `LastAccessedUtc` when `MaxConcurrentWorkspaces` is hit under `evictPolicy=lru`)
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs` (current `WorkspaceEvictedException` handling — classifies/formats only, no catch-and-retry anywhere in the codebase)
- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs` (`test_run`, `compile_check` — the two eviction-adjacent tools that hit this in-window)
- tests under `tests/RoslynMcp.Tests/Tools/` covering the retry wrapper

## Acceptance

- [ ] `compile_check` and `test_run` (at minimum — the two tools that reproduced this in-window) catch `WorkspaceEvictedException`, call `workspace_load` against the evicted session's recorded `LoadedPath` (already carried on `WorkspaceEvictedException`/`EvictedSessionRecord` per the shipped `workspace-id-recovery-hints` work), and retry the original call once against the new `workspaceId` before surfacing failure to the caller
- [ ] On a second failure (still evicted, or a genuinely different error), the original structured error envelope propagates unchanged — no silent infinite retry
- [ ] Regression test: force an eviction mid-call (e.g. load `MaxConcurrentWorkspaces` workspaces then call `compile_check`/`test_run` against the oldest), assert the call transparently recovers instead of throwing
- [ ] Do NOT touch the LRU TTL-refresh mechanism — `WorkspaceManager.GetRequiredSession` already calls `session.TouchAccess()` on every session resolution (confirmed live in current code), so "refresh TTL on every tool call" is already implemented; this row is scoped to the retry wrapper only

## Evidence

- `ai_docs/reports/20260805T210025Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §2a `compile_check`/`test_run` rows + §3 pattern 3. 2 codex sessions: `019fae3e` (`compile_check`, "Not found: Workspace ... not found or has been closed" after ~37min idle), `019fa0a1` (dbf5) (`test_run`, raw `WorkspaceEvictedException` mid-run against RoslynMcp's own test suite, failed 2/110 tests non-deterministically; an identical rerun 90s later with no code change passed 110/110 clean).

## Context

The retro's original proposal bundled two asks: "refresh the Lru TTL on every tool call" and "auto-detect an evicted workspaceId and transparently reload+retry once." Source verification (2026-08-05, this row's authoring pass) confirmed the FIRST ask is already shipped — `GetRequiredSession` touches the access timestamp on every resolution, so an actively-used workspace is never the LRU-eviction candidate; only a genuinely idle one is. The observed eviction was correct LRU behavior under `MaxConcurrentWorkspaces` pressure, not a TTL bug. The SECOND ask — no code anywhere catches `WorkspaceEvictedException` to retry — is the real, still-open gap, and it's what caused the flaky test failure against the server's own test suite.

## Notes

Scope this to the tools that actually reproduced the issue (`compile_check`, `test_run`) rather than every workspace-scoped tool, to keep the row within the size band — a broader sweep across all tool call sites can follow as a separate row if the pattern recurs elsewhere.
