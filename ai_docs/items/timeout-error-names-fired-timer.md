# timeout-error-names-fired-timer — Timeout envelopes tell the caller to raise a timer that did not fire

**row:** `timeout-error-names-fired-timer` · **pri:** `Medium` · **size:** `L`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:134-136`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:138-310`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:57-102`
- `src/RoslynMcp.Core/Services/IGatedCommandExecutor.cs`
- `src/RoslynMcp.Roslyn/Services/BuildService.cs:32-75`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:140-320`
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs:245-260`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:355-360`
- (new) `src/RoslynMcp.Core/Services/OperationTimeoutException.cs`
- `tests/RoslynMcp.Tests/TestRunTimeoutWireTests.cs`
- (new) `tests/RoslynMcp.Tests/TimeoutEnvelopeKnobTests.cs`

## Acceptance

- [ ] A Core typed timeout exception carries which timer fired, its env knob, and the configured and elapsed time. Each source maps to one knob:
  - The gate's request timeouts (`WorkspaceExecutionGate.cs:146`, `:305`) → `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS`.
  - `build_workspace` / `build_project` commands (`BuildService.cs:36`, `:57`), and `test_run`'s TUnit OR-filter restore, which runs under `BuildTimeout` (`TestRunnerService.cs:314-319`) → `ROSLYNMCP_BUILD_TIMEOUT_SECONDS`.
  - `test_run` commands (`TestRunnerService.cs:148-153`) → `ROSLYNMCP_TEST_TIMEOUT_SECONDS`.
  - `nuget_vulnerability_scan` commands (`NuGetDependencyService.cs:250-255`) → `ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS`.
  - The `workspace_fork_apply` restore (`WorkspaceForkApplyService.cs:533`) → `ROSLYNMCP_FORK_RESTORE_TIMEOUT_MINUTES` (`:355-359`).
- [ ] Each caller of `GatedCommandExecutor.ExecuteAsync` passes the timer identity along with its budget, and the executor never infers it from the budget value or the arguments. The identity arrives through a default-implemented `IGatedCommandExecutor` member (or an equivalent that keeps the signature), so the 5 test doubles in 4 files need no change.
- [ ] `ToolErrorHandler` renders the timer, its knob and the configured time. A `TimeoutException` without timer identity gets a generic timeout message that names no knob; the script worker IPC and cleanup bounds (`ScriptWorkerProcess.cs:59`, `:93`, `:243`) are fixed constants with no knob. The handler never guesses a "build/test" hint.
- [ ] `test_run`'s own timeout envelope (`TestRunnerService.cs:175-177`, "raise the configured test timeout") names `ROSLYNMCP_TEST_TIMEOUT_SECONDS`.
- [ ] Rendered messages stay path-free and never echo caller text. The command-budget message at `GatedCommandExecutor.cs:99-100` embeds the full dotnet argument list, including a caller's test filter, so it is never rendered verbatim.
- [ ] Red-first wire tests. A gate request timeout during `test_run` names `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS`; today the envelope leads with the build/test knobs. A `nuget_vulnerability_scan` command timeout names `ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS`; today it gets the same build/test/request text, which never mentions that knob.

## Evidence

- `WorkspaceExecutionGate.cs:146-150` and `:305-309` throw `TimeoutException("... request timeout of 120s. Increase ROSLYNMCP_REQUEST_TIMEOUT_SECONDS ...")`. `ToolErrorHandler.cs:134-136` maps every `TimeoutException` to "The operation timed out. For build/test operations, increase ROSLYNMCP_BUILD_TIMEOUT_SECONDS or ROSLYNMCP_TEST_TIMEOUT_SECONDS. For other operations, increase ROSLYNMCP_REQUEST_TIMEOUT_SECONDS.", which discards the specific message. Retro 2026-09-27 envelope: `{"category":"Timeout","tool":"test_run","message":"The operation timed out. For build/test operations, increase ...","heldMs":120101}`.
- `GatedCommandExecutor.cs:99` has three callers at `19ccd61b`, and it throws the same untyped `TimeoutException` for all of them:
  - `BuildService` (`BuildTimeout`) lets it propagate.
  - `TestRunnerService` turns its `TestTimeout` run into a result envelope (`:156-190`) and lets its `BuildTimeout` restore (`:314`) propagate.
  - `NuGetDependencyService.cs:250-255` (`VulnerabilityScanTimeout`, knob bound at `Program.cs:245`, `:255-256`) lets it propagate.
- Other sources: `WorkspaceForkApplyService.cs:533` (fork restore, knob `ROSLYNMCP_FORK_RESTORE_TIMEOUT_MINUTES`) and `ScriptWorkerProcess.cs:59`, `:93`, `:243` (constant bounds).

## Context

- Independent of `gated-build-test-operation-deadline`: after that lands, a real request timeout on any gated tool still gets the wrong hint.
- Medium: this is message quality. The retro's wrong-knob envelopes came from `test_run` request timeouts (within the 20 occurrences of that retro issue), which `gated-build-test-operation-deadline` (High) removes; this row keeps every remaining timeout honest.

## Notes

- Size L (9 production files). Split seam: (1) the typed exception, the `ToolErrorHandler` rendering, the gate's request timeouts and the generic no-knob fallback (the new Core file, `ToolErrorHandler.cs`, `WorkspaceExecutionGate.cs`) clear the retro symptom on their own; (2) the command budgets and the fork restore name their knobs (`GatedCommandExecutor.cs`, `IGatedCommandExecutor.cs`, `BuildService.cs`, `TestRunnerService.cs`, `NuGetDependencyService.cs`, `WorkspaceForkApplyService.cs`).
