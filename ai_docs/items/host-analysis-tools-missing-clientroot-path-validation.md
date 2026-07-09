# host-analysis-tools-missing-clientroot-path-validation — Add ClientRootPathValidator.ValidatePathAgainstRootsAsync to filePath-accepting endpoints missing it

**row:** `host-analysis-tools-missing-clientroot-path-validation` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/CodeActionTools.cs:33`
- `src/RoslynMcp.Host.Stdio/Tools/CodeActionTools.cs:52`
- `src/RoslynMcp.Host.Stdio/Tools/FlowAnalysisTools.cs:25`
- `src/RoslynMcp.Host.Stdio/Tools/FlowAnalysisTools.cs:43`
- `src/RoslynMcp.Host.Stdio/Tools/OperationTools.cs:20`

## Acceptance

- [ ] All five listed endpoints call ClientRootPathValidator.ValidatePathAgainstRootsAsync before dispatching filePath to the underlying service
- [ ] A path outside configured client roots is rejected with the same error shape sibling tools already produce

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04b-host-analysis-tools::DG5-security-data
