# workspace-restore-budget — Bound restore within load deadline

**row:** `workspace-restore-budget` · **pri:** `High` · **size:** `M` · **deps:** `workspace-restore-safe-execution`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:53-121`
- `src/RoslynMcp.Host.Stdio/Program.cs:240-260`
- (new) `tests/RoslynMcp.Tests/WorkspaceLoadRestoreBudgetTests.cs`
- `ai_docs/references/environment-variables.md`

## Acceptance

- [ ] A separately configured restore budget ends inside the remaining load/reload request budget and leaves time for reload.
- [ ] Document the default and environment override beside other `ROSLYNMCP_*` timeouts.
- [ ] Red-first fake-clock tests prove the restore budget does not let load cross the request deadline; caller cancellation still propagates.

## Evidence

- Parent `compile-check-restore-required-handshake`: `WorkspaceTools.cs:668-672` passes the full load-gate token to restore, leaving no reserve for reload.

## Context

- Prerequisite for default-on missing-assets restore; independent safety improvement for existing explicit restore.
