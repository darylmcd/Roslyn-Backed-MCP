# file-watcher-error-event-unhandled — Mark a workspace stale when its FileSystemWatcher reports an Error

**row:** `file-watcher-error-event-unhandled` · **pri:** `Medium` · **size:** `S` · **verified:** `3582e389d56c209b67a7af896c2749c2b13b2de3`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs` `WatchCore` tree watcher and `SharedDirectoryWatcher` constructor — no `Error` handler on any watcher.

## Acceptance

- [ ] A forced watcher Error (test seam) marks every workspace served by that watcher stale with reason external-edit.
- [ ] A test covers the tree watcher and a shared ancestor watcher.

## Evidence

- Found by the cold review of PR 2094 (2026-11-13). Pre-existing: the old per-filter watchers had no handler either. Consolidation puts all event types on one 8 KB Windows buffer, so overflow is no less likely.

## Context

- Windows buffer overflow raises Error and silently drops events; Linux inotify queue overflow behaves the same.
