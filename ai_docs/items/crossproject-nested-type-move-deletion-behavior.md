# crossproject-nested-type-move-deletion-behavior — Moving a nested type through cross-project routes keeps or deletes its outer type unpinned

**row:** `crossproject-nested-type-move-deletion-behavior` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Moving a nested type is either refused with a typed correction or supported; the outer type is never deleted or orphaned; red regression first; changelog states the behavior.

## Evidence

- PR 1992 cold review: CrossProjectRefactoringService.cs ~372; the old predicate deleted the whole file including Outer, the new one keeps Outer with no test row or changelog mention.
