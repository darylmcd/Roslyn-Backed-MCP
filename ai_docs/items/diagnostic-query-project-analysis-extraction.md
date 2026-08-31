# diagnostic-query-project-analysis-extraction — Extract diagnostic project analysis

**row:** `diagnostic-query-project-analysis-extraction` · **pri:** `Low` · **size:** `M` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticQueryService.cs`
- New `src/RoslynMcp.Roslyn/Services/DiagnosticProjectAnalyzer.cs`
- `tests/RoslynMcp.Tests/DiagnosticQueryServiceRegressionTests.cs`
- `tests/RoslynMcp.Tests/DiagnosticServiceFilterTotalsTests.cs`

## Acceptance

- [ ] Move per-project compiler/analyzer collection and effective analyzer-severity resolution into one internal collaborator; keep `DiagnosticQueryService` focused on query scope, aggregation, and versioned caching.
- [ ] Preserve syntax-tree, global analyzer-config, command-line specific-option, general-option, descriptor-default, and disabled-by-default precedence.
- [ ] Preserve cancellation, the proven Error-only analyzer-pass fast path, raw diagnostic reuse, filter totals, and current DTO output.
- [ ] Add one table-driven effective-severity regression matrix; retain the existing end-to-end filter and cache regressions.

## Evidence

The 2026-08-31 adjacent review measured `GetEffectiveReportDiagnostic` at cyclomatic complexity 17 and `CollectProjectDiagnosticsAsync` at 88 lines inside the 529-line query/cache service. One bounded collaborator can own the coupled project-analysis policy without changing the public MCP contract.
