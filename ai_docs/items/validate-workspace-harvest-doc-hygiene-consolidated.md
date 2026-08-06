# validate-workspace-harvest-doc-hygiene-consolidated — consolidated low-severity doc cleanup in the diagnostic-harvest merge

**row:** `validate-workspace-harvest-doc-hygiene-consolidated` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceValidationOverallStatusTests.cs:17` (builder header omits `Diagnostics` from the enumerated driving slots)
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:511` (the "lost racer" narrative restated near-verbatim in two adjacent comment blocks)

## Acceptance

- [ ] The test builder header comment adds `Diagnostics` to the enumerated slots that drive `MergeErrorDiagnostics` (the dedup assertions in `FailingCompile`/`CancelledCompile` already depend on it).
- [ ] The `CompileCheckService`-vs-`CompilationCache` "lost racer" explanation lives once (on `MergeErrorDiagnostics`); the `ComputeOverallStatus` comment block is reduced to a one-line pointer at it.

## Evidence

- Code-quality re-review of PR #1160 (`validate-workspace-diagnostic-harvest-reconcile` fix cycle): two low-severity doc-drift findings — an incomplete builder-slot enumeration, and a duplicated explanation with two places to drift apart on a future edit.

## Context

Spin-off from the `validate-workspace-diagnostic-harvest-reconcile` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1160). Doc-only hygiene.
