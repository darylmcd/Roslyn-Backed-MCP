# watcher-delivery-loss-observation — Treat lost watcher delivery as unknown freshness

**row:** `watcher-delivery-loss-observation` · **pri:** `Medium` · **size:** `S` · **deps:** `file-watcher-error-event-unhandled`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:207`

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:342` — shared ancestor watcher.

## Acceptance

- [ ] Once the coverage-recovery predecessor lands, verify change events delivered immediately after initial activation and after rearm for both watcher kinds.
- [ ] Correct any remaining event-delivery startup window while keeping owned-write suppression and root retirement. Use deterministic event-order controls and actual watcher positives.
## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. CreateWatcher enables events in its initializer before subscribing change handlers and never subscribes Error. Other identity watchers must be swept too.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.

## Notes

Rechecked 9e7a9439. FileWatcherService.cs:207–231 and :342–362 attach ordinary handlers before enabling the tree and shared watchers. Both still lack Error handlers. The remaining fix depends on file-watcher-error-event-unhandled because reload can clear stale state without restoring stopped watcher coverage. Qualify stable workspace_reload/workspace_status recovery first. If recovery requires a breaking response change, the trigger is the next operator-approved major with ADR, migration note and both raw protocol eras.
