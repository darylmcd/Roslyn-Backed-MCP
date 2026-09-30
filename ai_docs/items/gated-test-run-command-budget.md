# gated-test-run-command-budget — Bound test_run and test_coverage by TestTimeout, not the 2-minute gate timeout

**row:** `gated-test-run-command-budget` · **pri:** `High` · **size:** `M` · **deps:** `gated-build-command-budget`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:281`
- `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs:46-200`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:156-190`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:73-101`
- `tests/RoslynMcp.Tests/TestRunTimeoutWireTests.cs`
- (new) `tests/RoslynMcp.Tests/GateOperationDeadlineTests.cs`

## Acceptance

- [ ] `test_run` and `test_coverage` are bounded by `TestTimeout` (10 minutes), not the 2-minute gate `RequestTimeout`, reusing the release-then-execute pattern established by `gated-build-command-budget`: command phase holds no workspace lock or throttle slot; acquisition stays bounded by `RequestTimeout`.
- [ ] `test_run` keeps its timeout result envelope (`TestRunnerService.cs:156-190`). `test_coverage` keeps its timeout result (`TestCoverageCoordinator.BuildTimeoutResult`, `TestCoverageTools.cs:117-125`) when the command budget expires: `GatedCommandExecutor` throws `TimeoutException`, which the tool's `catch (Exception)` (`:126`) would otherwise turn into the unexpected-error result, so the tool handles it as a timeout.
- [ ] Red-first tests: (a) a gated `test_run` whose command runs 3 minutes completes (today cancelled at 120 s, `heldMs 120101` in the retro evidence); (b) a fake runner that outlives `TestTimeout` yields `failureEnvelope.errorKind: Timeout` for `test_coverage`.
- [ ] `workspaceChangedDuringRun` warning and command-phase duration follow the contract of `gated-build-command-budget` (additive).

## Evidence

- `TestRunTimeoutWireTests` only classifies the gate timeout; it does not lift the cap. Scoped `test_run` calls time out at about 120 s even for a 1-test filter (retro 2026-09-27, 20 occurrences, 4 repos).

## Context

- Seam 2 of split parent `gated-build-test-operation-deadline`. Hotspot siblings: `timeout-error-names-fired-timer` and `test-run-nobuild-msbuild-properties` edit `ValidationTools.cs` / `TestRunnerService.cs`; `test-coverage-unexpected-error-not-iserror` edits the same `test_coverage` catch-all. `ITestRunnerService` has 9 test doubles in 6 test files: prefer default-implemented members over signature changes.
