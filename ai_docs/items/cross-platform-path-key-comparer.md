# cross-platform-path-key-comparer — Make persisted path keys platform-correct

**row:** `cross-platform-path-key-comparer` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`
- `src/RoslynMcp.Roslyn/Helpers/CsprojSemanticEquality.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs`
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Path-keyed snapshots, document indexes, fork locks, and applied-file sets use case-insensitive equality only on Windows.
- [ ] Case-distinct files remain independently tracked on case-sensitive platforms.
- [ ] Add platform-conditional regressions covering transaction rollback and cross-lineage rebase path keys.

## Evidence

- The 2026-07-27 ten-row cold review found multiple touched persistence/refactoring dictionaries hard-coded to `StringComparer.OrdinalIgnoreCase`, which can merge distinct paths on Linux.
