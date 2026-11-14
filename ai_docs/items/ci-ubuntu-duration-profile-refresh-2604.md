# ci-ubuntu-duration-profile-refresh-2604 — Refresh the ubuntu duration profile on the ubuntu-26.04 image and rebalance linux-2

**row:** `ci-ubuntu-duration-profile-refresh-2604` · **pri:** `High` · **size:** `M`

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
- Candidate sources (complete green first attempts on Ubuntu 26.04): 43320130733, 43368083735, 43450662803, 43462870516, and 43517237201. The profile PR run is independent validation.
- Profile mixes measured seconds with static case weights for 107 classes (planner log: profiled classes 399, fallback classes 107).

## Context

- Operator chose rebalance over pinning classes or adding a fifth shard (2026-11-13); a fifth shard is not justified: summed shard time is ~78 min, balanced ~20 min per shard.
- Related: `ci-shard-duration-profile-hosted`, `test-heavy-integration-class-cost`.

## Notes

2026-11-14: the canonical collector replay matched the profile generated from five successful first-attempt runs. PR 2097 run 43617927737 at 93822db6a420d7852ab49b4552624bbb3fd80da0 passed validate. Linux jobs took 22:33, 29:39, 24:20 and 17:44 (11:55 spread). The limits are at most 5:00 spread and all jobs below 25:00. Linux 2 measured ForkNativeAdmissionTests at 871.21 cumulative case seconds and InterfaceSignatureReferenceRouteTests at 443.91 despite near-equal planned weights. PR 2097 is draft. Reopen after class-cost work or a validated wall-time model can meet both criteria. The cap-restoration row stays blocked.
