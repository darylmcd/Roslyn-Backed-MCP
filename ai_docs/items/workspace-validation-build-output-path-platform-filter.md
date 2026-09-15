# workspace-validation-build-output-path-platform-filter

**row:** `workspace-validation-build-output-path-platform-filter` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs` — IsBuildOutputPath replaces literal backslashes and compares bin/obj case-insensitively on every OS.
- `tests/RoslynMcp.Tests/ValidateRecentGitChangesTests.cs` — Git scope integration fixtures.

## Acceptance

- Match actual bin/obj path segments using the repository platform policy and platform separators.
- On case-sensitive filesystems preserve source paths under Bin/Obj and names containing a literal backslash; retain normal bin/obj exclusion and Windows behavior.
- Add one platform path-filter regression matrix; explicitly skip unsupported filesystem cases.

## Evidence

2026-09-15 source review: IsBuildOutputPath can drop legitimate case-distinct source directories before ResolveChangedFiles receives them. This parser filtering is separate from the selected scope membership/deduplication fix.
