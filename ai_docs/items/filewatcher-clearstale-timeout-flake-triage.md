# filewatcher-clearstale-timeout-flake-triage — Triage FileWatcherClearStaleAwaiterTests timeout flake

**row:** `filewatcher-clearstale-timeout-flake-triage` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs:405` (`FileWatcherClearStaleAwaiterTests.ClearStale_ReleasesAwaiterParkedOnPriorSignal_RatherThanStrandingIt`)

## Acceptance

- [ ] Root-cause the `System.TimeoutException` (timing-sensitive assertion vs. genuine bug) and either widen the timing budget, replace with a counter-based assertion, or fix the underlying race
- [ ] Register in `ai_docs/known-flakes.md` via a dedicated PR if confirmed to be a pre-existing timing flake (not a regression)

## Evidence

- PR #1034's CI run failed this test with `System.TimeoutException` on a completely unrelated diff (CodeActionTools/FlowAnalysisTools/OperationTools + test changes, zero touches to FileWatcherService); an unmodified rerun of the same commit passed cleanly. Matches the same class of self-hosted-CI-load timing sensitivity already documented for `WorkspaceExecutionGateTests.AutoReload_ResetsTimeoutBudget_ToolActionGetsFullBudget` in `ai_docs/known-flakes.md`, but this test is not yet registered there.
