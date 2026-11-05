# preview-store-boilerplate-duplication — Diff, version, store, DTO tail is copied about 25 times

**row:** `preview-store-boilerplate-duplication` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`
- `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`

## Acceptance

- [ ] StoreCodeFixPreviewAsync is generalized with a PreviewKind parameter and the copies at RefactoringService ~92, 499, 519, 553 plus the Bulk and CrossProject copies use it; token and envelope behavior unchanged.

## Evidence

- PR 1991 cold review: the same diff/GetCurrentVersion/Store tail remains with other PreviewKind values.
