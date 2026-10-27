# watcher-delivery-loss-observation — Treat lost watcher delivery as unknown freshness

**row:** `watcher-delivery-loss-observation` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:307`

## Acceptance

- [ ] Install change and Error handlers before enabling delivery; apply the same mechanism to all watcher instances.
- [ ] On overflow/delivery failure conservatively mark affected workspaces stale or authoritatively rescan; preserve owned-write suppression and root retirement.
- [ ] Use deterministic error-delivery and initialization-order controls plus actual watcher positives; no deadline relaxation.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. CreateWatcher enables events in its initializer before subscribing change handlers and never subscribes Error. Other identity watchers must be swept too.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
