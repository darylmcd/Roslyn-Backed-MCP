# ci-restore-linux-leg-timeout-30-after-rebalance — Restore the 30 minute cap on hosted Linux shard legs

**row:** `ci-restore-linux-leg-timeout-30-after-rebalance` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/resolve-ci-topology.ps1:95-107` — five code Linux legs (`linux-N-of-5`, lines 97-101) use 45-minute caps. The temporary-cap comment is at lines 95-96. The six Windows code legs (lines 89-94) are also 45 and are not part of this row. The policy-docs leg (`docs-linux-1-of-1`, line 107) is already 30.
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs:476-497` — the code Linux shard caps are pinned to 45 (legs 477-481; temporary-cap comment at 476). The policy-docs leg is pinned to 30 (line 497).
- `CHANGELOG.md:71-72` — v5.1.0 published the temporary timeouts. Add a reverting fragment when the caps change.

## Acceptance

- [ ] Restore 30-minute caps for all five code Linux shard legs. Update the pinned contract expectations to 30 and remove the temporary-cap comments. The policy-docs leg needs no change: it became one 30-minute leg on 2026-11-17 (`CI_POLICY.md` § Docs Route Test Selection).
- [ ] Demonstrate that the first full code PR run after the change keeps every Linux leg under the cap with margin, and that a docs-only PR run of the one-leg route finishes under its 30-minute cap.

## Evidence

- PR 2089 raised the code cap to 45 on operator approval (2026-11-13) after linux-2 hit 30 minutes with all tests passing. PR 2090 aligned the docs-only cap.
- Release v5.1.0 consumed both original fragments.
- PR 2135 (2026-11-17) changed the code route from four to five Linux legs and left every cap at 45. Its first run 44176184128 (head a6ac242a, first attempt, green): code Linux legs took 861 / 845 / 682 / 843 / 826 job seconds, against a 1800-second cap at 30 minutes. One run; see `CI_POLICY.md` § Linux Shard Count Decision.
- Docs-only route: while it ran four complete-partition shards it was the tighter one for a 30-minute cap (runs 44136021009: 1494 / 1392 / 1410 / 1466 s; 43777684941: 1271 / 1365 / 1439 / 1280 s). The 2026-11-17 docs-route decision replaced it with one leg capped at 30 that runs only the docs-tier consumer classes (local Windows run: 143 cases, whole verifier 135 s). No hosted run of that leg existed when this was written.

## Context

- The profile rebalance prerequisite shipped. Qualify the 30-minute cap on the code route, and confirm the policy-docs leg on its first hosted run.

## Notes

PR #2097 merged as `d3802cc5aefe19aa55597f8e52a329e381186ede`. Hosted run [`43774416634`](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/43774416634) passed `validate`. Linux jobs finished in 21:19, 20:39, 21:13, 21:37, a 0:58 spread.
