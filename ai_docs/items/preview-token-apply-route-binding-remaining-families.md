# preview-token-apply-route-binding-remaining-families — bind the remaining apply families to preview-token kinds

**row:** `preview-token-apply-route-binding-remaining-families` · **pri:** `Medium` · **size:** `L`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs` (`PreviewToolFor` / `ApplyRouteFor` switches, `applyRoute` parameter)
- `src/RoslynMcp.Host.Stdio/Tools/BulkRefactoringTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/CodeActionTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/DeadCodeTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ExtractMethodTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/FileOperationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/FixAllTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/InterfaceExtractionTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/MultiFileEditTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/TypeExtractionTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/TypeMoveTools.cs`
- `tests/RoslynMcp.Tests/ToolDispatchTests.cs`

## Acceptance

- [ ] Every named apply route that redeems a preview token declares its compatible `PreviewKind` set, so acceptance bullet 2 of the retired row `preview-token-apply-route-provenance` ("each named apply route accepts only compatible token kinds") holds universally rather than for the 5 `RefactoringTools` routes alone.
- [ ] The kind→(previewTool, applyRoute) knowledge lives in ONE map. Today it is encoded three times: `ToolDispatch.PreviewToolFor`, `ToolDispatch.ApplyRouteFor`, and the `applyRoute:` string literals at each call site.
- [ ] An unmapped CONCRETE `PreviewKind` fails loudly instead of falling through to the "an untagged \*_preview tool" / "apply_with_verify" arm; a test pins that every non-`Unspecified` member has a mapping.
- [ ] `ToolDispatchTests` no longer needs the "a route that declares no expectedKind must keep its pre-binding behavior" escape once every route is bound — or that test is re-scoped to out-of-tree `IPreviewStore` implementers only.
- [ ] **Split before planning** — this is `size: L` (>4 production files). Partition by tool family into per-slice children, mirroring how `method-description-diet` was sliced.

## Evidence

Measured on `main` 2026-08-25 after PR #1354 landed: `grep -c ApplyByTokenAsync src/RoslynMcp.Host.Stdio/Tools/*.cs` reports call sites in 15 production files, while only 5 `applyRoute:` bindings exist (all in `RefactoringTools.cs`: `rename_apply`, `organize_usings_apply`, `format_document_apply`, `code_fix_apply`, `format_range_apply`). `tests/RoslynMcp.Tests/ToolDispatchTests.cs:146` explicitly asserts unbound routes retain pre-binding behavior.

Cold code-quality review of PR #1354 additionally traced the triple-encoding: `ToolDispatch.cs:207-217` and `:221-231` are parallel switches over the same enum, each ending in a silent catch-all, and the same knowledge appears as literals at `RefactoringTools.cs:60/95/130/169/210`. A future concrete `PreviewKind` added without touching all three would emit a factually wrong, actionable-looking error telling the caller to redeem via `apply_with_verify`.

## Context

Direct follow-on required by the plan stanza for initiative `preview-token-apply-route-binding` (sweep `20260825T151721Z`), which chose an optional-parameter design to hold the Rule 3 budget at 3 files and explicitly deferred the remaining ~10 families here. The parent row `preview-token-apply-route-provenance` was closed on that basis — this row carries the residue, so it must not be dropped.

The reviewer recommended folding the map-centralization finding into THIS row rather than filing a sibling, because both would edit `ToolDispatch.cs` and collide as concurrent PRs.
