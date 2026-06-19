# aggregate-scorecard-includeself-double-count — -IncludeSelf double-counts the hub repo

**row:** `aggregate-scorecard-includeself-double-count` · **pri:** `Low` · **size:** `S`

## Anchors

- `eng/aggregate-promotion-scorecards.ps1` — the `-IncludeSelf` handling and the sibling-discovery loop (`siblingReposScanned` / `siblingReposWithScorecard` counters)

## Acceptance

- [ ] With `-IncludeSelf`, `Roslyn-Backed-MCP` is counted exactly once in `siblingReposScanned` / `siblingReposWithScorecard` (and contributes exactly one vote to quorum math), not twice.
- [ ] A regression in `AggregatePromotionScorecardsScriptTests.cs` covers the `-IncludeSelf` + self-as-sibling case.

## Evidence

- Flagged during implementation of `aggregate-scorecard-stale-search-path` (2026-06-19 top-n-remediation): with `-IncludeSelf`, the hub repo is added by `-IncludeSelf` AND re-discovered as a sibling under the parent folder (no self-exclusion when `-IncludeSelf` is set), so it appears twice in the scan counters.

## Context

Latent — matters only when `-IncludeSelf` is used. Quorum verdicts key on "at least 2 sibling repos voted promote"; a double-counted hub could let a single hub vote satisfy the 2-vote quorum spuriously. Low today (the common aggregation path does not pass `-IncludeSelf`), but a real correctness risk for quorum math if it ever runs that way.
