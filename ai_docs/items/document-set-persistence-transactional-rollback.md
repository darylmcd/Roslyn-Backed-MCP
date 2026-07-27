# document-set-persistence-transactional-rollback — Make document-set persistence transactional

**row:** `document-set-persistence-transactional-rollback` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs` (`PersistAsync`, document/project-reference writes)
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs` (`ApplyRefactoringAsync`)
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Snapshot every file that `DocumentSetPersistenceService` may add, change, or remove before the first write.
- [ ] When persistence or `TryApplyChanges` fails, restore all prior bytes and remove newly created files before returning failure.
- [ ] Add a deterministic mid-batch failure regression proving no partial file or project-reference mutation remains.

## Evidence

- The 2026-07-26 ten-row remediation review found that `PersistAsync` catches persistence failures after earlier writes may already have landed, then returns `(false, [])` without compensating those writes.
