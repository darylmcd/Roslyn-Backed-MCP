# validate-workspace-diagnostic-harvest-reconcile — reconcile validate_workspace's two diagnostic harvests

**row:** `validate-workspace-diagnostic-harvest-reconcile` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:210-215` (`allErrors` — concats compile.Diagnostics + diagResult.CompilerDiagnostics + AnalyzerDiagnostics)
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:244` (`ErrorCount: allErrors.Length`)
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:508-511` (`ComputeOverallStatus`)
- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs:180`

## Acceptance

- [ ] A `Category=="Compiler"` Error row surfaced ONLY by the `project_diagnostics` harvest, while `compile_check` reports `ErrorCount==0`, no longer enters `allErrors` / `WorkspaceValidationDto.ErrorCount` — the retro's green-build repro yields `overallStatus:"clean"` with `errorCount:0` (not `analyzer-error`/1).
- [ ] Root cause of the harvest disagreement is identified (`DiagnosticService`'s version-keyed compilation cache vs `CompileCheckService`'s direct `GetCompilationAsync`) and either fixed or explicitly documented with a test pinning the reconciliation rule.

## Evidence

- Code-quality review of PR #1140 (`validate-workspace-compiler-category-status-mismatch`): that PR relabeled the verdict (`compile-error` → `analyzer-error`) but `allErrors`/`ErrorCount` still merge the second harvest's `CompilerDiagnostics` — the retro's green-build repro (`ai_docs/reports/20260805T210025Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md:160` — ErrorCount 0, 1 phantom Compiler diagnostic, 35/35 tests green) still reports a non-clean verdict with `errorCount:1`; only the label moved.

## Context

Spin-off from the `validate-workspace-compiler-category-status-mismatch` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1140). Distinct from `compilation-cache-adoption-read-side` (cache adoption at other read sites, not this reconciliation) — checked before filing.
