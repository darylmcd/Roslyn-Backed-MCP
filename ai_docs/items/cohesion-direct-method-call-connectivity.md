# cohesion-direct-method-call-connectivity — Connect cohesion graph nodes through direct method calls

**row:** `cohesion-direct-method-call-connectivity` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CohesionAnalysisService.cs`
- `tests/RoslynMcp.Tests/CohesionSemanticIdentityTests.cs`

## Acceptance

- [ ] Add direct call edges using symbol identity; pin a caller/helper pair and retain separation for unrelated overloads.
- [ ] Prove the behavior with one focused regression shape and preserve the public DTO contract.

## Evidence

- 2026-09-14 direct cohesion remediation source review: ComputeClusters unions shared fields and shared helper targets but never connects a caller to the method node it invokes; no-field helpers remain separate clusters.
