# fork-source-fixture-cleanup-failure-propagation — Preserve lifecycle fixture failures through cleanup

**row:** `fork-source-fixture-cleanup-failure-propagation` · **pri:** `Medium` · **size:** `S` · **verified:** `847d7f94a4f3565da05208010a1397da04f1047f`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs:475` — `ForkFixture.ReapAsync`, HEAD 847d7f94.
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs:484` — `ForkFixture.DisposeAsync`, HEAD 847d7f94.
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs:56` — eight reap callers (56, 86, 115, 153, 239, 268, 304, 359); additional sequential cleanup at 86-87, 115-116, 180-184, 304-305, 354-355 (re-line before editing).
- `tests/RoslynMcp.Tests/Helpers/CleanupFailureCollector.cs` — existing cleanup-failure collector; the lifecycle test file does not use it yet; recheck exact API before use.

## Acceptance

- [ ] Preserve the primary failure and every independent cleanup failure when the behavioral cancellation token has expired; retain the behavioral deadline.
- [ ] Attempt each owned operation, stop, host-run and disposal cleanup independently; assign exactly one actual host/provider disposal owner.
- [ ] Cover every shared expired-token and sequential-cleanup instance. Preserve dependencies and material until their actual owners finish; report and observe pending tasks without claiming they were reaped.
- [ ] Add persistent actual-fixture regressions for expired behavioral cancellation, primary plus cleanup faults, and any newly introduced pending/task-admission branches; observe causal RED before the fix.

## Evidence

- Immutable32de7e8a lifecycle source: ReapAsync470-477 waits with the behavioral Timeout.Token475; its eight callers can mask primary failures or skip subsequent cleanup. Main14fbdc02 differs only by two configured-root DI lines after434; the cleanup mechanism is identical.
- PR1823 hosted run40134012386 Linux1 records only TaskCanceledException from ReapAsync during LastOperationRelease_DuringInitialCancel_DoesNotCompleteShutdownEarly(False), hiding the original failure.
- The callback-rendezvous defect is fixed independently in PR1823. This row is not a restore dependency.
- Experimental cleanup rewrite is preserved at `<user>/.codex/scratch/roslyn-remediate-20261023-split/fork-source-cleanup-d8-preserved.cs`, SHA256d8ec61665fd25f2215a0109446c370d9d599ffe8260c436231e8f6f80ae3a10f. It has unresolved ownership findings and is reference material, not an approved solution.
