# host-tools-cohesion-split — Split low-cohesion Host tool files by responsibility

**row:** `host-tools-cohesion-split` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:1-547`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:897-987`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:23-139`

## Acceptance

- [ ] AdvancedAnalysisTools.cs no longer hosts all 11 unrelated tool endpoints in a single class/file
- [ ] TryDisambiguateMetadataNameAsync elicitation logic moves to its own collaborator outside SymbolTools.cs
- [ ] Behavior/tool contract unchanged (existing tests still pass)

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04b-host-analysis-tools::DG1-design, S04b-host-analysis-tools::DG2-cleanliness
