# filewatcher-watcherentry-watchers-unguarded-mutation — WatcherEntry._watchers mutated without synchronization

**row:** `filewatcher-watcherentry-watchers-unguarded-mutation` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:181` (`private readonly List<FileSystemWatcher> _watchers = [];`)
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:208` (`public void AddWatcher(FileSystemWatcher watcher) => _watchers.Add(watcher);`)

## Acceptance

- [ ] `WatcherEntry._watchers` mutation is either documented as single-threaded-by-construction (a load-bearing comment on `AddWatcher` stating it is only called from `Watch()` before `EnableRaisingEvents`), OR guarded consistently with the rest of the type (`_reasonLock` or a dedicated lock) if any concurrent-mutation path is introduced.
- [ ] If guarding: a regression test exercises concurrent `AddWatcher`/`Dispose` and asserts no `InvalidOperationException`/torn state.

## Evidence

- Row-1 implementer finding (2026-06-20 top-n-remediation, `filewatcher-waitforstale-clearstale-stranded-awaiter`): `AddWatcher` does `_watchers.Add(...)` on a plain `List<FileSystemWatcher>` with no synchronization, while `Dispose` and the `FileSystemWatcher` event callbacks touch entry state from dispatch threads. The rest of `WatcherEntry` is explicitly `_reasonLock`-protected — this is the lone unguarded mutable collection on the type.

## Context

Currently benign: `AddWatcher` is only ever called single-threaded from `Watch()` before `EnableRaisingEvents` matters, so no concurrent mutation occurs today. The risk is an inconsistency a future caller could trip. Smallest fix is likely a load-bearing comment documenting the invariant; full guarding only if a concurrent path is added.
