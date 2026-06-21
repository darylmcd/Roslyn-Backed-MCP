---
category: Maintenance
---

- **Maintenance:** Documented the single-threaded-by-construction invariant on `WatcherEntry._watchers` in `FileWatcherService` with a load-bearing comment. The `List<FileSystemWatcher>` is mutated only via `AddWatcher`, which is called single-threaded from `Watch()` during entry construction; the `FileSystemWatcher` event callbacks touch only the `_reasonLock`-guarded stale-reason state, never the list — so it needs no synchronization today even though watchers may fire mid-construction. The comment records the invariant (the list was the lone unguarded mutable member in an otherwise `_reasonLock`-guarded type) and the guard-if-a-concurrent-path-is-added escape hatch. Closes `filewatcher-watcherentry-watchers-unguarded-mutation`.
