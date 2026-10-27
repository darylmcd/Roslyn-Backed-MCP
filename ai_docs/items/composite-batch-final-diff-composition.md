# composite-batch-final-diff-composition — Review the complete initial-to-final composite delta

**row:** `composite-batch-final-diff-composition` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:80`
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:106`
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:139`

## Acceptance

- [ ] Render each changed physical file from the original snapshot to the final accumulator, using the same intended mutations as publication.
- [ ] Test two edits in one file, an edit later reverted, distinct files and linked documents; preserve caps, descriptions and preview-only behavior.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. aggregatedDiffs[path]=change overwrites prior step review, while BuildCompositeMutationsAsync uses initialSolution and final accumulator. The mismatch is independent of diff whitespace omission and truncation-state loss.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
