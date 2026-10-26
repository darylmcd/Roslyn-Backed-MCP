# interface-extraction-escaped-member-identifiers — Preserve escaped member identifiers

**row:** `interface-extraction-escaped-member-identifiers` · **pri:** `High` · **size:** `M` · **deps:** `interface-extraction-nullable-context-projection`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:188` (`BuildInterfaceMembers`)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:602` (`CreateInterfaceMember`)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve C# identifier escaping for emitted method/property/event names in both extraction generators, covering every emitted member kind.
- [ ] Compile and bind keyword-named member declarations and calls through actual public extraction routes before and after apply/reload; preserve normal names and source trivia.
- [ ] Cover supported method type-parameter identifier spelling without expanding omitted generic declaration features owned by cross-project-interface-generic-type-parameters.

## Evidence

- ACTIVE dependency checkpoint: PR1849 merged27f82e45 and PR1846 merged5b1930a3 with cold PASS and all required hosted checks green. This objective follows the active nullable-context child through its existing dependency chain; implementation remains separate from preserved combined draft PR1840. Original Acceptance and complex candidate tests remain unchanged. Native134/139 causal findings stay OPEN; ownership prevention does not prove their cause. See ai_docs/reports/2026-10-26-interface-split-delivery.md.


- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found raw method.Name and event.Name passed to syntax factories. The new shared property/parameter projection escapes names; copied member emission must agree. Runtime proof pending.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
