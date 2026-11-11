# timeout-error-names-fired-timer — Timeout envelopes tell the caller to raise a timer that did not fire

**row:** `timeout-error-names-fired-timer` · **pri:** `Medium` · **size:** `L` · **deps:** `timeout-command-restore-timer-identity`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:134-136`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:623`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:634-639`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:138-310`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:57-102`
- `src/RoslynMcp.Core/Services/IGatedCommandExecutor.cs`
- `src/RoslynMcp.Roslyn/Services/BuildService.cs:32-75`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:140-320`
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs:245-260`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:777-816`
- `src/RoslynMcp.Core/Services/OperationTimeoutException.cs`
- `tests/RoslynMcp.Tests/TestRunTimeoutWireTests.cs`
- (new) `tests/RoslynMcp.Tests/TimeoutEnvelopeKnobTests.cs`

## Acceptance

- [ ] Through the public envelopes, verify each timeout identifies the timer that fired, its configured budget and the applicable knob. Cover build, test, vulnerability scan, TUnit filter restore and fork restore. The dependency row owns timer propagation through those command and restore paths.
- [ ] Preserve the existing request-timeout envelope and its `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS` hint. Untyped timeouts keep the generic fallback without a knob.
- [ ] Verify `test_run`'s result envelope identifies `ROSLYNMCP_TEST_TIMEOUT_SECONDS` when the test command budget fires. Keep literal caller text, paths and command arguments out of public timeout messages.
- [ ] Preserve category `Timeout`, the released `isError` behavior and `exceptionType: TimeoutException` for typed and untyped failures. Timer identity travels on the typed exception.
- [ ] Observe the command/restore wire regressions on the unchanged implementation, then pass them with the dependency's changes. Retain the existing request and generic fallback controls.

## Evidence

- Before PR #2035, `WorkspaceExecutionGate.cs:146-150` and `:305-309` threw `TimeoutException("... request timeout of 120s. Increase ROSLYNMCP_REQUEST_TIMEOUT_SECONDS ...")`. `ToolErrorHandler.cs:134-136` mapped every `TimeoutException` to "The operation timed out. For build/test operations, increase ROSLYNMCP_BUILD_TIMEOUT_SECONDS or ROSLYNMCP_TEST_TIMEOUT_SECONDS. For other operations, increase ROSLYNMCP_REQUEST_TIMEOUT_SECONDS.", which discarded the specific message. Retro 2026-09-27 envelope: `{"category":"Timeout","tool":"test_run","message":"The operation timed out. For build/test operations, increase ...","heldMs":120101}`.
- `GatedCommandExecutor.cs:122` has three callers at `19ccd61b`, and it throws the same untyped `TimeoutException` for all of them:
  - `BuildService` (`BuildTimeout`) lets it propagate.
  - `TestRunnerService` turns its `TestTimeout` run into a result envelope (`:156-190`) and lets its `BuildTimeout` restore (`:314`) propagate.
  - `NuGetDependencyService.cs:250-255` (`VulnerabilityScanTimeout`, knob bound at `Program.cs:245`, `:255-256`) lets it propagate.
- Other sources: `WorkspaceForkApplyService.cs:533` (fork restore, knob `ROSLYNMCP_FORK_RESTORE_TIMEOUT_MINUTES`) and `ScriptWorkerProcess.cs:59`, `:93`, `:243` (constant bounds).

- PR #2035 (`89626676`) delivered the typed request-gate timeout and generic no-knob fallback. Retain those behaviors when wiring command and fork timeouts.

## Context

- Remaining scope is command and fork timers. Request timers, the existing wire type mapping and the generic fallback are preservation checks.
- Split by command and fork budget owner before implementing. Keep this umbrella dependent on the concrete wiring work.

## Notes

2026-10-15 live witness: Roslyn MCP test_run of change-signature/catalog related classes returned category Timeout, heldMs120050, with generic build/test/request-knob text and no result counts. Bounded12-case runtime probes succeeded before this broader call. Keep using MCP for bounded probes; the mandatory full unsharded just ci owns complete suite proof. No timeout increase or passing broad-call claim.
