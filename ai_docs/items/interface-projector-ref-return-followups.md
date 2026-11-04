# interface-projector-ref-return-followups — Tighten ref-return route assertions and fix projector fragility

**row:** `interface-projector-ref-return-followups` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceMemberProjector.cs:92-95 (Project replaces all modifiers with unsafe on pointer members)`
- `src/RoslynMcp.Roslyn/Services/InterfaceMemberProjector.cs:205 (InheritedConstraints builds a member by string plus ParseMemberDeclaration)`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Extract-and-wire and apply/reload tests assert the consumer was retargeted to the interface and a real ref call compiles through it.
- [ ] Add a ref T generic return and a ref-returning member with a set accessor to the projector matrix.
- [ ] Pointer members keep their original non-access modifiers; InheritedConstraints no longer round-trips through ParseMemberDeclaration strings.

## Evidence

- PR 1961 proved coverage on all four routes and found no production defect; the reviewer noted these gaps and the projector fragility.
