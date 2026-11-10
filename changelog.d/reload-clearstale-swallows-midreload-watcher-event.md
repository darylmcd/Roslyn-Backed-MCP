---
category: Fixed
---

- **Fixed:** `workspace_reload` (and auto-reload) no longer wipes an external edit the file watcher observed while the reload was reading disk: the post-load clear now keeps `isStale: true` only when a change was made at or after the instant the reload began loading, so edits that finished earlier (including duplicate or delayed watcher events for them and the server's own apply echoes) are still cleared. Closes `reload-clearstale-swallows-midreload-watcher-event`.
