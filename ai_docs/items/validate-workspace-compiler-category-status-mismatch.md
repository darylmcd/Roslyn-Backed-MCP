# validate-workspace-compiler-category-status-mismatch — reconcile dual error sources behind validate_workspace's overallStatus

**row:** `validate-workspace-compiler-category-status-mismatch` · **pri:** `High` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:203-215` (`allErrors` merge — `compile.Diagnostics` concatenated with a SEPARATE `_diagnostics.GetDiagnosticsAsync(severityFilter: "Error")` call's `CompilerDiagnostics` + `AnalyzerDiagnostics`)
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:488-504` (`ComputeOverallStatus` — returns `"compile-error"` if `compile.ErrorCount > 0 || errors.Any(d => Category=="Compiler")`)
- tests under `tests/RoslynMcp.Tests/Services/WorkspaceValidationServiceTests.cs` (or equivalent)

## Acceptance

- [ ] Root cause confirmed: trace a case where `diagResult.CompilerDiagnostics` (the second, analyzer-service-sourced harvest) contains a `Category=="Compiler"` `Error`-severity entry that `compile.ErrorCount` (the first, compile_check-sourced harvest) does not count — likely a staleness/timing difference between the two independent calls, or a definitional mismatch in what each considers "Compiler" category
- [ ] `ComputeOverallStatus`'s `errors.Any(d => Category=="Compiler")` branch no longer fires when `compile.ErrorCount == 0` and the two sources disagree — either reconcile against `compile.Diagnostics` explicitly, or treat a lone second-source Compiler-category hit as `analyzer-error` (not `compile-error`) since it did not come from the actual compile pass
- [ ] Regression test: construct a workspace state where `compile.ErrorCount == 0` but the diagnostics harvest independently returns a `Category=="Compiler"` error, assert `overallStatus != "compile-error"`

## Evidence

- `ai_docs/reports/20260805T210025Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §2a `validate_workspace` row + §3 pattern 4. 1 codex session (`019f9781`), deterministic — reproduced on all 5 `validate_workspace` calls in that session, reporting `overallStatus: compile-error` with `Compile errors: 0`, `Compile warnings: 0`, `Analyzer/compile error diagnostics: 1`, and `Tests passed: 35/35`.

## Context

This is adjacent to, but distinct from, the already-shipped `validate-workspace-overallstatus-false-positive` fix (v1.28.1, 2026-04-22, `WorkspaceValidationService.cs`), which fixed `overallStatus` keying off `!Success` alone instead of `ErrorCount`. That fix is confirmed live in the current code (`ComputeOverallStatus` at line 494 does check `compile.ErrorCount > 0`). The residual bug is the SECOND disjunct on the same line: `errors.Any(d => Category=="Compiler")`, where `errors` is `allErrors` — a list built from TWO independently-called sources (`compile.Diagnostics` from `compile_check`, and a separate `_diagnostics.GetDiagnosticsAsync` call). When the second source disagrees with the first (reports a Compiler-category error the first source's `ErrorCount` doesn't reflect), the tool reports `compile-error` on what the caller correctly perceives as a clean build — forcing every caller to duplicate the check via a separate `compile_check` call to get a trustworthy answer, defeating `validate_workspace`'s purpose as a single aggregate gate.

## Notes

The exact diagnostic ID/message behind the phantom "1" count was not captured in the source retro (only the aggregate counts were visible in the compact table). The acceptance criteria above assume the root cause is a same-severity-different-source disagreement; if tracing reveals a different mechanism, update this file before closing.
