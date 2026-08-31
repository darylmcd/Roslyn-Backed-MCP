# diagnostic-analyzer-descriptor-enumeration-failure-isolation — Isolate analyzer descriptor enumeration failures

**row:** `diagnostic-analyzer-descriptor-enumeration-failure-isolation` · **pri:** `Low` · **size:** `M` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticProjectAnalyzer.cs`
- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs`
- `tests/RoslynMcp.Tests/DiagnosticQueryServiceRegressionTests.cs`

## Acceptance

- [ ] Treat non-cancellation failures from third-party `AnalyzerReference.GetAnalyzers` or `DiagnosticAnalyzer.SupportedDiagnostics` as an unknown fast-path result, so Error-only queries conservatively run the analyzer pass instead of failing during the optimization probe.
- [ ] Propagate cancellation unchanged and report one correlated, path-free unexpected diagnostic through the existing operator observability boundary.
- [ ] Add hostile analyzer-reference and descriptor regressions proving the query remains caller-safe, the analyzer pass is not skipped, and raw exception details are not exposed.

## Evidence

Adjacent review of the extracted Error-only fast path found direct third-party analyzer and descriptor enumeration outside Roslyn's analyzer-driver exception boundary; either accessor can currently abort the entire diagnostic query before the normal analyzer pass begins.
