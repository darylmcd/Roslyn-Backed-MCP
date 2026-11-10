# undo-solution-snapshot-restore-incomplete — Restore or refuse added/removed documents and parse options in solution-snapshot undo

**row:** `undo-solution-snapshot-restore-incomplete` · **pri:** `Medium` · **size:** `S` · **verified:** `e220816e3fa161caca44362acb8c70e9390e73bf`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:523` — CollectChangesFromSolutionDiffAsync restores only changed documents' text and SourceCodeKind.
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] Enumerate what solution-snapshot undo does not restore: documents added or removed by the apply, project-level ParseOptions.
- [ ] For each, restore it or explicitly refuse; red-first tests.
- [ ] Account for PersistRevertChangesAsync reloading from MSBuild afterwards, which discards in-memory SourceCodeKind.

## Evidence

- 2026-11-10 remediation (PRs #2055-#2057): PR #2056 added SourceCodeKind restore for changed documents only; the rest of the solution-diff surface is unrestored.
