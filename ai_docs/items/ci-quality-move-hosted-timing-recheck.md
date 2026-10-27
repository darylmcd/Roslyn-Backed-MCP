# ci-quality-move-hosted-timing-recheck — Recheck hosted timings after parallel quality scheduling

**row:** `ci-quality-move-hosted-timing-recheck` · **pri:** `Medium` · **size:** `S` · **deps:** `ci-artifact-owner-quality-deadline`

## Anchors

- `eng/collect-hosted-shard-timings.ps1`
- `CI_POLICY.md`

## Acceptance

- [ ] After the scheduling prerequisite merges, collect at least five complete green six-leg hosted runs with fresh in-window TRX; retain exact run/head/image evidence.
- [ ] Re-run the canonical timing collector and compare achievable gain to each image noise band under the existing policy. Do not introduce duration profiles from one run or local timing.
- [ ] Record measured results and justified weighting decision; do not claim the scheduling fix itself completes calibration.

## Evidence

- CI_POLICY requires recheck when non-test work moves between legs. This is the tracked evidence remainder of ci-artifact-owner-quality-deadline, not a requirement to retry a failed head.

- Two observed complete-green post-prerequisite code runs now exist:40401658309(namespace1846) and40419476050(startup1855). This is below five and is not a completed canonical collector/image-noise qualification. Keep every original Acceptance condition open.
