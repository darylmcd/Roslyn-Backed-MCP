# composite-redemption-admission-boundary — Revalidate composite redemption before claim and write

**row:** `composite-redemption-admission-boundary` · **pri:** `High` · **size:** `M`

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

## Notes

Reordered 2026-11-05 per cold plan review: this piece ships FIRST. Without it, composite-token-portable-workspace-identity makes forged disk tokens redeemable and the composite overload at ToolDispatch.cs:379-392 skips RevalidateChangedPathsAsync (PersistDirectory is unvalidated, Program.cs:251). Needs PeekChangedPaths on ICompositePreviewStore and a generalized revalidation; ProjectMutationTools.cs:241 has the same defect. A disk PeekChangedPaths must fail closed (the existing "null means skip" convention is fail-open). The admission half is covered by the identity piece pre-claim resolve; direct orchestration must still refuse before writes. Test both orchestrator constructions (UndoService.cs:80 throws before any write when the DI undo service is present).
