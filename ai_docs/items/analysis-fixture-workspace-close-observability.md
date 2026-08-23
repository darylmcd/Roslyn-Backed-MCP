# analysis-fixture-workspace-close-observability — Surface analysis fixture close failures

**row:** `analysis-fixture-workspace-close-observability` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/FlowAnalysisServiceTests.cs`
- `tests/RoslynMcp.Tests/FindPropertyWritesHintLocatorShapeTests.cs`

## Acceptance

- [ ] Stop suppressing `WorkspaceManager.Close` failures in both class-cleanup paths.
- [ ] Attempt owned copied-root cleanup even when workspace close fails, then preserve the original failure.
- [ ] Add one focused cleanup-failure regression or consolidate through an already-tested fixture cleanup helper.

## Evidence

- 2026-08-23 LocationDto Stage 1 changed-test review found copied bare `catch { }` cleanup in four analysis fixtures. The two touched fixtures were corrected inline; these two remaining fixtures retain the same silent failure path.
