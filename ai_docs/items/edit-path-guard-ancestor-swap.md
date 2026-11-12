# edit-path-guard-ancestor-swap — Keep the checked ancestor bound through write

**row:** `edit-path-guard-ancestor-swap` · **pri:** `Medium` · **size:** `M` · **verified:** `4c10969f300569f802eb1aecfcecc42a51721170`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`
- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs`

## Acceptance

- [ ] Qualify an external ancestor link swap after the final physical-path guard. Keep the checked ancestor identity through native and fallback writes or refuse before touching an unreviewed target. Verify disk bytes and undo ownership on each supported platform.

## Evidence

- Re-review of duplicate-target editing traced the existing gap from the physical-path guard to native TryApplyChanges and pathname-based commit. Source-derived. The duplicate-target fix carries the resolved DocumentId, which addresses service re-resolution.

## Context

- Separate from choosing the canonical validation result in composite-revalidation-canonical-path-toctou and from temporary filename swaps in atomic-file-writer-external-temp-path-swap.
