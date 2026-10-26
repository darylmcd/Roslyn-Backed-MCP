# interface-extraction-unsafe-signature-context — Preserve unsafe signature context

**row:** `interface-extraction-unsafe-signature-context` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:89-91`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:567-569`
- `tests/RoslynMcp.Tests/InterfaceParameterSemanticsIntegrationTests.cs` (new in parameter cluster)

## Acceptance

- [ ] Preserve required unsafe context for emitted pointer/function-pointer signature types, including return/property/indexer and parameter positions across both extractors.
- [ ] Support representable output in unsafe-enabled receiving projects; refuse a concrete receiving-project policy incompatibility before token publication or writes without changing that policy implicitly.
- [ ] Prove red-first public producer compilation plus preview/apply/reload for supported cases, ordinary controls and unchanged token/workspace/disk state for refusals.

## Evidence

- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found both generated interfaces carry only accessibility, so valid unsafe source signatures lose their compilation context. The shared parameter generator preserves the pointer syntax but does not repair the containing declaration. Runtime proof pending.
