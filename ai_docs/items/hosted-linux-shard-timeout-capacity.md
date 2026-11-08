# hosted-linux-shard-timeout-capacity — Restore Linux shard deadline margin

**row:** `hosted-linux-shard-timeout-capacity` · **pri:** `High` · **size:** `S`

## Anchors

- `eng/resolve-ci-topology.ps1:84`
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs:376`
- `CI_POLICY.md:180`

## Acceptance

- [ ] Code and policy-doc PR Linux legs each partition the complete test class set disjointly, with one artifact owner and the existing 30-minute job cap.
- [ ] Verify routing contract tests and affected local validation; confirm hosted code and docs routes complete without timeout cancellation.
- [ ] Retain the complete suite, release checks, version gates, and fail-closed aggregate result.

## Evidence

- PR #2032 run 42490458875 docs-linux-2-of-3 started 14:34:29 UTC; its tests passed 2297/2297 (35 skipped) after 28m18s at 15:04:41, then the 30-minute job cap cancelled post-test work. The aggregate `validate` failed because the matrix was cancelled.
- PR #2027 run 42487026249 linux-2-of-3 passed but consumed 29m25s of the 30-minute cap (13:52:51–14:22:16 UTC). Both routes share the three-way Linux class partition.

## Context

- The 2026-10-26 three-shard decision in `CI_POLICY.md` addressed an earlier two-shard capacity failure. New measured runs show that three shards no longer leave sufficient margin. Preserve the 30-minute deadline by distributing the complete class partition over four shards.
