# ci-ubuntu-duration-profile-refresh-2604 — Refresh the ubuntu duration profile on the ubuntu-26.04 image and rebalance linux-2

**row:** `ci-ubuntu-duration-profile-refresh-2604` · **pri:** `High` · **size:** `M` · **verified:** `6abdb0235dad35ae04705c6dfa6a69a0c4b1f1ee`

## Anchors

- `eng/shard-duration-profile.json` — `ubuntu-latest` entry still lists runs 40783926346…40803366211 (pre-rollover, ubuntu-24.04).
- `eng/collect-hosted-shard-timings.ps1` — the only sanctioned producer; fails closed below 5 distinct runs.
- `CI_POLICY.md` § Hosted Shard Weighting Decision — refresh procedure and staleness rule.

## Acceptance

- [ ] The `ubuntu-latest` profile holds 5 complete green first-attempt ubuntu-26.04 runs, produced by the collector (no local timing).
- [ ] The slowest Linux leg is within 5 minutes of the fastest on the first full PR run after the refresh, and every Linux leg finishes under 25 minutes.
- [ ] Row `ci-restore-linux-leg-timeout-30-after-rebalance` is then executable.

## Evidence

- Run 43386097968 linux-2: 1601 passed, 0 failed, 28m18s, cancelled at the 30 min cap. Same shard, earlier run 43365038953: 17m30s. Per-class TRX diff: `UndoServiceTests` +285 s (absent from the profile), `ForkNativeAdmissionTests` 473 s to 837 s.
- Eligible samples so far (ubuntu-26.04, full matrix, first attempt, all green): 43320130733, 43368083735, and the final head run of PR 2089. 43157187256 and 43152143261 ran on ubuntu-24.04 and do not count. Two more full code-PR runs are needed; the refresh PR cannot count its own run.
- Profile mixes measured seconds with static case weights for 107 classes (planner log: profiled classes 399, fallback classes 107).

## Context

- Operator chose rebalance over pinning classes or adding a fifth shard (2026-11-13); a fifth shard is not justified: summed shard time is ~78 min, balanced ~20 min per shard.
- Related: `ci-shard-duration-profile-hosted`, `test-heavy-integration-class-cost`.
