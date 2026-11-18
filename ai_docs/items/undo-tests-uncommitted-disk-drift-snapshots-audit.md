# undo-tests-uncommitted-disk-drift-snapshots-audit — Audit undo tests that encode uncommitted disk-only drift

**row:** `undo-tests-uncommitted-disk-drift-snapshots-audit` · **pri:** `Low` · **size:** `S` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `tests/RoslynMcp.Tests/UndoServiceTests.cs`
- `tests/RoslynMcp.Tests/EditUndoCohesionTests.cs`

## Acceptance

- [ ] Every test that captures an uncommitted (sequence 0) solution snapshot and then drifts disk only is listed; each is realigned to commit the capture with its affected files or rewritten to assert the documented uncommitted behaviour.

## Evidence

- undo-a report (PR 2140), sketch 6: "Tests that built uncommitted solution snapshots with disk-only drift (two found and realigned) encoded the unattributable shape; other test helpers may share it." Realigned: `Revert_SolutionSnapshot_AfterReload_IdenticalProjectsWithNewIds_DoesNotRefuse`, `RevertAsync_SolutionSnapshotPath_DiskWalk_PreservesOriginalEncoding`.
