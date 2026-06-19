# filewatcher-waitforstale-clearstale-stranded-awaiter — WaitForStaleAsync awaiter stranded by concurrent ClearStale

**row:** `filewatcher-waitforstale-clearstale-stranded-awaiter` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:247` (`ClearStale` swaps `_staleSignal`)
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:214` (`WaitForStaleAsync` captures the current `_staleSignal.Task`)

## Acceptance

- [ ] `ClearStale` completes (or cancels) the outgoing `TaskCompletionSource` before replacing it, so any awaiter parked on the prior signal unblocks deterministically instead of waiting out its `CancellationToken` deadline.
- [ ] A regression test covers: subscribe via `WaitForStaleAsync` on a non-stale entry, call `ClearStale`, assert the awaiter completes/cancels promptly rather than hanging to the token deadline.

## Evidence

- Code-quality review of `ci-flaky-fswatcher-staleness-test` (2026-06-19 top-n-remediation): `ClearStale` (line 247) replaces `_staleSignal` with a fresh TCS; an awaiter that captured the old TCS task at line 224/214 parks on a task that is never completed. Benign for the current sole caller (the test, bounded by a 5s CTS) but a latent trap for the next production caller.

## Context

The `WaitForStaleAsync` seam was added by the flaky-test fix to give the test a deterministic event-driven wait. The only production reference today is the interface declaration + its single implementation; no production code path awaits it yet. The fix is small and self-contained — complete the outgoing TCS on re-arm — but should land before any production caller starts depending on `WaitForStaleAsync` across a reload boundary.
