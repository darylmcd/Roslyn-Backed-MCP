# interface-extraction-unsafe-signature-context — Preserve unsafe signature context

**row:** `interface-extraction-unsafe-signature-context` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:83` (interface declaration)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:567` (interface modifiers)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve required unsafe context for emitted pointer/function-pointer signature types, including return/property/indexer and parameter positions across both extractors.
- [ ] Support representable output in unsafe-enabled receiving projects; refuse a concrete receiving-project policy incompatibility before token publication or writes without changing that policy implicitly.
- [ ] Prove red-first public producer compilation plus preview/apply/reload for supported cases, ordinary controls and unchanged token/workspace/disk state for refusals.
- [ ] After building the interface declaration, if any descendant is `PointerTypeSyntax` or `FunctionPointerTypeSyntax`: add `unsafe` to the interface when the receiving `CSharpCompilationOptions.AllowUnsafe` is true, otherwise refuse before token storage ("requires unsafe signatures; receiving project does not enable AllowUnsafeBlocks"). Test the three non-standalone routes with unsafe-disabled (refusal) and enabled (success) receivers.

## Evidence

- Unblocked 2026-10-27: PR1854 merged (`998b0e95`) and PR1857 moved hosted Linux to three shards; the earlier PARKED hold no longer applies.


- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found both generated interfaces carry only accessibility, so valid unsafe source signatures lose their compilation context. The shared parameter generator preserves the pointer syntax but does not repair the containing declaration. Runtime proof pending.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
- 2026-10-27: dependency `interface-extraction-nullable-context-projection` closed by PR1854 (hosted Linux budget fixed by PR1857). PR1840 closed as superseded; its design for this row is inlined here and its archive branches were deleted (head still at GitHub `refs/pull/1840/head`). Re-cut from current main; do not resume PR1840.
