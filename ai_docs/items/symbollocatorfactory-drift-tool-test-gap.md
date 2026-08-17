# symbollocatorfactory-drift-tool-test-gap — Add unit coverage for SymbolLocatorFactory and workspace_drift_check

**row:** `symbollocatorfactory-drift-tool-test-gap` · **pri:** `Low` · **size:** `M` · **deps:** `resource-read-protocol-error-semantics`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorFactory.cs:41`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorFactory.cs:161`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceDriftTool.cs:29`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:115`

## Acceptance

- [ ] New unit tests directly exercise SymbolLocatorFactory.Create() branches and TruncateForMessage boundary/ellipsis cases
- [ ] New unit/integration test directly covers workspace_drift_check for at least one drift scenario
- [ ] ToolErrorHandler.cs:115 XML doc no longer references the removed ExecuteAsync method

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04d-host-workspace-infra-tools::DG6-testability-obs
