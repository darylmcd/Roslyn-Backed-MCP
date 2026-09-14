# diagnostic-info-floor-vacuous-regression

**row:** `diagnostic-info-floor-vacuous-regression` · **pri:** `Low` · **size:** `S` · **deps:** —

## Anchors

- tests/RoslynMcp.Tests/DiagnosticServiceFilterTotalsTests.cs

## Acceptance

- [ ] Provide a real deterministic Info diagnostic and require it to survive the default severity floor; never skip assertions conditionally.

## Evidence

GetDiagnosticsAsync_DefaultSeverityFloorIncludesInfoRowsWhenPresent asserts only inside if TotalInfo > 0, allowing a false pass with no Info fixture.
Observed during direct remediation on 2026-09-14.
