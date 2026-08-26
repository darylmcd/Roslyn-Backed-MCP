# multifile-edit-preview-redemption-route-doc-drift — name the correct redemption route for preview_multi_file_edit

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs` (`PreviewMultiFileTextEditsAsync` summary; second site further down)
- `src/RoslynMcp.Core/Services/IEditService.cs` (interface doc)
- `src/RoslynMcp.Host.Stdio/Tools/MultiFileEditTools.cs` (the agent-visible tool `Description`)

## Acceptance

- [ ] All four sites name `preview_multi_file_edit_apply` (or `apply_with_verify`) as the redemption route for a `preview_multi_file_edit` token; none suggests `apply_composite_preview`.
- [ ] The MCP tool `Description` no longer advertises the composite pairing to clients.

## Evidence

Cold code-quality review of PR #1376 (sweep `20260825T214500Z`) verified the two stores are disjoint: `apply_composite_preview` resolves through `ICompositePreviewStore`, and `CompositePreviewStore` is an independent bounded store with no lookup into `PreviewStore`, so `CompositeApplyOrchestrator` returns "Preview token is invalid, expired, or stale" for such a token. PR #1376's own new comment names the correct route roughly 20 lines below the summary that names the wrong one, leaving a self-contradiction inside a single method.

Agent-visible: the wrong route is in the tool `Description`, so MCP clients are actively misdirected.

## Context

Deliberately not fixed inside the route-binding child that surfaced it — its Scope was at the Rule 3 cap.
