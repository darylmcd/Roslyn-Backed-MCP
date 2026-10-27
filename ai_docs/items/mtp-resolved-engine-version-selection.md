# mtp-resolved-engine-version-selection — Gate MTP filters on every applicable resolved engine

**row:** `mtp-resolved-engine-version-selection` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:19`
- `src/RoslynMcp.Roslyn/Helpers/TreeNodeFilterTranslator.cs:95`

## Acceptance

- [ ] Resolve the actual applicable target/RID package versions from evaluated active assets and compare NuGet semantic versions without choosing the first libraries entry.
- [ ] Permit multi-method filters only when every applicable TUnit.Engine version meets the existing fixed-version threshold; preserve valid single-method and missing/ambiguous-state controls.
- [ ] Observe old-red/new-green for prerelease and distinct target-version selection, without raising the threshold or changing caller defaults.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. TryGetResolvedPackageVersion returns the first libraries entry parsed as System.Version; TreeNodeFilterTranslator accepts one Version. Retained branch changes both to exact target selection and NuGetVersion lists.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
