# interface-extraction-ref-return-contracts — Preserve extracted ref returns

**row:** `interface-extraction-ref-return-contracts` · **pri:** `High` · **size:** `M` · **deps:** `interface-member-projection-shared-emitter`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:188` (`BuildInterfaceMembers`)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:602` (`CreateInterfaceMember`)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve ref and ref readonly return kinds on emitted methods, properties and indexers in both interface extractors.
- [ ] Verify generated symbol return kinds and real ref calls through extraction, inversion and extract-and-wire preview plus apply/reload; retain ordinary value-return controls.
- [ ] Observe the public regression fail on original production before the shared declaration projection fix.
- [ ] Return type = `RefType(TypeExpression(T))`, plus `readonly` for RefReadOnly, on methods, properties and indexers; copy `[UnscopedRef]` from the method, property and each accessor. Shapes: ref method, ref-readonly property, struct member returning `ref` to a field with `[UnscopedRef]`, ref struct, accessor-level `[UnscopedRef]`.

## Evidence

- Unblocked 2026-10-27: PR1854 merged (`998b0e95`) and PR1857 moved hosted Linux to three shards; the earlier PARKED hold no longer applies.


- Cold source review at main 0e0f90661035a38e570627b1508dac2ea2496708 found both generators use ReturnType/Type alone, omitting ReturnsByRef/ReturnsByRefReadonly and property.RefKind. New indexer/property projection exposes the copied omission; repair in this cluster rather than defer it. Runtime proof pending.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
- 2026-10-27: dependency `interface-extraction-nullable-context-projection` closed by PR1854 (hosted Linux budget fixed by PR1857). PR1840 closed as superseded; its design for this row is inlined here and its archive branches were deleted (head still at GitHub `refs/pull/1840/head`). Re-cut from current main; do not resume PR1840.
