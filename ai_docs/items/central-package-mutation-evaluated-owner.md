# central-package-mutation-evaluated-owner — Bind central-package edits to their effective authored owner

**row:** `central-package-mutation-evaluated-owner` · **pri:** `Medium` · **size:** `M` · **deps:** `central-package-query-evaluated-policy`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:58`
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:707`
- `src/RoslynMcp.Roslyn/Services/PackageMigrationOrchestrator.cs:29`

## Acceptance

- [ ] Use active evaluated CPM policy to choose mutation behavior and bind edits to the actual authored owning declaration.
- [ ] Preserve conditional/imported policy and safe concrete pre-effect refusal for unsupported ownership; never silently edit the nearest raw file instead.
- [ ] Cover add/update/migration paths sharing the decision mechanism with real preview/apply or no-effects refusal controls; reuse the query evaluation seam.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Semantic references show these mutation entry points call raw IsCentralPackageManagementEnabled. Existing restore-drift-inactive-package-reference-conditions covers restore expectations, not these mutation consumers.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
