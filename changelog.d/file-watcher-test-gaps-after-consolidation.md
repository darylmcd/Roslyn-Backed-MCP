---
category: Maintenance
---

- **Maintenance:** `FileWatcherService` watchers are now created through an internal factory, and tests cover a failed `Watch` releasing every registration, nested workspaces retiring independently, `Dispose` and concurrent `Watch`/`Unwatch` leaving no shared watcher behind, and `obj/`, `bin/` and `.git/` events not marking a workspace stale; two negative-signal tests that slept after the event now fence on the watcher's own dispatch order, and the service's XML doc now sits on the class and describes the shared-ancestor topology. No runtime behavior changes. Closes `file-watcher-test-gaps-after-consolidation`, `watcher-overwrite-negative-signal-fence`, `file-watcher-service-doc-misplaced`.
