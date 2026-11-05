# test-owned-copy-workspace-close-before-delete — Close three private workspaces before deleting copied roots

**row:** `test-owned-copy-workspace-close-before-delete` · **pri:** `Medium` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/DeadCodeIntegrationTests.cs:44` (load), `:77` (delete)
- `tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs:227` (load), `:247` (delete)
- `tests/RoslynMcp.Tests/ValidationIntegrationTests.cs:196` (load), `:215` (delete)
- `tests/RoslynMcp.Tests/TestBase.cs` (shared ordered cleanup steps)

## Acceptance

- [ ] Observe a red-first session-ownership assertion in each actual test on old code, then close each owned workspace before deleting its copied root; all three assertions pass after cleanup.
- [ ] Run close and delete as independent cleanup steps, preserving body and cleanup failures through `FixtureCleanupScope`; cover the no-ID load-failure branch and keep the original scenario assertions.
- [ ] Search the copied-root test pattern for other direct missing-close instances and record the distinction from sequential close/delete failure paths.

## Evidence

- 2026-11-05 remediation follow-up audit: all three methods load a private copied solution through `WorkspaceManager.LoadAsync`, delete its root in `finally`, and omit a matching Close. `TestBase.DisposeServices` is an intentional no-op until assembly cleanup.

## Context

- The earlier diagnostic performance fixture fix (#1977) addressed the same success-path ownership defect in one class. `test-local-manager-dispose-before-copy-delete` tracks method-owned managers disposed after a `finally` deletes their copies. `test-fixture-primary-cleanup-preservation` tracks the separate mechanism where a cleanup failure masks the primary test failure or skips later release steps.
- These four test files land together because the three affected classes share one cleanup contract in `TestBase`; shipping only some callers would leave the same assembly-owned leak behind.
