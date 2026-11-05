# composite-redemption-admission-boundary — Revalidate composite redemption before claim and write

**row:** `composite-redemption-admission-boundary` · **pri:** `High` · **size:** `M` · **deps:** `composite-token-portable-workspace-identity`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`
- `src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs`
- `src/RoslynMcp.Core/Services/ICompositePreviewStore.cs`
- `tests/RoslynMcp.Tests/PreviewApplyBoundaryRevalidationTests.cs`

## Acceptance

- [ ] A token whose persisted path lies outside the sanctioned roots refuses before claim and writes.
- [ ] Direct orchestration with an unadmitted session refuses before writes (no WorkspaceNotFound after writes).

## Evidence

- `ToolDispatch.cs:379-392` composite overload has no `RevalidateChangedPathsAsync`; `CompositeApplyOrchestrator.cs:165` reloads after writes.
