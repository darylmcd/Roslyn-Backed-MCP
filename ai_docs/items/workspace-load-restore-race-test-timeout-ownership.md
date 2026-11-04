# workspace-load-restore-race-test-timeout-ownership — Settle restore race timeout

**row:** `workspace-load-restore-race-test-timeout-ownership` · **pri:** `Medium` · **size:** `S` · **deps:** `timing-test-guard-operation-ownership`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreRaceTests.cs:349`

## Acceptance

- [ ] A timeout cancels and observes the owned load/restore task before deleting its copied fixture root.
- [ ] A red-first held-restore regression proves no active task retains the root when cleanup begins.

## Evidence

- Same-mechanism sweep during the PR #1969 timeout-ownership repair found an unowned `WaitAsync` at `WorkspaceLoadRestoreRaceTests.cs:349`; fixture deletion can race continuing restore work.
