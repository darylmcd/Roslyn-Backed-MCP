# test-inconclusive-count-unbudgeted — Nothing bounds the inconclusive test count

**row:** `test-inconclusive-count-unbudgeted` · **pri:** `Medium` · **size:** `M` · **verified:** `847d7f94a4f3565da05208010a1397da04f1047f`

## Anchors

- `eng/verify-release.ps1`
- `.github/workflows/ci.yml`
- `tests/RoslynMcp.Tests/Support/GitFixtureRunner.cs`

## Acceptance

- [ ] The release/CI test step reads the TRX counters and fails when the not-executed/inconclusive count exceeds a committed per-OS expectation (the OS-only guards are the expected set); the expectation lives in a file a PR must edit to raise it. (`eng/verify-release.ps1` reads `NotExecuted` only to prove each docs-tier consumer class ran; it carries no count budget.)
- [ ] The six fixture-or-product-shape inconclusives become failures or real assertions: `SymbolDisambiguationElicitationTests.cs:119` and `:492`, `Services/WorkspaceDriftServiceTests.cs:171`, `AnalysisToolsTests.cs:310` (its own comment says "fail loudly"), and the two remaining ones the classification finds.
- [x] `Support/GitFixtureRunner.cs` no longer turns every exception into not-run: `RequireAvailableAsync` (lines 44-55) now yields Inconclusive only for a Win32 "git not found" (native error 2 or 3); any other failure propagates.

## Evidence

- `rg -c "Assert\.Inconclusive\(" tests/RoslynMcp.Tests` → 98 (about 95 outside string literals); 0 unconditional; 0 cite a backlog row or issue.
- Guard mix (probe, all 95 read): OS 41, link capability 21, git missing 23, filesystem capability 4, fixture-or-product shape 6.
- `eng/verify-release.ps1` validates that a TRX exists and proves tests executed (:192-228) but applies no bound on not-executed outcomes; `.github/workflows/ci.yml` has no such check either (`rg -i "inconclusive|NotExecuted"` over both: no hit).
- `tests/RoslynMcp.Tests/Support/GitFixtureRunner.cs:52` — `catch (Exception ex)` converted to `false`, which feeds the 23 git inconclusives.
- Found by a read-only syntactic probe of `tests/RoslynMcp.Tests` at 6cf842a8 on 2026-10-09 (ast-grep plus a string/comment mask; no test executed); counts re-derived with `rg` at e52c1241 before filing.

## Context

- regression_shape: a skip path with no ceiling, so lost coverage is invisible.

## Notes

2026-11-20: re-derived at 847d7f94: GitFixtureRunner now only maps git-not-found to Inconclusive (bullet 3 done); no TRX inconclusive budget exists in verify-release.ps1; Assert.Inconclusive lines now 141 (was ~95).
