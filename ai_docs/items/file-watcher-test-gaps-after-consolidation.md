# file-watcher-test-gaps-after-consolidation — Cover the watcher failure, nesting and disposal paths added by the consolidation

**row:** `file-watcher-test-gaps-after-consolidation` · **pri:** `Medium` · **size:** `S` · **verified:** `3582e389d56c209b67a7af896c2749c2b13b2de3`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs` `WatchCore` catch block, `Subscribe`/`Unsubscribe`, `Dispose`.
- `tests/RoslynMcp.Tests/FileWatcherSharedWatcherTests.cs` — covers footprint, shared .editorconfig, per-root retirement, rename-over only.

## Acceptance

- [ ] A faulting watcher factory (injected seam) proves a failed Watch leaves no entry and no shared subscription, and that the Linux error names fs.inotify.max_user_instances.
- [ ] A nested workspace (root that is another workspace ancestor) is retired independently of its parent.
- [ ] Dispose() leaves no shared watcher registered; concurrent Subscribe/Unsubscribe on one directory never leaks or double-disposes.
- [ ] Events under obj/, bin/, .git/ on the tree watcher do not mark stale.

## Evidence

- Cold review of PR 2094 (2026-11-13) item 8. None of these paths is exercised; the failure path was verified only by reading.

## Context

- Needs a small watcher-construction seam; keep it internal.
