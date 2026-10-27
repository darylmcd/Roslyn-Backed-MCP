# refactoring-rebase-source-kind-preservation — Preserve reviewed document source kind during replay

**row:** `refactoring-rebase-source-kind-preservation` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:895`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:927`
- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:165`

## Acceptance

- [ ] Preserve reviewed source kind across same-coordinate rebase and full-document reconstruction; enumerate all clone instances.
- [ ] Observe sibling-rebase and document clone old-red/new-green; retain text/encoding, linked/project identity and additions.
- [ ] Prove supported native Script behavior before making native claims; do not import unapproved compiler-certificate/comparator architecture.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. RebaseChangedDocuments updates only text; RebaseAddedDocuments reconstructs without sourceCodeKind. Retained finding includes proposal-only comparators, which remain with the architectural decision owner.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
