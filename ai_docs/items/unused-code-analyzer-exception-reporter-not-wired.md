# unused-code-analyzer-exception-reporter-not-wired — `UnusedCodeAnalyzer`'s AnalysisScan failure reporting doesn't wire the DI-resolved `IUnexpectedExceptionReporter`

**row:** `unused-code-analyzer-exception-reporter-not-wired` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:256`

## Acceptance

- [ ] `UnusedCodeAnalyzer`'s public constructor accepts an optional `IUnexpectedExceptionReporter? exceptionReporter = null`, mirroring `CodePatternAnalyzer`'s shape.
- [ ] The `AnalysisScan` catch's `UnexpectedExceptionReporting.Report` call passes the injected reporter instead of a hardcoded `null`.
- [ ] A regression test injects a fake reporter and asserts it is invoked on a per-candidate scan failure.

## Evidence

Cold `implementation-reviewer` re-review of `unused-symbol-scan-fail-unsafe-reference-count` (cycle 2, 2026-09-03): `UnusedCodeAnalyzer.cs:218-221` still passes `reporter: null` into `UnexpectedExceptionReporting.Report(reporter: null, ex, UnexpectedExceptionCategory.AnalysisScan)` — no injection seam exists, so an operator-side reporter can never observe this failure path. Every other production usage of `UnexpectedExceptionCategory.AnalysisScan` in this codebase (`CodePatternAnalyzer.cs`, `DiRegistrationService.cs`, `NuGetDependencyService.cs`, `CohesionAnalysisService.cs`, `ExceptionFlowService.cs`, `SupportedFixEnumerationService.cs`) wires an optional `IUnexpectedExceptionReporter?` constructor parameter that DI resolves in production.

## Context

Surfaced during the fail-safe fix that added the `AnalysisScan` catch in the first place (`unused-symbol-scan-fail-unsafe-reference-count`, merged PR #1438). The catch itself is correct and fixed the original High-severity finding; this row tracks only the reporter-wiring gap, left out of scope deliberately (Directive #6 — smallest complete fix) since it's a pre-existing pattern gap, not part of the fail-unsafe defect being closed.
