# loadasync-watch-registered-after-initial-load — Register the file watcher before the initial load reads disk

**row:** `loadasync-watch-registered-after-initial-load` · **pri:** `Medium` · **size:** `S` · **verified:** `a2a51ec4de033bfd3aa36ba9d238b73e849b0f9a`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`

## Acceptance

- [ ] An edit that lands while the initial load is reading disk leaves the new workspace stale (deterministic test with a gated loader); a failed load unwatches without leaking the watcher.

## Evidence

- Same defect class as the reload clear-stale race fixed by PR 2050, on the initial-load path: LoadAsync registers the watcher only after the load.
