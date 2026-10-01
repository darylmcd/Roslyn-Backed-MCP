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
