# shared-members-explicit-generic-call-traversal — Recognize explicit generic calls in shared-member analysis

**row:** `shared-members-explicit-generic-call-traversal` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CohesionAnalysisService.cs`
- `tests/RoslynMcp.Tests/CohesionSemanticIdentityTests.cs`

## Acceptance

- [ ] Recognize inferred and explicit constructed calls to the same private generic helper without conflating overloads.
- [ ] Prove the behavior with one focused regression shape and preserve the public DTO contract.

## Evidence

- 2026-09-14 direct cohesion remediation source review: MethodAccessesMember scans only IdentifierNameSyntax and compares constructed symbols directly, so explicit generic helper invocations are omitted.
