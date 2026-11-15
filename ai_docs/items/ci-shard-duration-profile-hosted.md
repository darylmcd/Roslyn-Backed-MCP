# ci-shard-duration-profile-hosted — Re-measure hosted shard balance after profile merge

**row:** `ci-shard-duration-profile-hosted` · **pri:** `Medium` · **size:** `M`

## Anchors

- `eng/collect-hosted-shard-timings.ps1`
- `CI_POLICY.md`
- `eng/shard-duration-profile.json`

## Acceptance

- [x] `eng/shard-duration-profile.json` holds per-class mean case seconds for `windows-latest` and `ubuntu-latest`, produced from at least five complete green hosted runs by `eng/collect-hosted-shard-timings.ps1`; no local timing enters a hosted image's profile.
- [x] `get-test-shard-plan.ps1` weights a class by its profile entry for the image, falls back to the static case weight for a class absent from the profile, and keeps the complete disjoint partition and the existing fail-closed checks.
- [x] The profile's refresh procedure and staleness rule are written in `CI_POLICY.md` (when to regenerate, what a missing or stale class does).
- [ ] After merge, five fresh complete green runs are re-measured; the report records critical path and achievable gain against the 2026-10-29 baseline (Windows 1827.2s / 478.4s gain; Ubuntu 1322.8s / 229.0s).

## Evidence

- `CI_POLICY.md` § Hosted Shard Weighting Decision, 2026-10-29 recheck: both images showed achievable gain above the single-leg noise band under case-count weighting.
- Profile source: runs 40783926346, 40788319028, 40788434338, 40800095395, 40803366211 (35 leg observations); each was verified complete, first-attempt, and green before generation.
- Implementation shipped in PR #1945, merged as `d0a94adf3fc705a132f6feebc752330299eb33cc` on 2026-11-02. Hosted checks passed all seven test legs at PR head `f9f9ad0478cd84767e127cce3c7cfd3b4597ed3d`; exact-head affected suite passed 71/71. The first hosted attempt exposed a release-fixture defect; it was corrected before merge.
- Remaining: measure critical path and achievable gain under a comparable shard topology and image against the 2026-10-29 baseline. The November 14 four-shard cohort documents current skew but changes both Linux topology and image.

## Context

- A class runs serially under class-level parallelism, so the partition cannot beat the largest class; see `test-heavy-integration-class-cost` for reducing that floor.
- Refresh the profile only from complete green hosted runs, using the procedure and staleness rule in `CI_POLICY.md`.

## Notes

2026-11-14 canonical non-ProfileJson report for 43320130733, 43368083735, 43450662803, 43462870516 and 43517237201. Four Ubuntu 26.04 legs: mean critical path 1,493.0 s, achievable gain 333.0 s, wall-time noise band 327.1 s, within-leg case-duration swing 2.07x vs across-leg 1.17x. Four Windows legs: 1,917.8 s, 293.6 s, 327.5 s and 1.62x vs 1.10x. Collector flags cumulative case time as an unreliable wall-time predictor on both images. Topology and Ubuntu image differ from the October 29 baseline. This measurement does not qualify closing the row or changing shard policy.
