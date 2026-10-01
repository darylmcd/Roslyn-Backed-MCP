# change-signature-record-primary-ctor-add-remove — Support add/remove on positional records in change_signature_preview

**row:** `change-signature-record-primary-ctor-add-remove` · **pri:** `Medium` · **size:** `M` · **deps:** `change-signature-class-struct-primary-ctor-add-remove`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:72`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs:85`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] `op=add|remove` on a positional `record` / `record struct` rewrites the parameter list and every construction site (including target-typed `new(...)` and named arguments).
- [ ] Removal or rename changes a public member (the synthesized positional property): either include its readers, `with` and deconstruction sites in the preview, or refuse with a specific message naming that consequence.
- [ ] Regression tests cover a positional record for add and remove, including a `with` and a deconstruction site.

## Evidence

- Reproduced 2026-09-27 on roslyn-mcp 4.2.1 against TradeWise `CanonicalPriceBar` (positional record): `change_signature_preview op=remove` returned a misleading "produced no changes" failure. Root cause: `ChangeSignatureAddRemovePreviewBuilder.cs:72` and `:128` only rewrite `BaseMethodDeclarationSyntax`; a primary constructor's parameters live on the `TypeDeclarationSyntax.ParameterList`.

## Context

- Split child of `change-signature-primary-constructor-parameters` (plan 20261001T034545Z shipped only its interim refusal: primary-constructor targets are now refused with a specific public message). Each child replaces the refusal for its own shape. Found during TradeWise bl-2752 (PR #2658). Children share `ChangeSignatureAddRemovePreviewBuilder.cs` and `ChangeSignaturePreviewTests.cs`, so they are chained by `deps`.
