# preview-apply-tool-descriptions — Name each producer's apply route

**row:** `preview-apply-tool-descriptions` · **pri:** `Medium` · **size:** `M` · **deps:** `scaffold-batch-preview-apply-route`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/CrossProjectRefactoringTools.cs:18-70`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolRefactorTools.cs:24-71`
- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs:62-67`
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:122-123`
- `tests/RoslynMcp.Tests/ToolDispatchTests.cs`

## Acceptance

- [ ] Descriptions for `scaffold_test_batch_preview`, `dependency_inversion_preview`, `extract_interface_cross_project_preview`, `move_type_to_project_preview`, and `symbol_refactor_preview` name their actual apply tool.
- [ ] A contract test verifies each producer's documented route against the token store and a real apply or explicit wrong-route response.

## Evidence

- Parent `preview-token-store-mismatch-false-stale`: at least one description names the wrong store's apply tool; others omit the route.

## Context

- Waits for the scaffold token routing decision so the description reports the final route. One documentation contract across the listed producers.
