# interface-extraction-escaped-member-identifiers — Preserve escaped member identifiers

**row:** `interface-extraction-escaped-member-identifiers` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:245-286`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:602-615`
- `tests/RoslynMcp.Tests/InterfaceParameterSemanticsIntegrationTests.cs` (new in parameter cluster)

## Acceptance

- [ ] Preserve C# identifier escaping for emitted method/property/event names in both extraction generators, covering every emitted member kind.
- [ ] Compile and bind keyword-named member declarations and calls through actual public extraction routes before and after apply/reload; preserve normal names and source trivia.
- [ ] Cover supported method type-parameter identifier spelling without expanding omitted generic declaration features owned by cross-project-interface-generic-type-parameters.

## Evidence

- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found raw method.Name and event.Name passed to syntax factories. The new shared property/parameter projection escapes names; copied member emission must agree. Runtime proof pending.
