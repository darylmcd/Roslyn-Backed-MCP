# ci-duration-profile-requires-complete-shard-indices — Validate every declared shard index in each profile run

**row:** `ci-duration-profile-requires-complete-shard-indices` · **pri:** `Low` · **size:** `S` · **verified:** `ddf4cc6d0f4d1ee8a165571c099820583f52eeb9`

## Anchors

- `eng/collect-hosted-shard-timings.ps1:457`
- `tests/RoslynMcp.Tests/TestResultsSummaryContractTests.cs`

## Acceptance

- [ ] Reject duplicate or missing declared shard indices, including a shard omitted from every sampled run.

## Evidence

- Completeness currently derives from observed manifest leg names. November 14 review verified every declared index in the selected cohort.
