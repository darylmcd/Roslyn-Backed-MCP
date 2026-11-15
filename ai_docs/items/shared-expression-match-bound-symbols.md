# shared-expression-match-bound-symbols — Preserve expression operand bindings during shared extraction

**row:** `shared-expression-match-bound-symbols` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:703` (`CollectMatchingExpressionHitsAsync`)
- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`

## Acceptance

- [ ] Compare bound operations in all original structurally matching occurrences before synthesizing the helper. Permit corresponding capture slots and equivalent declarations inside the selection. Require equivalent non-capture bindings, conversions, and operation semantics.
- [ ] Prove red through the experimental preview for identical source binding to different enclosing methods, different bound lambda delegate types, and checked versus unchecked arithmetic. Keep preview and applied compilation and binding for equivalent qualified references and separate but equivalent captures.

## Evidence

- At 9e7a9439, the matcher uses syntax equivalence and compares capture counts and types. Two expressions spelled `GetValue(x)` can call different enclosing methods, and a common helper selects the example's binding for both.
- Cold plan review identified this as a separate mechanism from binding the generated helper qualifier.
