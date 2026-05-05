---
category: Added
---

- **Added:** `workspace_drift_check` tool — fast comparison of in-memory MSBuildWorkspace snapshot against filesystem mtimes. Returns `{ stale, files_drifted[], recommended }` so agents can conditionally `workspace_reload` before reads instead of always-reloading or never-reloading. Eliminates silent stale-snapshot reads after out-of-band `Edit`/`Write` mutations. Closes `workspace-drift-check-tool`.
