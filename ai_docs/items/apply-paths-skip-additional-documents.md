# apply-paths-skip-additional-documents — Cover additional and analyzer-config documents in the apply/persist producers

**row:** `apply-paths-skip-additional-documents` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs`
- `src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs`
- `src/RoslynMcp.Roslyn/Services/CompositeSolutionMutationPlanner.cs`

## Acceptance

- [ ] A reviewed edit to an additional document or analyzer-config document survives preview, rebase and apply (or is refused), per producer, with a red-first test each.
- [ ] The analyzer-config case honours that `Workspace.TryApplyChanges` throws NotSupportedException for analyzer-config changes (probe in the undo-solution-diff-skips-additional-documents plan): disk write plus reload, kept out of the applied solution.
- [ ] Other producers iterating GetChangedDocuments only (PreviewStore.cs:~220, WorkspaceForkApplyService.cs:~862) are checked and covered or listed as not reachable.

## Evidence

- Undo PR (#2181) plan review: no shipped tool produces such changes in a snapshot today; this row makes the producers consistent. Same family as undo-solution-diff-skips-additional-documents (closed).
