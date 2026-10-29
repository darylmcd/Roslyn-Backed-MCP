# ci-shard-duration-profile-hosted — Weight hosted test shards by a measured per-image duration profile

**row:** `ci-shard-duration-profile-hosted` · **pri:** `Medium` · **size:** `M`

## Anchors

- `eng/get-test-shard-plan.ps1`
- `tests/RoslynMcp.Tests/TestShardPlanContractTests.cs`
- `CI_POLICY.md`
- (new) `eng/shard-duration-profile.json`

## Acceptance

- [ ] `eng/shard-duration-profile.json` holds per-class mean case seconds for `windows-latest` and `ubuntu-latest`, produced from at least five complete green hosted runs by `eng/collect-hosted-shard-timings.ps1` (or a documented extension of it); no local timing enters a hosted image's profile.
- [ ] `get-test-shard-plan.ps1` weights a class by its profile entry for the image, falls back to the static case weight for a class absent from the profile, and keeps the complete disjoint partition and the existing fail-closed checks.
- [ ] The profile's refresh procedure and staleness rule are written in `CI_POLICY.md` (when to regenerate, what a missing or stale class does).
- [ ] After merge, five fresh complete green runs are re-measured; the report records critical path and achievable gain against the 2026-10-29 baseline (Windows 1827.2s / 478.4s gain; Ubuntu 1322.8s / 229.0s).

## Evidence

- `CI_POLICY.md` § Hosted Shard Weighting Decision, 2026-10-29 recheck: both images show achievable gain above the single-leg noise band, so the case-count weighting no longer stands.
- Collector input: runs 40783926346, 40788319028, 40788434338, 40800095395, 40803366211 (35 leg observations).

## Context

- regression_shape: a contract test where a class with a large profile weight and few cases lands on a leg of its own, which the static weight would not do.
- A class runs serially under class-level parallelism, so the partition cannot beat the largest class; see `test-heavy-integration-class-cost` for reducing that floor. Sequence either first.
- Open question for the implementer: profile drift as classes change; the staleness rule above must answer it.
