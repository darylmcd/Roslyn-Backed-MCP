# preview-rebase-linked-document-project-identity — Preserve project ownership when rebasing removals

**row:** `preview-rebase-linked-document-project-identity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:966`
- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs`

## Acceptance

- [ ] Resolve the original removed document's project/context before matching its physical path in the current solution.
- [ ] Preserve linked sibling documents and unrelated sibling edits across preview-token rebase; avoid first-path-match removal across every project.
- [ ] Add a linked-document preview plus sibling apply plus removal/undo regression; observe the wrong-context removal on old code before repair.
- [ ] Obtain fresh independent review and required local/hosted checks before verified merge and closure.

## Evidence

- Independent renderer delta review observed RebaseRemovedDocuments selecting the first document with a matching path across all projects, losing original project/context ownership.
- This is a pre-existing different mechanism from project-reference metadata replacement; record it separately and do not expand the held restore drafts.
