# cohesion-overload-cluster-symbol-identity — cohesion-overload-cluster-symbol-identity

**row:** `cohesion-overload-cluster-symbol-identity` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CohesionAnalysisService.cs`
- `tests/RoslynMcp.Tests/CohesionAnalysisTests.cs`

## Acceptance

- [ ] Preserve distinct overloads in cohesion clustering by using a symbol-stable internal key rather than `method.Name`.
- [ ] Keep the public DTO shape unchanged while metrics account for every overload.
- [ ] Add one overloaded-method regression that proves neither method is overwritten.

## Evidence

- The live audit found the clustering map keyed by simple method name, which overwrites overload entries before metric projection.
