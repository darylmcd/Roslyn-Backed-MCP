# solution-diff-truncation-source-substring-test — Assert truncation behavior instead of source wording

**row:** `solution-diff-truncation-source-substring-test` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/P4BehavioralBundleTests.cs:234`
- `tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs`

## Acceptance

- [ ] Replace the source-file substring probe with behavioral coverage of the omitted-file truncation marker, reusing the existing SolutionDiffHelper test fixture.
- [ ] Assert omitted paths/count and returned-versus-omitted accounting from real changed solution snapshots; observe a regression fail against a broken marker implementation.
- [ ] Remove duplicate assertions that only test implementation spelling; retain meaningful public output coverage.

## Evidence

- Read-only source inspection on 2026-10-16: P4BehavioralBundleTests.SolutionDiffHelper_TruncationMarker_IncludesFilePaths reads SolutionDiffHelper.cs and searches for the literal phrase "Omitted file(s):". It can pass while runtime truncation is broken.
- SolutionDiffHelperTests.Truncation_LastEntryHasTruncatedFilePath already constructs a real solution exceeding the cap and is the natural shared behavioral test location.
- No runtime claim. Separate test-quality mechanism from the authorized diff-reader security migration.
