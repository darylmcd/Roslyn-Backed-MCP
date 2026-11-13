# undo-disk-walk-fallback-restores-drifted-sibling-docs — Stop the undo disk-walk fallback from restoring drifted sibling documents and reporting no-op success

**row:** `undo-disk-walk-fallback-restores-drifted-sibling-docs` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:715` — `CollectChangesFromSolutionDiffAsync` diffs against the whole current solution.

## Acceptance

- [ ] A fallback revert never rewrites a document the apply did not change, and an empty restore is not reported as success (red-first).

## Evidence

- Raised by the cold implementation review of PR 2082 (2026-11-12). Related: `undo-solution-diff-reverts-sibling-document-edits`.

## Context

- Check overlap with `undo-solution-diff-reverts-sibling-document-edits` before starting; merge the two if it is the same fix.
