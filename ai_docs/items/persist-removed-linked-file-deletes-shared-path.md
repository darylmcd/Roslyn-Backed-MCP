# persist-removed-linked-file-deletes-shared-path — Do not delete a path another project still links

**row:** `persist-removed-linked-file-deletes-shared-path` · **pri:** `Medium` · **size:** `S` · **verified:** `e220816e3fa161caca44362acb8c70e9390e73bf`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:252` — PersistRemovedDocumentsAsync deletes the physical file whenever it exists (delete at :267).
- `tests/RoslynMcp.Tests/RebaseDocumentIdentityTests.cs:135`

## Acceptance

- [ ] Delete the physical file only when no other solution document references the path; otherwise remove only the document/project item.
- [ ] Red-first test: removing a linked document leaves the sibling project's copy readable on disk.
- [ ] Undo restores the removed document and file state.

## Evidence

- 2026-11-10 remediation (PRs #2055-#2057): PR #2055 end-to-end test Linked_Document_Removal_Apply_After_Sibling_Apply_Keeps_Other_Project_Copy_And_Undo_Restores asserts workspace state only, because the apply would delete the shared physical file.
