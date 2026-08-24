# ci-hosted-shard-duration-balancing — Rebalance hosted shards from repeated timings

**row:** `ci-hosted-shard-duration-balancing` · **pri:** `Medium` · **size:** `M`

## Anchors

- `eng/get-test-shard-plan.ps1`
- `eng/summarize-test-results.ps1`
- `tests/RoslynMcp.Tests/TestShardPlanContractTests.cs`
- `tests/RoslynMcp.Tests/TestResultsSummaryContractTests.cs`

## Acceptance

- [ ] Collect at least five successful per-leg TRX runs for each hosted OS/image and quantify wall-time skew separately from summed case duration.
- [ ] Adopt duration weights only when repeated evidence shows material skew; keep deterministic discovered-case weights as a fail-closed fallback.
- [ ] Model hosted Windows and hosted Linux independently; do not mix local-machine timings into either profile.
- [ ] One deterministic missing/stale-profile regression proves no class is omitted, overlapped, or assigned to an empty shard.

## Evidence

Static discovered-case weights currently split the compiled classes evenly, but workspace-heavy and serialized fixtures have heterogeneous costs. The new hosted-only topology first needs repeated per-image evidence; a local Windows profile is not representative of GitHub-hosted Windows and cannot safely drive its partition.
2026-08-24 PR #1325 calibration: two hosted Windows jobs completed in 19m50s and 16m29s; their TRX cumulative class durations were 2,549.4s and 1,764.0s despite equal static weights. The unchanged static planner projects four shards at 1,258.7s, 865.6s, 785.5s, and 1,403.5s from the same evidence, so the immediate topology expanded to four. Require at least five successful same-image samples before adding an OS-specific duration profile.
