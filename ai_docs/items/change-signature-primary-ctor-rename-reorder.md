# change-signature-primary-ctor-rename-reorder — Support rename and reorder on primary constructors in change_signature_preview

**row:** `change-signature-primary-ctor-rename-reorder` · **pri:** `Medium` · **size:** `M` · **deps:** `change-signature-record-primary-ctor-add-remove`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:72`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs:85`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] `op=rename|reorder` on a primary constructor (class, struct, record) rewrites the type declaration `ParameterList` and every construction site, including target-typed `new(...)` and named-argument callsites; for records, rename also accounts for the synthesized property or refuses naming that consequence.
- [ ] Regression tests cover rename and reorder on a class primary constructor and a positional record.

## Evidence

- Reproduced 2026-09-27 on roslyn-mcp 4.2.1 against TradeWise `CanonicalPriceBar` (positional record): `change_signature_preview op=remove` returned a misleading "produced no changes" failure. Root cause: `ChangeSignatureAddRemovePreviewBuilder.cs:72` and `:128` only rewrite `BaseMethodDeclarationSyntax`; a primary constructor's parameters live on the `TypeDeclarationSyntax.ParameterList`.

## Context

- Split child of `change-signature-primary-constructor-parameters` (plan 20261001T034545Z shipped only its interim refusal: primary-constructor targets are now refused with a specific public message). Each child replaces the refusal for its own shape. Found during TradeWise bl-2752 (PR #2658). Children share `ChangeSignatureAddRemovePreviewBuilder.cs` and `ChangeSignaturePreviewTests.cs`, so they are chained by `deps`.
