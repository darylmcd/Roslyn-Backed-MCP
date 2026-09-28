# gated-build-test-operation-deadline — Gated build/test/coverage/scan calls are cancelled by the 2-minute request timeout, not their own timeouts

**row:** `gated-build-test-operation-deadline` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs:46-200`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:26-97`
- `src/RoslynMcp.Host.Stdio/Tools/SecurityTools.cs:60-80`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:648-680`
- `src/RoslynMcp.Roslyn/Services/BuildService.cs:32-75`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:103-265`
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs:235-260`
- `docs/decisions/0010-validation-verdict-completeness.md`
- `tests/RoslynMcp.Tests/TestRunTimeoutWireTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceValidationVerdictTests.cs:69-109`
- (new) `tests/RoslynMcp.Tests/GateOperationDeadlineTests.cs`

## Acceptance

- [ ] Rule: a gated tool call whose long phase is an out-of-process `dotnet` command is bounded by that command's own budget, not the 2-minute `RequestTimeout`. In scope: `build_workspace` and `build_project` (`BuildTimeout`, 5 minutes); `test_run` (`TestTimeout`, 10 minutes); `validate_workspace`, `validate_recent_git_changes` and `workspace_fork_apply` with `runTests=true` (`TestTimeout`); and `nuget_vulnerability_scan` (`VulnerabilityScanTimeout`, 5 minutes, `ValidationServiceOptions.cs:38`; it runs under `gate.RunReadAsync` at `SecurityTools.cs:73`). `test_coverage` is also in scope. Today it calls `IDotnetCommandRunner` directly with only the gate's token (`TestCoverageTools.cs:167`, `:192`) and has no command budget, so it moves onto `GatedCommandExecutor` under `TestTimeout`.
- [ ] The `validate_*` test phase runs under `TestTimeout`, outside the 25 s validation phase cap. `WorkspaceValidationService` wraps the related-test run in `RunValidationPhaseAsync("test_run", ...)` (`:358-362`), which arms `_validationPhaseTimeout` (`:741`; the 25 s `DefaultValidationPhaseTimeout`, `:25`, `:63`). The compile, diagnostics and related-test discovery phases keep that cap. The same applies to `workspace_fork_apply` with `runTests=true` and no `testFilter`, which runs its tests through `ValidateAsync` (`WorkspaceForkApplyService.cs:229-233`).
- [ ] A test phase that ends in the run's own timeout envelope yields `overallStatus: timeout`, as the phase cap does today, and never `test-failure`. `DotnetOutputParser.BuildTimeoutResult` (`DotnetOutputParser.cs:142-159`) returns `Failed: 1` with `ErrorKind: Timeout`, and `ComputeOverallStatus` (`WorkspaceValidationService.cs:540-557`) would otherwise report it as a failing test. `ComputeOverallStatus` maps a test run whose envelope has `ErrorKind: Timeout` to `timeout` after `compile-error` and `analyzer-error` and before `test-failure`, keeping ADR 0010's precedence. Unlike the phase-cap result, the result keeps its real compile result, diagnostics, discovered tests and filter. It adds a warning that names phase `test_run` and `ROSLYNMCP_TEST_TIMEOUT_SECONDS`. The runner's envelope passes through unchanged, including `IsRetryable: false` (`DotnetOutputParser.cs:145`); its summary already tells the caller to narrow the filter or raise the test timeout (`TestRunnerService.cs:175-177`).
- [ ] Re-point `WorkspaceValidationVerdictTests.TestPhaseTimeout_IsRetryable_AndCallerCancellationStillPropagates` (`:72`), which pins the 50 ms phase cap on the test phase today:
  - Its runner returns `DotnetOutputParser.BuildTimeoutResult(...)`, the production envelope, and the service is built without the short `phaseTimeout`.
  - Keep these assertions unchanged: the runner was entered, `overallStatus` is `timeout`, `ChangedFilePaths`, `FailureEnvelope.ErrorKind` is `Timeout`, a warning names `'test_run'`, and the whole caller-cancellation half.
  - Change one assertion: `FailureEnvelope.IsRetryable` becomes `IsFalse`, because the envelope is now the runner's. Rename the method to drop `IsRetryable`, for example `TestPhaseTimeout_ReportsTimeoutVerdict_AndCallerCancellationStillPropagates`.
  - The re-pointed test is red today: the envelope's `Failed: 1` yields `test-failure`.
  - The phase cap and its `IsRetryable: true` stay pinned for the compile phase by `WorkspaceValidationTimeoutTests.cs:76-86` and `ValidateRecentGitChangesTests.cs:369-371`.
- [ ] The command phase holds neither the per-workspace reader/writer lock nor a global throttle slot. The tool resolves its inputs under the gate, releases it, and runs the command through `GatedCommandExecutor`, whose global and per-workspace command gates already serialize commands (`GatedCommandExecutor.cs:73-84`). It re-enters the read gate only for post-run work that reads the workspace, such as build diagnostic span enrichment (`BuildService.cs:42`). If the workspace is closed or reloaded during the command, post-run work degrades to un-enriched output and says so; it does not fail the call.
- [ ] Every `dotnet restore` against a loaded workspace takes the same per-workspace command gate, so it never overlaps a build or test command on that workspace. Today the load and reload restore calls `IDotnetCommandRunner.RunAsync` directly (`WorkspaceTools.cs:668-672`). Until this row, a reload's writer lock (`:117-121`) waited out a running build's read lock; once the command phase releases that lock, only the command gate keeps `workspace_reload autoRestore=true` from running `dotnet restore` while `dotnet build` or `dotnet test` writes the same `obj/`. Route the restore helper through `GatedCommandExecutor` (or a lock shared with it). `compile-check-restore-required-handshake` states the same requirement for its default restore; whichever row lands first implements it in the shared helper.
- [ ] The command records the workspace version when it starts (`IWorkspaceManager.GetCurrentVersion`, `Contracts/IWorkspaceManager.cs:148`). If the version moved before post-run enrichment, because a writer applied a change mid-run, the result carries an additive warning such as `workspaceChangedDuringRun`. `docs/release-policy.md` treats additive status fields as minor-compatible.
- [ ] `workspace_fork_apply` releases the source workspace's writer lock (`ValidationBundleTools.cs:83`) once the fork is copied and the preview replayed. The fork's restore, load, validation and test phases (`WorkspaceForkApplyService.cs:224-243`) run without holding the source lock.
- [ ] Acquisition (rate limiter, global throttle, per-workspace lock, auto-reload) stays bounded by `RequestTimeout` and is never charged to the command budget. `_meta.gate` keeps reporting `queuedMs` and `heldMs` for the gated phases, and the result reports the command phase's duration separately, so a caller can tell a lock wait from a slow command.
- [ ] Red-first tests (fake clock or fake runners):
  - (a) A gated `test_run` whose command runs 3 minutes completes. Today it is cancelled at 120 s (`heldMs 120101` in the retro evidence).
  - (b) A writer on the same workspace that arrives 30 s into that run gets its lock without waiting for the run to finish. Today the writer waits for the whole hold. When that writer applies a change, the run's result carries `workspaceChangedDuringRun`; a run with no intervening change carries no such warning.
  - (c) A `workspace_fork_apply` with `runTests=true` does not block a source-workspace write while the fork's tests run. Today it holds the source writer lock throughout.
  - (d) `validate_workspace` with `runTests=true`, built through the internal constructor with a short `validationPhaseTimeout` and a fake test runner that outlasts it, completes with the runner's result. Today it returns `overallStatus: timeout` naming phase `test_run`.
  - (e) A `workspace_reload autoRestore=true` that arrives mid-build waits for the build command before its restore starts. The wait stays bounded by the reload's own request budget, as its writer-lock wait is today.
- [ ] `RequestTimeout` keeps its default and still bounds every other tool; raising it globally is not the fix.
- [ ] Compatibility class: minor-compatible (additive) under `docs/release-policy.md:19` and `:27-28`, so the row ships on the 4.x line. No request parameter, response field or error category is removed or changes meaning:
  - Stable `build_workspace`, `build_project`, `test_run` and `nuget_vulnerability_scan` keep their shipped outcomes; only which timer fires changes. A command-budget expiry keeps category `Timeout` (`GatedCommandExecutor.cs:95-101` throws `TimeoutException`), and `test_run` keeps its timeout result envelope (`TestRunnerService.cs:156-190`).
  - Stable `test_coverage` keeps its timeout result (`TestCoverageCoordinator.BuildTimeoutResult`, returned at `TestCoverageTools.cs:117-125`) when the new command budget expires. `GatedCommandExecutor` throws `TimeoutException`, which the tool's `catch (Exception)` (`:126`) would otherwise turn into the unexpected-error result, so the tool handles it as a timeout. This holds whichever of this row and `test-coverage-unexpected-error-not-iserror` lands first. Test: a fake runner that outlives `TestTimeout` yields `failureEnvelope.errorKind: Timeout`.
  - `workspaceChangedDuringRun` and any command-phase duration field are additive, and `_meta.gate` keeps its keys.
  - Experimental `validate_workspace`, `validate_recent_git_changes` and `workspace_fork_apply` (`ServerSurfaceCatalog.Analysis.cs:34-36`) change one value: a test-phase timeout carries the runner's `IsRetryable: false` instead of ADR 0010's internal-cap `IsRetryable: true`. Amend ADR 0010's test-phase paragraph (`docs/decisions/0010-validation-verdict-completeness.md`, Decision) in the same PR. Its compile, diagnostics and discovery phases keep the retryable cap.

## Evidence

- `WorkspaceExecutionGate.cs:216` arms `new CancellationTokenSource(_requestTimeout, _timeProvider)` before the throttle and lock waits and links it into the action token. `:255` re-arms it only after an auto-reload, and `:305-309` reclassifies its expiry as `TimeoutException`. `ExecutionGateOptions.cs:52` defaults `RequestTimeout` to 2 minutes.
- The inner budget already exists for three of the tools: `GatedCommandExecutor.cs:74-75` arms the caller's budget (`BuildTimeout` / `TestTimeout` / `VulnerabilityScanTimeout`, `ValidationServiceOptions.cs:12`, `:18`, `:38`). It runs inside the outer gate, so the 2-minute request deadline always fires first.
- At `02db6c49`, `build_workspace` (`ValidationTools.cs:33`), `build_project` (`:63`), `test_run` (`:281`), `test_coverage` (`TestCoverageTools.cs:57`), `nuget_vulnerability_scan` (`SecurityTools.cs:73`) and `validate_workspace` (`ValidationBundleTools.cs:35`) run under `RunReadAsync`. `validate_recent_git_changes` (`:59`) reaches it through `ToolDispatch.ReadByWorkspaceIdAsync`, and `workspace_fork_apply` runs under `RunWriteAsync` (`:83`). Each call also holds one of the `max(2, ProcessorCount)` global throttle slots (`WorkspaceExecutionGate.cs:52`, `:69`). Nito's `AsyncReaderWriterLock` queues new readers behind a waiting writer (`AsyncReaderWriterLockRegistry.cs:11`), so a long hold stalls reads too.
- Lifting only the request deadline would stretch the build, `test_run`, `test_coverage` and scan holds, and a fork's explicit-filter test run, from 2 minutes to as long as 10 minutes. The `validate_*` related-test runs are cut sooner: their 25 s phase cap fires before both `RequestTimeout` and `TestTimeout`.
- The load and reload restore (`WorkspaceTools.cs:648-680`) bypasses `GatedCommandExecutor`, so today only the RW lock keeps it apart from a running build. `workspace_load` holds no per-workspace lock at all.
- Retro 2026-09-27: scoped `test_run` calls time out at about 120 s even for a 1-test filter, while the same filter runs through the CLI in about a minute (20 occurrences, 4 repos). `TestRunTimeoutWireTests` only classifies the gate timeout; it does not lift the cap.

## Context

- Siblings split from the same retro issue: `timeout-error-names-fired-timer` (wrong knob in the message) and `test-run-nobuild-msbuild-properties` (no build skip). Both edit `ValidationTools.cs` or `TestRunnerService.cs`, so the planner's hotspot ordering sequences them.
- `validation-tools-error-envelope-not-iserror` landed in #1659 (`31ea3103`) and moved build/test failures to the shared filter; the `ValidationTools.cs` lines above are re-derived after it. `test-coverage-unexpected-error-not-iserror` (Medium) edits the `test_coverage` catch-all in `TestCoverageTools.cs`; sequence by hotspot.
- `compile-check-restore-required-handshake` bounds the load gate's restore inside the load's own budget and adds a default restore through the same helper; the command-gate requirement above is shared with it.
- The 25 s phase cap itself is a literal with no knob; `validation-phase-timeout-knob` (Low) makes it configuration.
- Longer term, `tasks-extension-build-test-run` (Low) adds the async task mode; it does not replace this fix.

## Notes

- Size L on purpose. Split seams, each independently shippable once the first establishes the release-then-execute pattern:
  - (1) `build_workspace` / `build_project` (`BuildService.cs`, `ValidationTools.cs`), with the restore command gate (`WorkspaceTools.cs`), which must land with or before the first release of the RW lock.
  - (2) `test_run` and `test_coverage` (`TestRunnerService.cs`, `ValidationTools.cs`, `TestCoverageTools.cs`).
  - (3) the `runTests=true` paths of `validate_*` and `workspace_fork_apply`, with the test phase moved off the 25 s cap, the timeout verdict and the ADR 0010 amendment (`WorkspaceValidationService.cs`, `ValidationBundleTools.cs`, `WorkspaceForkApplyService.cs`).
  - (4) `nuget_vulnerability_scan` (`NuGetDependencyService.cs`, `SecurityTools.cs`).
- The release-then-execute design needs no change to `IWorkspaceExecutionGate`, which 23 test classes in 22 files implement (re-counted at `02db6c49`). Widening that interface is the costliest option; avoid it. Service-interface changes have smaller but real fan-out: `ITestRunnerService` has 9 test doubles in 6 test files, and `IGatedCommandExecutor` has 5 in 4. Prefer default-implemented members over signature changes.
