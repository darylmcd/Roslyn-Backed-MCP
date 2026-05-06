---
category: Changed
---

- **Changed:** `workspace_load` now auto-runs `workspace_warm` for omitted `prewarm` on solutions with more than 50 projects, while explicit `prewarm: false` keeps the cold-load profile. Closes `workspace-warm-default-above-50-projects`.
