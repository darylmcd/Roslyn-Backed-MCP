# verify-release-stale-package-cache-asset-graph — Isolate release verification from stale package-cache asset graphs

**row:** `verify-release-stale-package-cache-asset-graph` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/verify-release.ps1` — package-metadata validation and restore ownership.
- `tests/RoslynMcp.Tests/VerifyReleaseChildScriptTests.cs` — release-verifier child-process contract coverage.

## Acceptance

- Run sequential `just ci` gates with different external `NUGET_PACKAGES` roots without manually deleting generated `obj` trees.
- Make package-metadata validation consume only the current restored asset graph, or restore every graph it inspects against the active cache.
- Add a regression that leaves prior-cache assets in place and proves the next-cache gate does not report ambiguous nuspec paths.

## Evidence

2026-09-20 `agents-md-missing-validation-runtime` fix cycle: the first source-equivalent gate failed before build/test because generated assets referenced both the prior and current external caches for `Microsoft.NET.Test.Sdk/18.10.0`. Removing only verified generated `bin`, `obj`, and `artifacts` paths made the unchanged-source retry pass.
