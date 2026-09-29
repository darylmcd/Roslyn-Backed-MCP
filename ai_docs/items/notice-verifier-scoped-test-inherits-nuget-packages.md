# notice-verifier-scoped-test-inherits-nuget-packages — Isolate scoped notice fixture from ambient package root

**row:** `notice-verifier-scoped-test-inherits-nuget-packages` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ThirdPartyNoticeDriftTests.cs:90-136`
- `tests/RoslynMcp.Tests/ThirdPartyNoticeDriftTests.cs:199-232`
- `eng/update-third-party-notices.ps1:97-116`

## Acceptance

- [ ] The scoped solution test supplies its fixture package root as `NUGET_PACKAGES` to the child verifier process, and other verifier fixture calls clear the inherited variable.
- [ ] With an ambient `NUGET_PACKAGES` pointing outside the fixture, all `ThirdPartyNoticeDriftTests` pass without modifying process-wide environment state.

## Evidence

- 2026-09-29: `just ci` on PR #1672 failed one test (`VerifyMode_WithSolutionPath_IgnoresStaleAssetGraphFromForeignPackageRoot`, 3267 passed, 12 skipped). The same test failed on unchanged main with `NUGET_PACKAGES=C:/Code-Repo/.caches/nuget`; it passed on main with that variable removed. The script intentionally filters scoped asset graphs to the active package root, while the test fixture constructs a distinct `current-packages` root.

## Context

- Discovered while validating the volume-mount junction fix. The test harness must isolate its child process environment; changing the production verifier would weaken its restored-graph check.
