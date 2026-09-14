# cohesion-ranked-limit-after-discovery — Apply the cohesion result limit after severity ranking

**row:** `cohesion-ranked-limit-after-discovery` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CohesionAnalysisService.cs`
- `tests/RoslynMcp.Tests/CohesionSemanticIdentityTests.cs`

## Acceptance

- [ ] Return the highest-scoring type for limit=1 regardless of document ordering and preserve cancellation and incomplete-scan reporting.
- [ ] Prove the behavior with one focused regression shape and preserve the public DTO contract.

## Evidence

- 2026-09-14 direct cohesion remediation source review: GetCohesionMetricsDetailedAsync stops scanning at the result limit before sorting by Lcom4Score, so an early low-score type hides a later higher-score type.
