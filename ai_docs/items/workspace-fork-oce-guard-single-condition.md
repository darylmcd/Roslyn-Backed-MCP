# workspace-fork-oce-guard-single-condition — timeout reclassification guard weaker than the canonical pattern

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs`

## Acceptance

- [ ] The catch guard matches the canonical two-condition form used by `GatedCommandExecutor` / `WorkspaceExecutionGate`: `when (!ct.IsCancellationRequested && <timeoutCts>.IsCancellationRequested)`.
- [ ] A regression asserts a cancellation originating from a token OTHER than the fork timeout is not reported as `TimeoutException`.

## Evidence

Found while independently verifying the `gate-owned-timeout-cts-oce-classification-audit` conclusions. That audit correctly reported "no gap" at all five of its own anchors — this is a different, narrower defect it did not look for.

`WorkspaceForkApplyService.RestoreForkAsync` reclassifies with `catch (OperationCanceledException) when (!ct.IsCancellationRequested) => throw new TimeoutException(...)`. The canonical pattern documented by sibling initiative `gate-timeout-exception-drops-inner-oce` is two-condition. With only the first condition, an OCE from any internal token that is not the caller's ambient `ct` is attributed to the restore timeout.

Low blast radius (one call site, message-only misattribution), but it is exactly the inconsistency the codebase has been converging away from.
