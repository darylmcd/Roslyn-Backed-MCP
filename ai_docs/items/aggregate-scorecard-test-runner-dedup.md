# aggregate-scorecard-test-runner-dedup — dedup the pwsh-runner helpers in AggregatePromotionScorecardsScriptTests

**row:** `aggregate-scorecard-test-runner-dedup` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Skills/AggregatePromotionScorecardsScriptTests.cs` — `RunAggregator` (~line 408) and `RunAggregatorWithIncludeSelf` (~line 452)

## Acceptance

- [ ] A single parameterized pwsh-runner helper (e.g. `RunAggregator(params string[] extraArgs)` or an `includeSelf` overload) backs both the no-arg call sites and the `-IncludeSelf` test; the duplicated `ProcessStartInfo` / read-stdout-stderr / 60s-timeout / `Kill` block exists exactly once.
- [ ] All existing `AggregatePromotionScorecardsScriptTests` still pass unchanged.

## Evidence

- Code-quality review of `aggregate-scorecard-includeself-double-count` (2026-06-21 top-n-remediation): `RunAggregatorWithIncludeSelf` is a ~30-line near-verbatim copy of `RunAggregator` (same `ProcessStartInfo` + read + 60s timeout + kill), differing only by appending `-IncludeSelf`.

## Context

Test-helper duplication introduced when the `-IncludeSelf` regression test was added. Pure refactor — no behavior change. Low priority (cosmetic test hygiene); fold into the next edit of this test file.
