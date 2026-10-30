# rebase-changed-document-silent-drop — Warn when rebase drops a changed document

**row:** `rebase-changed-document-silent-drop` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs (RebaseChangedDocumentsAsync, the continue on a missing current document)`

## Acceptance

- [ ] A reviewed change whose document vanished from the current solution is refused or reported, never dropped silently; test through ApplyRefactoringAsync.

## Evidence

- Seen while planning PR 1908. A reviewed edit lost without a diagnostic is a correctness hazard for the preview/apply contract.
- Sighted during the 2026-10-29 remediation of refactoring-rebase-source-kind-preservation, cross-project-interface-reference-binding and automatic-reload-analyzer-reader-lifetime (PRs 1908-1910). Not re-derived beyond the cold reviews.
