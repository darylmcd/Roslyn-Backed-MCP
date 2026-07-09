# host-tools-complexity-hotspot-cleanup — Reduce cyclomatic complexity in four host tool-shim hotspots

**row:** `host-tools-complexity-hotspot-cleanup` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs:94`
- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs:118`
- `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs:112`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:230`

## Acceptance

- [ ] Each of the four methods measures cyclomatic complexity <=8 after refactor
- [ ] No behavior change: existing tests for server_info, path validation, prompt shim, and error classification still pass

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04d-host-workspace-infra-tools::DG2-cleanliness
