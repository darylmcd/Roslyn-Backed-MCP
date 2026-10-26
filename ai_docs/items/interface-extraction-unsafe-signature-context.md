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

- PARKED with preserved draft PR1840; existing dependency chain is held by PR1846/1849 required hosted failures. No scope or Acceptance removed. Native PowerShell134/139 diagnosis has not established a causal repair after bounded base/cache probes; see ai_docs/reports/2026-10-26-interface-split-delivery.md for exact heads, failing jobs and resume conditions. Do not merge the combined candidate or retry unchanged CI.


- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found both generated interfaces carry only accessibility, so valid unsafe source signatures lose their compilation context. The shared parameter generator preserves the pointer syntax but does not repair the containing declaration. Runtime proof pending.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
