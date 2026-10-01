---
category: Fixed
---

- **Fixed:** `get_complexity_metrics`, `get_cohesion_metrics`, `get_coupling_metrics` and `get_namespace_dependencies` now reject an unknown `projectName` with the InvalidArgument envelope instead of returning an empty result. Migration: a mistyped or stale project name now fails; omit `projectName` for the whole solution or use `workspace_status` to list project names.
