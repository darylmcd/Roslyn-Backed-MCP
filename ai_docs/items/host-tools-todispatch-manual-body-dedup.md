# host-tools-todispatch-manual-body-dedup — Route ChangeSignatureTools/ParameterObjectTools/SuggestionTools through ToolDispatch.ReadByWorkspaceIdAsync

**row:** `host-tools-todispatch-manual-body-dedup` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs:41-49`
- `src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs:41-54`
- `src/RoslynMcp.Host.Stdio/Tools/SuggestionTools.cs:24-29`

## Acceptance

- [ ] all three tool methods call ToolDispatch.ReadByWorkspaceIdAsync instead of duplicating gate.RunReadAsync + JsonSerializer.Serialize(..., JsonDefaults.Indented)

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04a-host-refactor-tools::DG2-cleanliness
