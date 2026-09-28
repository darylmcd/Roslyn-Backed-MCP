# gated-build-test-operation-deadline — Gated build/test/coverage/scan calls are cancelled by the 2-minute request timeout, not their own timeouts

**row:** `gated-build-test-operation-deadline` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs:46-200`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:26-97`
- `src/RoslynMcp.Host.Stdio/Tools/SecurityTools.cs:60-80`
- `src/RoslynMcp.Roslyn/Services/BuildService.cs:32-75`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:103-265`
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs:235-260`
- `tests/RoslynMcp.Tests/TestRunTimeoutWireTests.cs`
- (new) `tests/RoslynMcp.Tests/GateOperationDeadlineTests.cs`

## Acceptance

- [ ] Rule: a gated tool call whose long phase is an out-of-process `dotnet` command is bounded by that command's own budget, not the 2-minute `RequestTimeout`. In scope: `build_workspace` and `build_project` (`BuildTimeout`, 5 minutes); `test_run` (`TestTimeout`, 10 minutes); `validate_workspace`, `validate_recent_git_changes` and `workspace_fork_apply` with `runTests=true` (`TestTimeout`); and `nuget_vulnerability_scan` (`VulnerabilityScanTimeout`, 5 minutes, `ValidationServiceOptions.cs:38`; it runs under `gate.RunReadAsync` at `SecurityTools.cs:73`). `test_coverage` is also in scope. Today it calls `IDotnetCommandRunner` directly with only the gate's token (`TestCoverageTools.cs:167`, `:192`) and has no command budget, so it moves onto `GatedCommandExecutor` under `TestTimeout`.
- [ ] The command phase holds neither the per-workspace reader/writer lock nor a global throttle slot. The tool resolves its inputs under the gate, releases it, and runs the command through `GatedCommandExecutor`, whose global and per-workspace command gates already serialize commands (`GatedCommandExecutor.cs:73-84`). It re-enters the read gate only for post-run work that reads the workspace, such as build diagnostic span enrichment (`BuildService.cs:42`). If the workspace is closed or reloaded during the command, post-run work degrades to un-enriched output and says so; it does not fail the call.
- [ ] `workspace_fork_apply` releases the source workspace's writer lock (`ValidationBundleTools.cs:83`) once the fork is copied and the preview replayed. The fork's restore, load, validation and test phases (`WorkspaceForkApplyService.cs:224-243`) run without holding the source lock.
- [ ] Acquisition (rate limiter, global throttle, per-workspace lock, auto-reload) stays bounded by `RequestTimeout` and is never charged to the command budget. `_meta.gate` keeps reporting `queuedMs` and `heldMs` for the gated phases, and the result reports the command phase's duration separately, so a caller can tell a lock wait from a slow command.
- [ ] Red-first tests on a fake clock: (a) a gated `test_run` whose command runs 3 minutes completes (today it is cancelled at 120 s: `heldMs 120101` in the retro evidence); (b) a writer on the same workspace that arrives 30 s into that run gets its lock without waiting for the run to finish (today it waits for the whole hold); (c) a `workspace_fork_apply` with `runTests=true` does not block a source-workspace write while the fork's tests run (today it holds the source writer lock throughout).
- [ ] `RequestTimeout` keeps its default and still bounds every other tool; raising it globally is not the fix.

## Evidence

- `WorkspaceExecutionGate.cs:216` arms `new CancellationTokenSource(_requestTimeout, _timeProvider)` before the throttle and lock waits and links it into the action token. `:255` re-arms it only after an auto-reload, and `:305-309` reclassifies its expiry as `TimeoutException`. `ExecutionGateOptions.cs:52` defaults `RequestTimeout` to 2 minutes.
- The inner budget already exists for three of the tools: `GatedCommandExecutor.cs:74-75` arms the caller's budget (`BuildTimeout` / `TestTimeout` / `VulnerabilityScanTimeout`, `ValidationServiceOptions.cs:12`, `:18`, `:38`). It runs inside the outer gate, so the 2-minute request deadline always fires first.
- `ValidationTools.cs:33` (build_workspace), `:70` (build_project) and `:317` (test_run), `TestCoverageTools.cs:57`, `SecurityTools.cs:73` and `ValidationBundleTools.cs:35` and `:59` all run under `RunReadAsync`. `workspace_fork_apply` runs under `RunWriteAsync` (`ValidationBundleTools.cs:83`). Each call also holds one of the `max(2, ProcessorCount)` global throttle slots (`WorkspaceExecutionGate.cs:52`, `:69`). Nito's `AsyncReaderWriterLock` queues new readers behind a waiting writer, so a long hold stalls reads too. Lifting only the deadline would stretch each of these holds from 2 minutes to as long as 10 minutes.
- Retro 2026-09-27: scoped `test_run` calls time out at about 120 s even for a 1-test filter, while the same filter runs through the CLI in about a minute (20 occurrences, 4 repos). `TestRunTimeoutWireTests` only classifies the gate timeout; it does not lift the cap.

## Context

- Siblings split from the same retro issue: `timeout-error-names-fired-timer` (wrong knob in the message) and `test-run-nobuild-msbuild-properties` (no build skip). Both edit `ValidationTools.cs` or `TestRunnerService.cs`, so the planner's hotspot ordering sequences them.
- `validation-tools-error-envelope-not-iserror` (owned by the active plan `20260926T234932Z_backlog-remediate`) edits the ValidationTools catch-all; rebase on it if it lands first.
- The load gate (`workspace_load` / `workspace_reload` auto-restore) is out of scope here; `compile-check-restore-required-handshake` bounds its restore inside the load's own budget.
- Longer term, `tasks-extension-build-test-run` (Low) adds the async task mode; it does not replace this fix.

## Notes

- Size L on purpose. Split seams, each independently shippable once the first establishes the release-then-execute pattern: (1) `build_workspace` / `build_project` (`BuildService.cs`, `ValidationTools.cs`); (2) `test_run` and `test_coverage` (`TestRunnerService.cs`, `ValidationTools.cs`, `TestCoverageTools.cs`); (3) the `runTests=true` paths of `validate_*` and `workspace_fork_apply` (`WorkspaceValidationService.cs`, `ValidationBundleTools.cs`, `WorkspaceForkApplyService.cs`); (4) `nuget_vulnerability_scan` (`NuGetDependencyService.cs`, `SecurityTools.cs`).
- The release-then-execute design needs no change to `IWorkspaceExecutionGate`, which 23 test classes in 22 files implement at `19ccd61b`. Widening that interface is the costliest option; avoid it. Service-interface changes have smaller but real fan-out: `ITestRunnerService` has 9 test doubles in 7 files and `IGatedCommandExecutor` has 5 in 4. Prefer default-implemented members over signature changes.
