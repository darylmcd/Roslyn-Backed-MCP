# workspace-cache-test-close-masks-primary — Preserve cache test failures during workspace close

**row:** `workspace-cache-test-close-masks-primary` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/MsBuildEvaluationCacheTests.cs` (three `WorkspaceManager.Close` finally blocks)

## Acceptance

- [ ] Attempt workspace close and fixture deletion after a cache test body fails, retaining the original failure and any cleanup failures together.
- [ ] Red-first regressions inject body, workspace close, and fixture deletion failures in combinations and verify all owned cleanup is attempted exactly once.
- [ ] Successful cache evaluation tests keep their existing lifecycle behavior.

## Evidence

- Cold review of PR #1953: each cache test calls `WorkspaceManager.Close` in an inner `finally` before the outer `FixtureCleanupScope` receives the body result. If close throws while the test body already failed, the close exception replaces that primary failure. This close ordering predates the fixture deletion change.
