# undo-solution-diff-skips-additional-documents — Solution-diff undo ignores additional and analyzer-config document changes

**row:** `undo-solution-diff-skips-additional-documents` · **pri:** `Medium` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:898`
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] A solution-snapshot apply that changes an additional or analyzer-config document is restored by revert_last_apply / revert_apply_by_sequence, or the revert refuses (never reports success without restoring).
- [ ] Red-first test with an apply touching an additional document.

## Evidence

- undo-a report bad-code sketch 1: CollectChangesFromSolutionDiffAsync loops `projectChange.GetChangedDocuments()` only (UndoService.cs:898); the sibling GetChangedAdditionalDocuments/GetChangedAnalyzerConfigDocuments are never consulted. Different mechanism from the closed sibling-document rows (PR 2140).
