# workspace-infra-resource-cleanup-hygiene — Fix unpruned/unguarded resource-cleanup gaps

**row:** `workspace-infra-resource-cleanup-hygiene` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:21`
- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs:64`

## Acceptance

- [ ] GatedCommandExecutor subscribes to workspace-close/eviction and removes the corresponding semaphore entry, verified by a repeated load/close-cycle test showing bounded dictionary size.
- [ ] PersistentCompositeStorage.TryRead no longer throws uncaught on a concurrently-deleted version subdirectory; a test simulating the race passes.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03d-roslyn-workspace-infra::DG7-config-deps-ergo, S03d-roslyn-workspace-infra::DG3-robustness
