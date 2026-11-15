# ci-restore-linux-leg-timeout-30-after-rebalance — Restore the 30 minute cap on hosted Linux shard legs

**row:** `ci-restore-linux-leg-timeout-30-after-rebalance` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/resolve-ci-topology.ps1:84-95` — four code and four docs-only Linux shards use 45-minute caps. The code topology has a temporary-cap comment.
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs:385-405` — both groups pin all Linux shard caps to 45.
- `CHANGELOG.md:71-72` — v5.1.0 published the temporary timeouts. Add a reverting fragment when the caps change.

## Acceptance

- [ ] Restore 30-minute caps for all four code and all four docs-only Linux shard legs. Update the pinned contract expectations to 30 and remove the temporary-cap comments.
- [ ] Demonstrate that the first full code and docs-only PR runs after the change keep every Linux leg under the cap with margin.

## Evidence

- PR 2089 raised the code cap to 45 on operator approval (2026-11-13) after linux-2 hit 30 minutes with all tests passing. PR 2090 aligned the docs-only cap.
- Release v5.1.0 consumed both original fragments.

## Context

- The profile rebalance prerequisite shipped. Qualify the new 30-minute cap independently on code and policy-doc routes.

## Notes

PR #2097 merged as `d3802cc5aefe19aa55597f8e52a329e381186ede`. Hosted run [`43774416634`](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/43774416634) passed `validate`. Linux jobs finished in 21:19, 20:39, 21:13, 21:37, a 0:58 spread.
