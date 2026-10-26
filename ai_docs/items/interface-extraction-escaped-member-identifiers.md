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

- PARKED with preserved draft PR1840; existing dependency chain is held by PR1846/1849 required hosted failures. No scope or Acceptance removed. Native PowerShell134/139 diagnosis has not established a causal repair after bounded base/cache probes; see ai_docs/reports/2026-10-26-interface-split-delivery.md for exact heads, failing jobs and resume conditions. Do not merge the combined candidate or retry unchanged CI.


- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found raw method.Name and event.Name passed to syntax factories. The new shared property/parameter projection escapes names; copied member emission must agree. Runtime proof pending.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
