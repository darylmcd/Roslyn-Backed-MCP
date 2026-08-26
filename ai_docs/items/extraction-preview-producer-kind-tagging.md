# extraction-preview-producer-kind-tagging — tag the extraction/move preview producers with their PreviewKind

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs`
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs`
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`
- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`

## Acceptance

- [ ] Each of the four services passes `PreviewKind.ExtractMethod` / `ExtractInterface` / `ExtractType` / `MoveTypeToFile` to the kind-carrying `PreviewStore.Store` overload; `extract_shared_expression_to_helper` (no named apply route) stays untagged.
- [ ] A test proves a token minted by `extract_type_preview` is refused by `extract_method_apply` before the refactoring service is called.

## Evidence

Traced at code level by the cold code-quality review of PR #1377 (sweep `20260825T214500Z`): all four producers still call the no-kind `Store` overload, `PreviewStore` defaults those overloads to `PreviewKind.Unspecified`, and `ToolDispatch.RequireCompatibleProducer` returns early on `Unspecified`. So the four apply routes bound by #1377 declare a producer family but cannot yet refuse a sibling extraction token — only the five originally-tagged kinds are actually enforced.

## Context

Explicitly deferred by that initiative's Approach; the consumer half shipped in #1377. This is the paired producer half.
