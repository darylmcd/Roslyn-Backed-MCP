# interface-rebound-concrete-lookup-duplicated — Share the rebound concrete-type lookup

**row:** `interface-rebound-concrete-lookup-duplicated` · **pri:** `Low` · **size:** `M` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs`:321
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`:261

## Acceptance

- [ ] One helper resolves the concrete type in the post-generation compilation and throws one refusal text; both routes call it.
- [ ] Existing refusal tests (`InterfaceReplaceUsagesRefusalTests`) and the dependency-inversion tests stay green; refusal text change noted in the changelog if visible.

## Evidence

- iface-extraction report (PR 2145), sketch 4: "The FindSimilarSymbols rebind lookup is copied in InterfaceExtractionService.cs:~296 and CrossProjectRefactoringService.cs:~256 (different failure wording)".
- Verified at 12bc2c8b: both `SymbolFinder.FindSimilarSymbols(...).FirstOrDefault()` sites at the lines above.
