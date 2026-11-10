# restore-drift-inactive-package-reference-conditions — Compare active evaluated package expectations

**row:** `restore-drift-inactive-package-reference-conditions` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:287` — CollectExpectedPackages through329; central raw-version expectations at371 onward.
- `src/RoslynMcp.Roslyn/Helpers/MsBuildMetadataHelper.cs` — existing explicit-operation-global native evaluation seam.
- `tests/RoslynMcp.Tests/RestoreStalenessDetectorTests.cs`

## Acceptance

- [ ] Derive expected PackageReference items and central requirements from actual active evaluation under operation globals, including item/group/import conditions.
- [ ] Observe RED when an inactive package falsely requires restore; cover the opposite configuration, active version drift and imported/central conditions as controls.
- [ ] Preserve active references, version updates and sanitized unreadable/malformed metadata classification. Cover every same-mechanism raw expectation reader without an independent condition interpreter.
- [ ] Complete fresh review, affected and hosted qualification before closure; split only into independently correct rows if needed.

## Evidence

- Main58ba4207 source enumerates raw XML Descendants(PackageReference), Include/Update and versions without evaluating conditions. The false-restore consequence is inferred, not runtime-reproduced.
- Landed metadata-global propagation does not evaluate raw package expectations; this is a distinct mechanism.

## Context

- Independent nonblocking remainder; no dependency from current restore closure. Re-derive use of the existing evaluator rather than adding native-authority machinery.

## Notes

Remediation run 2026-11-10: premise live (RestoreStalenessDetector.cs:288-329 reads raw XML from only Directory.Build.props, the project and Directory.Build.targets via EnumeratePackageReferenceDocuments :331-352; LoadCentralPackageVersions :371-396; HasMissingAssets caller :507 passes DefaultGlobalProperties). Cold plan review BLOCKING: evaluating under workspace.Properties (no TargetFramework) evaluates only the OUTER build of a multi-target project, so TFM-conditioned packages vanish and a missing or drifted one no longer triggers restore (under-reporting, worse than today over-reporting); a complete fix evaluates each inner TFM (split TargetFrameworks, set the TargetFramework global per evaluation, union the items), the same seam as msbuild-evaluation-project-context-cache. Also: the existing test HasMissingAssets_UnevaluableProject_DoesNotThrowAndTreatsAssetsAsAbsent (RestoreStalenessDetectorTests.cs:195-211) flips because an unevaluable project becomes "unknown" so default autoRestore stops firing (needs an explicit decision); implicit SDK PackageReferences appear in evaluated items; share one evaluation with GetExistingAssetsPath (ProjectMetadataParser.cs:86). Add a multi-target red test. Bad code: EnumeratePackageReferenceDocuments hardcodes three files and a hand-rolled FindNearestFile duplicates MsBuildMetadataHelper.FindNearestFile.
