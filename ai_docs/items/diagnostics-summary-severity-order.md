# diagnostics-summary-severity-order

**row:** `diagnostics-summary-severity-order` · **pri:** `Low` · **size:** `S` · **deps:** —

## Anchors

- src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs
- tests/RoslynMcp.Tests/DiagnosticServiceFilterTotalsTests.cs

## Acceptance

- [ ] Pin mixed-severity groups in Error, Warning, Info order, retaining descending count within each severity.

## Evidence

GetProjectDiagnostics uses OrderByDescending with Error=0, Warning=1, Info=2, putting the least severe groups first.
Observed during direct remediation on 2026-09-14.
