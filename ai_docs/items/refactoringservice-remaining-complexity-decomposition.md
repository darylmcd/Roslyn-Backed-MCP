# refactoringservice-remaining-complexity-decomposition — Split remaining RefactoringService hotspots

**row:** `refactoringservice-remaining-complexity-decomposition` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs` (`RebaseModifiedSolutionOntoCurrentAsync`, `BuildRenameSummaryChangesAsync`, `BuildFileSnapshotsForDocumentSetChangesAsync`)
- `tests/RoslynMcp.Tests/RefactoringApplyFailureTests.cs`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] Extract cohesive collaborators or helpers so each named hotspot has cyclomatic complexity below 15 and fewer than 80 executable lines.
- [ ] Preserve stale-preview rebase, rename-summary, and file-snapshot behavior with focused regressions.

## Evidence

- Live Roslyn complexity metrics on 2026-07-26 reported complexity 21/17/16 and maintainability indices 33.92/41.52/37.97 after the persistence extraction.
