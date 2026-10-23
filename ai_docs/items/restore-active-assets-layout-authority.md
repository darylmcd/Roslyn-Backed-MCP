# restore-active-assets-layout-authority — Use the evaluated active assets file

**row:** `restore-active-assets-layout-authority` · **pri:** `Medium` · **size:** `M` · **deps:** `restore-assets-json-kind-guards`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:468` — Default assets can shadow the evaluated active layout.
- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:27` — Resolved-version reader uses only the default layout.
- `tests/RoslynMcp.Tests/RestoreStalenessDetectorTests.cs`
- `tests/RoslynMcp.Tests/ProjectMetadataParserTryGetResolvedPackageVersionTests.cs`

## Acceptance

- [ ] Root capture, resolved-version authorization, restore drift and missing-assets detection share active assets-path authority: an evaluated alternate file wins over a stale default, and missing active assets never fall back to that stale file.
- [ ] Qualify default, BaseIntermediateOutputPath, MSBuildProjectExtensionsPath and ProjectAssetsFile layouts; preserve platform path identity and safe unknown results for unreadable or malformed assets.
- [ ] Observe stale-default/missing-active regressions fail before repair; obtain independent review and scoped/hosted checks plus the serialized delivery gate before closure.

## Scope

- One locator contract; two production files, two test files and one fragment.

## Evidence

- restore-complete-active-layout-red.log: 5 failed/1 passed on package-root, missing-active and drift assertions against the old default-first locator.
- restore-complete-callers-json-red.log also reproduced two alternate-layout parser failures and a stale default authorizing the wrong resolved version.
- References: ea969292 and the preserved restore branch/stash; no native-authority imports.
