# refactoring-apply-orchestration-decomposition — Decompose refactoring apply orchestration

**row:** `refactoring-apply-orchestration-decomposition` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs` (`ApplyRefactoringAsync`)
- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs`
- `tests/RoslynMcp.Tests/RefactoringApplyFailureTests.cs`

## Acceptance

- [ ] Separate preview validation, stale-solution rebase, undo capture, persistence, and post-apply bookkeeping into named helpers.
- [ ] Reduce `ApplyRefactoringAsync` below 80 executable lines and cyclomatic complexity 9.
- [ ] Preserve token invalidation, failure rollback, file-set persistence, and cross-lineage project-reference behavior.

## Evidence

- Live Roslyn metrics during the 2026-07-27 ten-row cold review measured complexity 10 and 97 lines after the three previously named RefactoringService hotspots were decomposed.
