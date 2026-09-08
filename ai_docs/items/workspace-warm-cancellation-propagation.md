# workspace-warm-cancellation-propagation — Fail warm requests on caller cancellation

**row:** `workspace-warm-cancellation-propagation` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceWarmService.cs`
- `tests/RoslynMcp.Tests/WorkspaceWarmServiceTests.cs`

## Acceptance

- [ ] Throw `OperationCanceledException` when the caller token is already canceled or becomes canceled between projects; never return a partial success result.
- [ ] Preserve per-project isolation for non-cancellation failures and continue to propagate cancellation raised inside Roslyn operations.
- [ ] Add one pre-canceled, loaded multi-project workspace regression that observes cancellation and no result projection.

## Evidence

- `WorkspaceWarmService.WarmAsync` currently breaks its loop when `ct.IsCancellationRequested`, then returns a successful partial `WorkspaceWarmResult`, contradicting the service remarks that caller cancellation propagates.
