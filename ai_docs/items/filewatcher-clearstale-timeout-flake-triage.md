# filewatcher-clearstale-timeout-flake-triage — Triage FileWatcherClearStaleAwaiterTests timeout flake

**row:** `filewatcher-clearstale-timeout-flake-triage` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs:405` (`FileWatcherClearStaleAwaiterTests.ClearStale_ReleasesAwaiterParkedOnPriorSignal_RatherThanStrandingIt`)

## Acceptance

- [ ] Root-cause the `System.TimeoutException` (timing-sensitive assertion vs. genuine bug) and either widen the timing budget, replace with a counter-based assertion, or fix the underlying race
- [ ] Register in `ai_docs/known-flakes.md` via a dedicated PR if confirmed to be a pre-existing timing flake (not a regression)

## Evidence

- PR #1034's CI run failed this test with `System.TimeoutException` on a completely unrelated diff (CodeActionTools/FlowAnalysisTools/OperationTools + test changes, zero touches to FileWatcherService); an unmodified rerun of the same commit passed cleanly. Matches the same class of self-hosted-CI-load timing sensitivity already documented for `WorkspaceExecutionGateTests.AutoReload_ResetsTimeoutBudget_ToolActionGetsFullBudget` in `ai_docs/known-flakes.md`, but this test is not yet registered there.
Second occurrence 2026-07-22: PR #1086 (Directory.Packages.props version-pin-only diff,
zero touches to FileWatcherService or tests) failed validate with the same
System.TimeoutException at ExternalEditStalenessTests.cs:405. Confirmed non-deterministic:
3/3 local reruns passed in ~22ms each (self-hosted runner idle at the time). Matches the
PR #1034 evidence exactly - timing-sensitive under CI load, not a regression. Strengthens
the case to register in known-flakes.md per this row's acceptance criteria #2.
Registered in known-flakes.md 2026-07-22 (acceptance criterion #2 done, via dedicated PR - three occurrences today: PR #1086, PR #1085 CI, both unrelated diffs; plus PR #1034 precedent; plus 3/3 local reruns passing). Criterion #1 (root-cause the timing / widen UnblockBoundMs / counter-based assertion) remains open - row stays open for that.
