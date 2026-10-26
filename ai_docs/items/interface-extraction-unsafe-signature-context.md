# interface-extraction-unsafe-signature-context — Preserve unsafe signature context

**row:** `interface-extraction-unsafe-signature-context` · **pri:** `High` · **size:** `M` · **deps:** `interface-extraction-nullable-context-projection`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:83` (interface declaration)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:567` (interface modifiers)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve required unsafe context for emitted pointer/function-pointer signature types, including return/property/indexer and parameter positions across both extractors.
- [ ] Support representable output in unsafe-enabled receiving projects; refuse a concrete receiving-project policy incompatibility before token publication or writes without changing that policy implicitly.
- [ ] Prove red-first public producer compilation plus preview/apply/reload for supported cases, ordinary controls and unchanged token/workspace/disk state for refusals.

## Evidence

- ACTIVE dependency checkpoint: PR1849 merged27f82e45 and PR1846 merged5b1930a3 with cold PASS and all required hosted checks green. This objective follows the active nullable-context child through its existing dependency chain; implementation remains separate from preserved combined draft PR1840. Original Acceptance and complex candidate tests remain unchanged. Native134/139 causal findings stay OPEN; ownership prevention does not prove their cause. See ai_docs/reports/2026-10-26-interface-split-delivery.md.


- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found both generated interfaces carry only accessibility, so valid unsafe source signatures lose their compilation context. The shared parameter generator preserves the pointer syntax but does not repair the containing declaration. Runtime proof pending.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
