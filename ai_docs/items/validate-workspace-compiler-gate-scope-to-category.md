# validate-workspace-compiler-gate-scope-to-category — scope validate_workspace's compiler-arm corroboration gate to Category=="Compiler" rows

**row:** `validate-workspace-compiler-gate-scope-to-category` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:546-553` (`MergeErrorDiagnostics`)
- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs:181-189` (`WORKSPACE001` — `Category="Workspace"`, appended before any filtering)
- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs:107` (flows into `DiagnosticsResultDto.CompilerDiagnostics`)

## Acceptance

- [ ] `MergeErrorDiagnostics` gates only rows whose `Category=="Compiler"`; non-Compiler rows in `diagResult.CompilerDiagnostics` (today: `WORKSPACE001`) merge regardless of compile authority.
- [ ] Unit test: complete + green `CompileCheckDto` (`Cancelled=false`, `CompletedProjects==TotalProjects`, `ErrorCount==0`) plus a `WORKSPACE001` `Category=="Workspace"` row in `CompilerDiagnostics` → merged length 1 and `ComputeOverallStatus != "clean"`.

## Evidence

- Code-quality re-review of PR #1160 (`validate-workspace-diagnostic-harvest-reconcile` fix cycle): traced at code level — `DiagnosticService.cs:183-188` appends `WORKSPACE001` (`Severity="Error"`, `Category="Workspace"`) ahead of all severity/id filtering; `CompileCheckService.cs:141` increments `CompletedProjects` for the same null-compilation project without touching `ErrorCount`, so `compileIncomplete` is false and the row is silently dropped by the new gate. Pre-diff that row reached `errors` and yielded `analyzer-error`; post-diff it yields `clean`.

## Context

Spin-off from the `validate-workspace-diagnostic-harvest-reconcile` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1160), found during the Step 8b fix-cycle re-review. Not blocking (medium, not high) — the reviewer notes the precondition (whether `GetCompilationAsync` actually returns null for any project MSBuildWorkspace loads) is unverified, which is why this is medium not high.
