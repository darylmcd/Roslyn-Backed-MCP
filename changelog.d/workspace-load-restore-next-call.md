---
category: Added
---

- **Added:** `workspace_load` and `workspace_reload` results now carry a structured `nextCall` (`workspace_reload` with `autoRestore:true`) when a restore is required; the lean restore hint now describes missing package assets. Closes `workspace-load-restore-next-call`.
