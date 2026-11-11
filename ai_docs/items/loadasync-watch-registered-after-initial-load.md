# loadasync-watch-registered-after-initial-load — Register the file watcher before the initial load reads disk

**row:** `loadasync-watch-registered-after-initial-load` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:512-537`
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:165`

## Acceptance

- [ ] An edit that lands while the initial load is reading disk leaves the new workspace stale (deterministic test with a gated loader); a failed load unwatches without leaking the watcher.

## Evidence

- Same defect class as the reload clear-stale race fixed by PR 2050, on the initial-load path: LoadAsync registers the watcher only after the load.
