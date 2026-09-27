# change-signature-primary-constructor-parameters - Support primary-constructor parameters in change_signature_preview

**row:** `change-signature-primary-constructor-parameters` · **pri:** `Medium` · **size:** `M` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] `op=remove|add|rename|reorder` on a positional `record` (and a C# 12 class/struct primary constructor) rewrites the type declaration's `ParameterList` and every construction site, including target-typed `new(...)` and named-argument callsites
- [ ] For records, the preview also accounts for the synthesized positional property (removal/rename changes a public member): either include its readers/`with`/deconstruction sites in the preview, or refuse with a specific message naming that consequence
- [ ] Until full support lands, a primary-constructor target is refused with a specific message rather than the "produced no changes" path
- [ ] Regression tests cover a positional record and a class primary constructor for each op
- [ ] backlog: sync ai_docs/backlog.md

## Evidence

- Reproduced 2026-09-27 on roslyn-mcp 4.2.1 against TradeWise `CanonicalPriceBar` (`public sealed record CanonicalPriceBar(...)`, `src/TradeWise.Domain/MarketData/CanonicalPriceBar.cs:19`): `change_signature_preview op=remove name=Volume` with file/line/column returned `InvalidOperation` "The operation is not valid in the current state". The `metadataName ..ctor` form fails the same way (TradeWise bl-2752 executor).
- Root cause: `ChangeSignatureAddRemovePreviewBuilder.cs:72` (`if (node is not BaseMethodDeclarationSyntax mds) continue;`) and `:128` (`FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()`) only rewrite method/constructor declarations. A primary constructor's parameters live on the `TypeDeclarationSyntax`'s `ParameterList`, so nothing is rewritten and `ChangeSignatureService.cs:476` throws "produced no changes". The host then masks that message; the masking is tracked separately by `change-signature-refusals-public-message`.

## Context

Found during TradeWise bl-2752 (PR #2658), where removing a positional record parameter across 4 test projects had to be done by hand plus compile_check iteration.
