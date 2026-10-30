# di-inversion-interface-namespace-folder-mismatch — Align DI-inversion interface namespace with its folder

**row:** `di-inversion-interface-namespace-folder-mismatch` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs (CreateInterfaceExtractionSolutionAsync vs PreviewExtractInterfaceAsync namespace derivation)`

## Acceptance

- [ ] DI-inversion and extract routes derive namespace and folder consistently, or the difference is a documented, tested contract.

## Evidence

- Found while adding the Interfaces-subfolder binding test in PR 1910 (pre-existing FLAG-10C inconsistency; that test asserts the cross-project and extract-and-wire routes only).
- Sighted during the 2026-10-29 remediation of refactoring-rebase-source-kind-preservation, cross-project-interface-reference-binding and automatic-reload-analyzer-reader-lifetime (PRs 1908-1910). Not re-derived beyond the cold reviews.
