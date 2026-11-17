# ci-restore-linux-leg-timeout-30-after-rebalance — Restore the 30 minute cap on hosted Linux shard legs

**row:** `ci-restore-linux-leg-timeout-30-after-rebalance` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/resolve-ci-topology.ps1:89-101` — five code Linux legs (`linux-N-of-5`, lines 91-95) and four docs-only Linux legs (`docs-linux-N-of-4`, lines 98-101) use 45-minute caps. The temporary-cap comment is at lines 89-90. The six Windows code legs (lines 83-88) are also 45 and are not part of this row.
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs:387-409` — both groups pin all Linux shard caps to 45 (code legs 388-392, docs legs 406-409; temporary-cap comment at 387).
- `CHANGELOG.md:71-72` — v5.1.0 published the temporary timeouts. Add a reverting fragment when the caps change.

## Acceptance

- [ ] Restore 30-minute caps for all five code and all four docs-only Linux shard legs. Update the pinned contract expectations to 30 and remove the temporary-cap comments.
- [ ] Demonstrate that the first full code and docs-only PR runs after the change keep every Linux leg under the cap with margin.

## Evidence

- PR 2089 raised the code cap to 45 on operator approval (2026-11-13) after linux-2 hit 30 minutes with all tests passing. PR 2090 aligned the docs-only cap.
- Release v5.1.0 consumed both original fragments.
- PR 2135 (2026-11-17) changed the code route from four to five Linux legs and left every cap at 45. Its first run 44176184128 (head a6ac242a, first attempt, green): code Linux legs took 861 / 845 / 682 / 843 / 826 job seconds, against a 1800-second cap at 30 minutes. One run; see `CI_POLICY.md` § Linux Shard Count Decision.
- Docs-only Linux legs still run four shards: runs 44136021009 (1494 / 1392 / 1410 / 1466 s) and 43777684941 (1271 / 1365 / 1439 / 1280 s). The docs route is the tighter one for a 30-minute cap.

## Context

- The profile rebalance prerequisite shipped. Qualify the new 30-minute cap independently on code and policy-doc routes.

## Notes

PR #2097 merged as `d3802cc5aefe19aa55597f8e52a329e381186ede`. Hosted run [`43774416634`](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/43774416634) passed `validate`. Linux jobs finished in 21:19, 20:39, 21:13, 21:37, a 0:58 spread.
