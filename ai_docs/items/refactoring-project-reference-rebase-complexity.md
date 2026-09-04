# refactoring-project-reference-rebase-complexity — Decompose project-reference rebasing

**row:** `refactoring-project-reference-rebase-complexity` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs` (`RebaseProjectReferences`)
- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs`

## Acceptance

- [ ] `RebaseProjectReferences` measures cyclomatic complexity below 10.
- [ ] Added-reference metadata, duplicate suppression, removed-reference matching, path-based project rebasing, and missing-project no-op behavior remain intact.

## Regression

Rebase a stale preview containing one added and one removed project reference over an unrelated current-solution change; both reference changes land and the unrelated change survives.
