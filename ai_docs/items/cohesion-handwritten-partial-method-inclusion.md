# cohesion-handwritten-partial-method-inclusion — Preserve handwritten partial methods in cohesion analysis

**row:** `cohesion-handwritten-partial-method-inclusion` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CohesionAnalysisService.cs`
- `tests/RoslynMcp.Tests/CohesionSemanticIdentityTests.cs`

## Acceptance

- [ ] Count a handwritten partial implementation once and analyze its body while continuing to exclude attributed generator methods.
- [ ] Prove the behavior with one focused regression shape and preserve the public DTO contract.

## Evidence

- 2026-09-14 direct cohesion remediation source review: IsSourceGenPartial returns true for every IsPartialDefinition, including handwritten partial implementations.
