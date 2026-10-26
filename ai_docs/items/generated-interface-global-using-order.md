# generated-interface-global-using-order — Keep generated global imports before ordinary imports

**row:** `generated-interface-global-using-order` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/UsingDirectiveSynthesizer.cs:83` (SortUsingDirectives)
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs` (generated interface consumer)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (generated interface consumer)
- `tests/RoslynMcp.Tests/UsingDirectiveSynthesizerTests.cs` (existing helper controls)
- `tests/RoslynMcp.Tests/ExtractInterfaceSemanticUsingsTests.cs` (existing extraction consumer controls)

## Acceptance

- [ ] Place every global using before ordinary using directives while preserving alias/static/global semantics and required namespace deduplication in all shared generated-interface consumers.
- [ ] Observe valid-before native extraction with mixed global/plain imports fail with actual CS8915 on old generated output; prove serialized preview/applied reload compile and bind after repair. Cover global alias/static forms and ordinary controls.
- [ ] Correct stale helper summary claiming raw cross-project move ownership; verify actual consumer references. Do not change raw-move file/header policy.

## Evidence

- Current root semantic get_source_text confirms SortUsingDirectives appends specials after ordinary imports at83–96, while IsSpecialUsingDirective includes GlobalKeyword at106–111. Thus ordinary+global output violates compiler ordering. Source summary8–12 incorrectly lists raw cross-project move as a consumer.
- Separate from interface-extractor-import-order, which owns source-file formatter baseline debt, and from raw-move-file-header-ownership, whose builder must preserve source scopes rather than invoke this synthesizer.
