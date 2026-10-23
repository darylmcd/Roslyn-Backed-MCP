# restore-active-assets-layout-authority — Use the evaluated active assets file

**row:** `restore-active-assets-layout-authority` · **pri:** `Medium` · **size:** `M` · **deps:** `restore-metadata-evaluation-global-context`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:468` — Default assets can shadow the evaluated active layout.
- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:27` — Resolved-version reader uses only the default layout.
- `tests/RoslynMcp.Tests/RestoreStalenessDetectorTests.cs`
- `tests/RoslynMcp.Tests/ProjectMetadataParserTryGetResolvedPackageVersionTests.cs`

- `tests/RoslynMcp.Tests/WorkspaceLoadRestorePackagesPathTests.cs` — Existing fixtures must create evaluable SDK projects before asserting active assets authority.

## Acceptance

- [ ] Root capture, resolved-version authorization, restore drift and missing-assets detection share active assets-path authority: an evaluated alternate file wins over a stale default, and missing active assets never fall back to that stale file.
- [ ] If evaluation cannot establish an active assets path, report unknown; do not authorize a default file merely because it exists. Use real evaluable SDK fixtures and preserve existing assertions.
- [ ] Qualify configured Release-default/Debug-alternate paths with both files present, then default, BaseIntermediateOutputPath, MSBuildProjectExtensionsPath and ProjectAssetsFile layouts; preserve platform path identity and safe unknown results for unreadable or malformed assets.
- [ ] Observe stale-default/missing-active regressions fail before repair; obtain independent review and scoped/hosted checks plus the serialized delivery gate before closure.

## Scope

- One locator contract; two production files, three test files and one fragment (six planned files).

## Evidence

- restore-complete-active-layout-red.log: 5 failed/1 passed on package-root, missing-active and drift assertions against the old default-first locator.
- restore-complete-callers-json-red.log also reproduced two alternate-layout parser failures and a stale default authorizing the wrong resolved version.
- References: ea969292 and the preserved restore branch/stash; no native-authority imports.

- Third-plan stop trigger: split missing evaluation context into workspace-reload-preserve-global-properties -> restore-metadata-evaluation-global-context -> this row before implementation. Cold review traced supported configured loading losing globals on reload and metadata evaluation; bare active-path priority would introduce incorrect configured authorization.
- No implementation has begun for this slice. Earlier RED receipts are historical forcing evidence; obtain current-head RED/GREEN after the context slices merge.
