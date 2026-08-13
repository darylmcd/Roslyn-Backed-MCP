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
## Amendment — 2026-08-13 (cold self-review; corrects a claim in merged history)

**The plan stanza and reconcile PR #1244's body both understated this site, and that is now uncorrectable in git history — so the correction lives here.**

- `plan.md` § 9 described `WorkspaceForkApplyService.cs:531-535` as "already carries the exact fix pattern: ... **mirroring** `GatedCommandExecutor.ExecuteAsync`/`WorkspaceValidationService`/`WorkspaceExecutionGate`". It does not mirror them: those use the two-condition guard `when (!ct.IsCancellationRequested && <timeoutCts>.IsCancellationRequested)`; this site uses only the first condition.
- PR #1244's body repeated the audit's "no gap at all five anchors" framing and listed "WorkspaceForkApplyService's existing reclassification" among the re-verified conclusions, in the **same commit** that filed this row contradicting it.

The audit's narrow question ("can an unclassified OCE escape?") is answered correctly at this site — it does reclassify. The false part is the equivalence claim. Treat this row, not the plan or the commit message, as the accurate record.
