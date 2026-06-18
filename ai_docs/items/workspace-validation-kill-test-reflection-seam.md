# workspace-validation-kill-test-reflection-seam — Drop reflection in WorkspaceValidationService kill-failure test

**row:** `workspace-validation-kill-test-reflection-seam` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ValidateRecentGitChangesTests.cs` — the kill-failure observability test that invokes the private `TryKillProcessTree` via `BindingFlags.NonPublic` reflection

## Acceptance

- [ ] The kill-failure observability test exercises the Warning-log path without reflecting into a private method name.
- [ ] A rename of the helper produces a compile error, not a runtime `Invoke` failure (drive via the injected `killProcessTree` seam, or expose the helper `internal` with `InternalsVisibleTo`).

## Evidence

- Code-quality review of PR #968 (`workspace-validation-process-kill-failure-observability`) flagged the regression test uses `typeof(WorkspaceValidationService).GetMethod("TryKillProcessTree", BindingFlags.Instance | BindingFlags.NonPublic)`, coupling the test to a private method name. — 2026-06-18 backlog-sweep execute.

## Context

The shipped PR added an injectable `Action<Process>? killProcessTree` test seam on the internal constructor; the test should drive the failure path through that seam (compile-checked) rather than reflecting into the private helper.
