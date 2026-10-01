# load-sensitive-timing-tests-full-suite — Find why full-suite load makes timing-sensitive tests fail and fix them deterministically

**row:** `load-sensitive-timing-tests-full-suite` · **pri:** `High` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/MissingWorkspaceRootRetirementTests.cs:117-146`
- `tests/RoslynMcp.Tests/DotnetCommandRunnerPipeLifetimeTests.cs:68-79`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs:250-263`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:566-640`

## Acceptance

- [ ] Reproduce and explain the full-suite load that stretches wall-clock-sensitive tests: capture thread-pool queue length, worker count and machine CPU during a `verify-release` run (local and hosted `windows-hosted-*` shards) and name which parallel test classes saturate the machine.
- [ ] Fix the cause or the specific tests deterministically (fake clock or injected delay, attempt-count assertions, or a lower worker cap for the heavy parallel classes) instead of widening deadlines; a deadline may only be widened where the property under test is "bounded vs unbounded".
- [ ] `TransientGateFailure_RetriesUntilMissingWorkspaceRetires` (10 s `WaitAsync` on a ~1 s production retry, `WorkspaceManager.RetireMissingWorkspaceAsync`) no longer times out under the full suite.
- [ ] Re-audit every test with a fixed wall-clock or ready-marker deadline on a spawned child or timer (`rg 'WaitAsync\(TimeSpan|Elapsed <|CancellationTokenSource\(TimeSpan' tests`), starting with `DotnetCommandRunnerPipeLifetimeTests.cs:68` (10 s CTS around a `pwsh` cold start).

## Evidence

- Plan 20260930T213336Z (2026-09-30/10-01): `MissingWorkspaceRootRetirementTests.TransientGateFailure_RetriesUntilMissingWorkspaceRetires` timed out (17 s) in one local integration gate and passed on rerun; it passed 12/12 in isolation and the clean-main full gate (3378 passed) was green. `SweepAbandonedRoots_LiveOtherProcess_PreservesStaleProcessCopies` failed 3 hosted Windows runs with a 15 s PowerShell cold start (fixed by #1701, `cmd.exe` owner). `AtomicFileWriter_PersistentReaderFailsWithinBound_AndCleansTemp` took ~7 s under the full suite against a ~2.4 s retry budget (2 s in isolation) and failed a local gate and two hosted runs (bound widened to 30 s by #1702).
- A fourth, `WorkspaceWarmServiceTests.WarmAsync_SecondCall_NoCold_FasterThanFirst` (`tests/RoslynMcp.Tests/WorkspaceWarmServiceTests.cs:58`, asserts the second warm is 10x faster than the first), failed the PR 1704 local gate with first=317 ms second=53 ms under full-suite load and passed on rerun. Add it to the Acceptance audit.
- All of these started failing after the 2026-09-29/30 parallelization changes (`DoNotParallelize` audit waves, PR 1684 parallel validation timeout tests).

## Context

- Mechanism row for the pattern; the widened bound in #1702 and the `cmd.exe` owner in #1701 are the per-test mitigations it should replace where a deterministic seam exists.
