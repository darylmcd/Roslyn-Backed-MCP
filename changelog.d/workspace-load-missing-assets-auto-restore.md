---
category: Changed
---
- **Changed:** `workspace_load`/`workspace_reload`: `autoRestore` is now nullable. Omitted restores only never-restored projects (missing `project.assets.json`, located through `UseArtifactsOutput`/custom intermediate-path layouts) and keeps a failed or timed-out restore non-fatal (`restoreRequired: true` plus a path-free `restoreFailureReason`); `true` still restores for drift and fails the call on a failed restore; `false` opts out. See ADR 0013.
