# refactor-services-non-atomic-write-rollback — Add atomic temp+rename writes and rollback on partial multi-file apply failure

**row:** `refactor-services-non-atomic-write-rollback` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:76`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:57`
- `src/RoslynMcp.Roslyn/Services/EditService.cs:664`
- `src/RoslynMcp.Roslyn/Services/EditService.cs:158`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:296`
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:562`

## Acceptance

- [ ] All direct File.WriteAllTextAsync calls in these 4 services route through an atomic temp+rename helper.
- [ ] A simulated mid-loop IOException in CompositeApplyOrchestrator's multi-file apply either rolls back prior writes or surfaces a clearly-marked partial-apply result with logging, and the preview token/ChangeTracker state stays consistent with what was actually written.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03a-roslyn-refactor-services::DG5-security-data, S03a-roslyn-refactor-services::DG3-robustness, S03a-roslyn-refactor-services::DG6-testability-obs
