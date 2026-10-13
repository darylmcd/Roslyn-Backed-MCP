# change-signature-class-struct-primary-ctor-add-remove — Support add/remove on class and struct primary constructors in change_signature_preview

**row:** `change-signature-class-struct-primary-ctor-add-remove` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:72`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs:85`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] `op=add|remove` on a C# 12 class or struct primary constructor rewrites the type declaration's `ParameterList` and every construction site, including target-typed `new(...)` and named-argument callsites; the interim refusal no longer applies to this shape.
- [ ] Regression tests cover a class primary constructor and a struct primary constructor for add and remove.

## Evidence

- Reproduced 2026-09-27 on roslyn-mcp 4.2.1 against TradeWise `CanonicalPriceBar` (positional record): `change_signature_preview op=remove` returned a misleading "produced no changes" failure. Root cause: `ChangeSignatureAddRemovePreviewBuilder.cs:72` and `:128` only rewrite `BaseMethodDeclarationSyntax`; a primary constructor's parameters live on the `TypeDeclarationSyntax.ParameterList`.

## Context

- Split child of `change-signature-primary-constructor-parameters` (plan 20261001T034545Z shipped only its interim refusal: primary-constructor targets are now refused with a specific public message). Each child replaces the refusal for its own shape. Found during TradeWise bl-2752 (PR #2658). Children share `ChangeSignatureAddRemovePreviewBuilder.cs` and `ChangeSignaturePreviewTests.cs`, so they are chained by `deps`.

## Operator decision — defer for a broader design (2026-10-13)

| Field | Recorded decision / evidence |
|---|---|
| Live instruction | Defer this initiative for a broader design. |
| Rejected proposal | No approval for conservative atomic refusal of unresolved initialization, including valid pure computations whose neutrality cannot be established. |
| Root cause | Removing the last struct primary parameter can merge selected initialization and synthesized zero construction. Actual accepted-preview regressions cover serializer defaults, instance/static effects, nullable values, floating negative zero and decimal scale. |
| Preserved work | PR1779 remains OPEN on intermediate03d4af4b27af9ff9fb5bc1b871ebc2dd203ea6ba; existing source worktree/branch, staged/working deltas and cold0 history retained. No source mutation, landing or cleanup performed for this decision. |
| Held evidence | Scratch artifact fix1-held-common-cause-checkpoint.json; SHA256561c6e4a35d531929b53269a8c866f8d1328cedd85856170aff8402e03981e4c. Root independently checked364 artifact hashes/sizes and2 current source hashes with0mismatches. No final success result or corrected-head cold review. |
| Next admission | Re-vet a broader complete construction design and public contract; preserve positive representable cases and all causal witnesses. Fresh audited replan/review/admission and final-head validation remain required. |
| Ownership | Operator/design deferral, separate from global tooling remediation owned by the other agent. Keep the backlog row open; do not silently substitute the unapproved refusal policy. |
