# gated-validate-test-phase-budget — Run validate_* and workspace_fork_apply test phases under TestTimeout

**row:** `gated-validate-test-phase-budget` · **pri:** `High` · **size:** `M` · **deps:** `gated-test-run-command-budget`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:358-362`
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:540-557`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:26-97`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:103-265`
- `docs/decisions/0010-validation-verdict-completeness.md`
- `tests/RoslynMcp.Tests/WorkspaceValidationVerdictTests.cs:69-109`

## Acceptance

- [ ] The `validate_workspace`, `validate_recent_git_changes` and `workspace_fork_apply` (`runTests=true`) test phase runs under `TestTimeout`, outside the 25 s validation phase cap. `WorkspaceValidationService` wraps the related-test run in `RunValidationPhaseAsync("test_run", ...)` (`:358-362`), which arms the 25 s `DefaultValidationPhaseTimeout`; compile, diagnostics and related-test discovery keep that cap. The fork's no-`testFilter` tests run through `ValidateAsync` (`WorkspaceForkApplyService.cs:229-233`).
- [ ] A test phase ending in the run's own timeout envelope yields `overallStatus: timeout`, never `test-failure`. `DotnetOutputParser.BuildTimeoutResult` (`DotnetOutputParser.cs:142-159`) returns `Failed: 1` with `ErrorKind: Timeout`; `ComputeOverallStatus` (`:540-557`) maps a test run with `ErrorKind: Timeout` to `timeout` after `compile-error`/`analyzer-error` and before `test-failure`, keeping ADR 0010 precedence.
- [ ] Re-point `WorkspaceValidationVerdictTests.TestPhaseTimeout_IsRetryable_AndCallerCancellationStillPropagates` (`:72`): runner returns `DotnetOutputParser.BuildTimeoutResult(...)`, service built without the short `phaseTimeout`; keep the runner-entered, `overallStatus: timeout`, `ChangedFilePaths`, `ErrorKind: Timeout`, warning naming `'test_run'` and caller-cancellation assertions; `FailureEnvelope.IsRetryable` becomes false; rename to drop `IsRetryable`. The phase cap and its `IsRetryable: true` stay pinned for compile by `WorkspaceValidationTimeoutTests.cs:76-86` and `ValidateRecentGitChangesTests.cs:369-371`.
- [ ] `workspace_fork_apply` releases the source workspace writer lock (`ValidationBundleTools.cs:83`) once the fork is copied and the preview replayed; the fork's restore, load, validation and test phases (`WorkspaceForkApplyService.cs:224-243`) run without the source lock.
- [ ] Red-first tests: (c) a fork apply with `runTests=true` does not block a source-workspace write while the fork's tests run; (d) `validate_workspace runTests=true` built through the internal constructor with a short `validationPhaseTimeout` and a fake test runner that outlasts it completes with the runner's result.
- [ ] Amend ADR 0010's test-phase paragraph in the same PR: a test-phase timeout carries the runner's `IsRetryable: false` instead of the internal cap's `true`. Compatibility: experimental tools (`ServerSurfaceCatalog.Analysis.cs:34-36`) change this one value; everything else additive.

## Evidence

- The `validate_*` related-test runs are cut by the 25 s phase cap before both `RequestTimeout` and `TestTimeout`; `workspace_fork_apply` runs under `RunWriteAsync` (`ValidationBundleTools.cs:83`) and holds the source writer lock throughout its fork tests.

## Context

- Seam 3 of split parent `gated-build-test-operation-deadline`; depends on the test-command budget seam. `validation-phase-timeout-knob` (Low) makes the 25 s cap configuration.
