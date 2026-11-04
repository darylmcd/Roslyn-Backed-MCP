# document-set-persistence-compensation-residuals — Close the residual persistence compensation gaps

**row:** `document-set-persistence-compensation-residuals` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs (AppliedFiles seeded with planned csproj paths ~119; persist writes ~228 and ~248; rollback CancellationToken.None; created parent directories)`
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] AppliedFiles lists only paths actually written.
- [ ] Persist writes use the atomic writer used elsewhere, or the gap is justified with evidence.
- [ ] Directories created for added documents are removed on rollback when empty and owned.
- [ ] Ownership verification retries a bounded number of times on transient sharing violations before marking a path foreign, and does not re-read every captured path before every effect group when nothing could have changed (measure the I/O first).
- [ ] Rollback honors a caller cancellation policy or documents why it must not.

## Evidence

- Each item was observed by the PR 1963 implementer or cold reviewers; none was introduced by that PR.
