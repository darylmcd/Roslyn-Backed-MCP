---
category: Fixed
---

- **Fixed:** `workspace_reload` (and auto-reload) no longer wipes an external edit the file watcher observed while the reload was reading disk: the post-load clear is now conditional on no `external-edit` mark having landed since the reload started, so `workspace_status` keeps reporting `isStale: true` until the next reload; server-attributed (`apply`/`restore`) marks are still cleared. Closes `reload-clearstale-swallows-midreload-watcher-event`.
