# ci-duration-profile-excludes-zero-run-class-samples — Require positive class duration in every sampled run

**row:** `ci-duration-profile-excludes-zero-run-class-samples` · **pri:** `Low` · **size:** `S` · **verified:** `ddf4cc6d0f4d1ee8a165571c099820583f52eeb9`

## Anchors

- `eng/collect-hosted-shard-timings.ps1:674`
- `tests/RoslynMcp.Tests/TestResultsSummaryContractTests.cs`

## Acceptance

- [ ] A class with one zero-duration sample and another positive sample is excluded.

## Evidence

- Collector requires aggregate positivity. CI_POLICY.md requires positive duration per run. None of the November 14 profile inputs has a mixed-zero included class.
