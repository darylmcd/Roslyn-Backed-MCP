# worktree-teardown-windows-lock-multi-drain — workspace_close drain must cover testhost processes

**row:** `worktree-teardown-windows-lock-multi-drain` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:100` (`workspace_close`)
- workspace-close/drain logic under `src/RoslynMcp.Roslyn/Services/`

## Acceptance

- [ ] `workspace_close(drainProcesses=true)` also terminates detached `testhost.exe`/`vstest.console` processes spawned by `test_run` (or polls-until-released with a bounded retry)
- [ ] Documented that test-running in a worktree needs this stronger drain before removal
- [ ] Regression: harness asserts a post-`test_run` `workspace_close(drainProcesses=true)` leaves no lock on the test bin dir

## Evidence

- `audit-reports/20260531T192823Z_roslyn-backed-mcp_mcp-server-surface-test.md` teardown sub-phase 6z, server v2.3.1. Source: 2026-05-31 surface-test.

## Context

The documented one-shot Windows teardown — `workspace_close(drainProcesses=true)` (runs `dotnet build-server shutdown`) then `git worktree remove` — was insufficient to release the disposable worktree's `tests/RoslynMcp.Tests/bin/Debug/net10.0` after a Phase-8 `build_workspace`/`test_run`: `git worktree remove` failed `Invalid argument` and `rm -rf` failed `Device or resource busy` repeatedly, clearing only after several extra `dotnet build-server shutdown` drains plus a lingering-`testhost.exe` kill.
