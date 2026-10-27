# interface-extraction-escaped-member-identifiers — Preserve escaped member identifiers

**row:** `interface-extraction-escaped-member-identifiers` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:188` (`BuildInterfaceMembers`)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:602` (`CreateInterfaceMember`)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve C# identifier escaping for emitted method/property/event names in both extraction generators, covering every emitted member kind.
- [ ] Compile and bind keyword-named member declarations and calls through actual public extraction routes before and after apply/reload; preserve normal names and source trivia.
- [ ] Cover supported method type-parameter identifier spelling without expanding omitted generic declaration features owned by cross-project-interface-generic-type-parameters.
- [ ] Escape names when `SyntaxFacts.GetKeywordKind(name) != None` (reserved keywords only) for method, property, event and method type-parameter names; the generator escapes parameter names. Test `@class` methods, `@event` events and properties, and a `<@class>` type parameter (raw names today at IES:198,205,212,230,239 and CPRS:505,510,519).

## Evidence

- Unblocked 2026-10-27: PR1854 merged (`998b0e95`) and PR1857 moved hosted Linux to three shards; the earlier PARKED hold no longer applies.


- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found raw method.Name and event.Name passed to syntax factories. The new shared property/parameter projection escapes names; copied member emission must agree. Runtime proof pending.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
- 2026-10-27: dependency `interface-extraction-nullable-context-projection` closed by PR1854 (hosted Linux budget fixed by PR1857). PR1840 closed as superseded; its design for this row is inlined here and its archive branches were deleted (head still at GitHub `refs/pull/1840/head`). Re-cut from current main; do not resume PR1840.
