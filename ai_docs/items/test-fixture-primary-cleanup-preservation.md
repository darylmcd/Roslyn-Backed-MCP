# test-fixture-primary-cleanup-preservation — Preserve primary failures while attempting all owned cleanup

**row:** `test-fixture-primary-cleanup-preservation` · **pri:** `Medium` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/PerformanceBehaviorTests.cs:66`
- `tests/RoslynMcp.Tests/Helpers/CleanupFailureCollector.cs`

## Acceptance

- [ ] Inventory identical primary-plus-cleanup failure paths and group independently correct cohesive batches; advisory file counts do not limit complete root-cause coverage. Exclude existing fork-source-fixture-cleanup-failure-propagation, fork-source-fixture-initialization-cleanup-ownership, process-pipe-worker-probe-cleanup-ownership, workspace-cache-test-close-masks-primary, project-fixture-construction-failure-leaks-root and publish-verifier-timeout-process-ownership; amend their inventory if needed rather than duplicate it. Fixture disposal ownership from closed PR #1953 is complete.
- [ ] Preserve primary failure, attempt each independent owned cleanup and surface secondary failures; retain fixture initialization ownership.
- [ ] Use deterministic primary-plus-cleanup controls; preserve every real scenario assertion and child/workspace/temp ownership.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Retained test patches replace sequential Close/delete finally blocks with CleanupFailureCollector. Current PerformanceBehaviorTests still has sequential cleanup; broader branch-only native fixtures belong to the architectural decision, not current migrations.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
- PR #1953 closed `workspace-cache-fixture-cleanup-failure-silent`; this inventory now points to the remaining distinct cache-close and partial-construction rows.
