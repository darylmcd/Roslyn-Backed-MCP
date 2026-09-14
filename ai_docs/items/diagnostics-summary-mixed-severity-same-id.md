# diagnostics-summary-mixed-severity-same-id

**row:** `diagnostics-summary-mixed-severity-same-id` · **pri:** `Low` · **size:** `S`

## Anchors

- src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs
- tests/RoslynMcp.Tests/DiagnosticServiceFilterTotalsTests.cs

## Acceptance

- [ ] Define and test summary grouping for the same ID reported as both Error and Warning in one solution; do not label the combined count with the first observed severity.

## Evidence

GetProjectDiagnostics groups only by Id and copies severity from First(), even though analyzer severity can vary by project configuration.
Observed during diagnostic reliability remediation on 2026-09-14.
