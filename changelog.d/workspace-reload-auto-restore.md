---
category: Added
---

- **Added:** `workspace_load` and `workspace_reload` now accept `autoRestore=true`, and workspace status now reports `restoreRequired` when package inputs drift from the loaded assets snapshot so callers can recover stale metadata references with one restore-plus-reload flow. Closes `workspace-reload-auto-restore`. (PR #362)
