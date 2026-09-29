# workspace-restore-safe-execution — Serialize restore with build and test

**row:** `workspace-restore-safe-execution` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:640-680`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:73-84`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreRaceTests.cs`

## Acceptance

- [ ] The load/reload restore helper takes the per-workspace command gate used by build/test, so restore cannot write `obj/` concurrently with either command.
- [ ] Time waiting for that gate counts against the caller's restore budget.
- [ ] A red-first fake-runner test holds the command gate while load requests restore and proves no overlap.

## Evidence

- Parent `compile-check-restore-required-handshake`: `WorkspaceTools.cs:668-672` currently calls the command runner directly with the load gate token and server environment, outside `GatedCommandExecutor`.

## Context

- Standalone concurrency fix for existing explicit `autoRestore:true`; the default-on slice depends on it.
