# persist-removed-linked-file-deletes-shared-path — Do not delete a path another project still links

**row:** `persist-removed-linked-file-deletes-shared-path` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:252` — PersistRemovedDocumentsAsync deletes the physical file whenever it exists (delete at :267).
- `tests/RoslynMcp.Tests/RebaseDocumentIdentityTests.cs:135`

## Acceptance

- [ ] Delete the physical file only when no other solution document references the path; otherwise remove only the document/project item.
- [ ] Red-first test: removing a linked document leaves the sibling project's copy readable on disk.
- [ ] Undo restores the removed document and file state.

## Evidence

- 2026-11-10 remediation (PRs #2055-#2057): PR #2055 end-to-end test Linked_Document_Removal_Apply_After_Sibling_Apply_Keeps_Other_Project_Copy_And_Undo_Restores asserts workspace state only, because the apply would delete the shared physical file.

## Notes

2026-11-25 bounded selection qualification at main0652f7fd: persistence deletes any removed document path, but the existing test manually constructs a PreviewStore removal and asserts workspace state; it does not establish real public membership-only intent. Public delete_file/move_file/namespace relocation/cross-project final-declaration move/remove_dead_code(removeEmptyFiles) request physical deletion. Native code actions accept arbitrary ChangedSolution, but no concrete provider/action producing membership-only linked removal was found. Fork/rebase repeats producer intent. Blanket retained-link protection would alter explicit delete_file semantics. No P0/I3 or unintended data-loss path is established. Hold the current acceptance pending a concrete public/native producer and raw preview/apply witness; preserve explicit physical-delete behavior. No runtime reproduction or test gate was run in this source-only qualification.
