# analysis-tools-pagination-clamp-rollout — Wire the new ValidatePagination max-clamp into unbounded analysis-tool call sites

**row:** `analysis-tools-pagination-clamp-rollout` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:640`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:598-601`
- `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:39`
- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:186`
- `src/RoslynMcp.Host.Stdio/Tools/ConsumerAnalysisTools.cs:33`

## Acceptance

- [ ] find_references_bulk validates symbols.Length before performing per-symbol reference walks
- [ ] get_complexity_metrics calls ParameterValidation.ValidatePagination on its limit parameter
- [ ] find_consumers exposes limit/offset consistent with sibling tools (find_type_usages, callers_callees, symbol_relationships)

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04b-host-analysis-tools::DG4-performance, S04b-host-analysis-tools::DG7-config-deps-ergo
