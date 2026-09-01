# diagnostic-project-analyzer-policy-decomposition — Separate diagnostic analysis policy

**row:** `diagnostic-project-analyzer-policy-decomposition` · **pri:** `Low` · **size:** `M` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticProjectAnalyzer.cs`
- New `src/RoslynMcp.Roslyn/Services/DiagnosticSeverityPolicy.cs`
- `tests/RoslynMcp.Tests/DiagnosticQueryServiceRegressionTests.cs`

## Acceptance

- [ ] Extract effective-severity evaluation and diagnostic collection from project orchestration without changing the public diagnostics contract.
- [ ] Preserve analyzer-config precedence, conservative Error-only fallback, hidden-diagnostic exclusion, and severity-invariant totals.
- [ ] Add one table-driven regression shape that compares compiler, generator, and analyzer projection before and after the extraction.

## Evidence

The 2026-08-31 adjacent semantic review measured `GetEffectiveReportDiagnostic` at cyclomatic complexity 17 and found project orchestration, severity policy, and DTO collection sharing one class. The current reliability fix centralized third-party probe failures but deliberately did not widen into this structural refactor.
