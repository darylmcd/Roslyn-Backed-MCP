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

## Evidence (2026-10-28, ~/.claude ci-durations smoke run)

- `node ~/.claude/scripts/ci-durations.mjs D:/Roslyn-Backed-MCP --json` over 30 successful attempt-1 `pull_request` runs of `ci.yml`; medians re-derived independently from `gh run view <id> --json jobs`, identical.
- `shard-imbalance`: `validate-leg (windows-hosted-1-of-4)` median 1983 s, 1.98x its same-run Windows siblings (~1000 s), 953 s longer, 15 of 15 full-route runs.
- `ai_docs/CI_POLICY.md:104` "Current Timing Evidence" table holds pre-sharding numbers (Windows median job 16m09s, Linux 25m10s) under a "Current" heading.
- `ai_docs/CI_POLICY.md:140` states a duration-weighted partition cannot beat the noise band; the 1.98x over 15/15 runs contradicts it for the Windows lane.
- `ai_docs/CI_POLICY.md:146-149` Linux leg timings match the run data exactly.
- Re-evaluating weighting under this row should also refresh or retire the stale table at :104 and the :140 decision.
