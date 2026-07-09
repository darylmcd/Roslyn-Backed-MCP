# host-tools-layer-test-coverage-gap — Add Tools-layer tests for the 8 untested MCP tool endpoint classes and align ValidationTools error handling

**row:** `host-tools-layer-test-coverage-gap` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs:26`
- `src/RoslynMcp.Host.Stdio/Tools/SecurityTools.cs:1`
- `src/RoslynMcp.Host.Stdio/Tools/SuppressionTools.cs:1`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:29`

## Acceptance

- [ ] Each of the 8 named Tools classes has at least one test invoking the Tools static class directly (not only its Core service).
- [ ] ValidationTools' build_workspace/build_project/test_discover/test_related/test_related_files handlers classify failures via ToolErrorHandler consistent with test_run.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04c-host-build-test-tools::DG6-testability-obs
