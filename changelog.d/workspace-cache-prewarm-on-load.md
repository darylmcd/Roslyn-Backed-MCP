---
category: Added
---

- **Added:** `workspace_load(prewarm: true)` now runs `workspace_warm` after a successful load and returns the warm result alongside the load summary; omitted or `false` preserves the cold-load profile. Closes `workspace-cache-prewarm-on-load`.
