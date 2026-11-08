# extract-interface-same-name-unrelated-contract-noop — Verify the implemented contract before no-op

**row:** `extract-interface-same-name-unrelated-contract-noop` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:65`
- `src/RoslynMcp.Roslyn/Helpers/GeneratedInterfaceReference.cs:99`
- `tests/RoslynMcp.Tests/ExtractInterfaceSemanticUsingsTests.cs:418`

## Acceptance

- [ ] Preserve the existing #748 no-op when the implemented same-name interface already represents the requested member contract.
- [ ] When a same-name implemented interface does not cover the requested member selection and the target namespace is free, generate the requested interface while preserving the originally implemented sibling-interface symbol. Refuse only a demonstrated target conflict. Never return a zero-change success warning for an incomplete contract.
- [ ] Red-first coverage uses a sibling-namespace implemented interface plus an additional requested public member; verify pending and applied symbol identities and compilation after generation.

## Evidence

- At `eed3273083b4c72784893d25c8be41cadfe8c876`, `InterfaceExtractionService.PreviewExtractInterfaceAsync` tests `typeSymbol.AllInterfaces.Any(i => i.Name == resolvedName)` before checking the selected member contract. The existing `ExtractInterface_Preview_NoOp_When_Type_Already_Implements_SameNamed_Interface` test (`ExtractInterfaceSemanticUsingsTests.cs:418-496`, regression #748) deliberately expects a no-op for a sibling-namespace interface that covers its single public member. This remains valid; a class with another requested public member exposes the name-only shortcut.

## Context

- If generation is chosen, adding a same-namespace interface can rebind the original unqualified base-list name. `GeneratedInterfaceReference.AddToBaseListAsync` may then see the generated symbol and skip a rewrite, so assert that the original sibling interface remains implemented. Qualify the original base-list binding before adding the new document when necessary; an actual occupied target namespace is a separate conflict refusal.
