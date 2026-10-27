# central-package-query-evaluated-policy — Read central package versions from the active evaluated context

**row:** `central-package-query-evaluated-policy` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/MsBuildMetadataHelper.cs:73`
- `src/RoslynMcp.Roslyn/Helpers/MsBuildMetadataHelper.cs:115`
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs:117`

## Acceptance

- [ ] Resolve effective CPM policy and central versions from evaluated imports/items/properties under the actual loaded context.
- [ ] Preserve explicit XML inspection helpers only for syntax contracts; compare conditional/imported/expanded/version-override cases and separately configured sessions.
- [ ] Reuse the existing context/cache owner rather than importing the retained native certificate graph. Observe public query old-red/new-green with actual compilation/evaluation premises.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Semantic references show GetNuGetDependenciesDetailedAsync directly consumes TryGetCentralPackageVersion, whose implementation reads nearest raw Directory.Packages.props XML. Imported/conditional/property-expanded effective versions can disagree.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
