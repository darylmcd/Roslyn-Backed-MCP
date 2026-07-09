# refactoringservice-persistence-extraction — Extract RefactoringService's document-set persistence logic

**row:** `refactoringservice-persistence-extraction` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:936-1147` (`PersistDocumentSetChangesAsync`, `CreateDocumentSetPersistenceStateAsync`, `PersistAddedDocumentsAsync`, `PersistChangedDocumentsAsync`, `PersistRemovedDocuments`)

## Acceptance

- [ ] Move the ~210-line document-set persistence block into a standalone type
- [ ] Wire it for reuse by `EditService`/`ProjectMutationService` if/when those services need the same persistence shape (confirm actual reuse need before generalizing the API)
- [ ] No behavior change to `RefactoringService`'s existing apply/persist paths

## Evidence

- Follow-on from `refactoringservice-god-class-decomposition` (PR #1039): the acceptance criterion's original premise that this block is "reused elsewhere" was found false (grep confirmed the method names exist only in `RefactoringService.cs`) — extracting it into a shared type is a genuinely larger effort (new type + wiring into 2+ services + re-verifying snapshot/rollback semantics) than that initiative's `size: S` budget supported, so it was explicitly scoped out and tracked here instead.
