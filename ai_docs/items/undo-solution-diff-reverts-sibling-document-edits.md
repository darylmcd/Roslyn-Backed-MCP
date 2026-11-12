# undo-solution-diff-reverts-sibling-document-edits — Limit solution-snapshot undo to the documents this apply changed

**row:** `undo-solution-diff-reverts-sibling-document-edits` · **pri:** `Medium` · **size:** `S` · **deps:** `undo-solution-snapshot-restore-incomplete`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:523` — CollectChangesFromSolutionDiffAsync diffs the pre-apply solution against the whole current solution and reverts every changed document, including later siblings' edits to other files.
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] A by-sequence revert of apply A, after a file-disjoint apply B, restores only A's documents and leaves B's edits intact (red-first).
- [ ] No public contract change.

## Evidence

- Cold plan review (2026-11-12) of `undo-solution-snapshot-restore-incomplete`: same defect at document level; the dependency check only blocks overlapping files. Reach is limited because production apply paths pass file snapshots.

## Context

- Same mechanism as `undo-solution-snapshot-restore-incomplete`; chained behind it.
