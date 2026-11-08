# cross-project-interface-signature-refusal-compile — Refuse cyclic signature closure

**row:** `cross-project-interface-signature-refusal-compile` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-interface-signature-reference-closure`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:183`
- `src/RoslynMcp.Roslyn/Services/InterfaceMemberProjector.cs:297`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs:485`

## Acceptance

- [ ] A source-owned or otherwise cyclic emitted signature refuses with the concrete unsupported closure before token storage, while valid acyclic references still preview across direct extraction, dependency inversion and extract-and-wire.

## Evidence

- At `0121d9ab`, extraction stores a preview after adding only a source-to-target edge. Source-owned IAnimal would require a cyclic target-to-source edge.

## Context

- Depends on valid-reference closure. Add an ADR and BREAKING migration note for public preview refusal behavior. The dependent compile-gate row handles other generated-interface errors.
