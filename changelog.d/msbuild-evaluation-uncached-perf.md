---
category: Maintenance
---

- **Maintenance:** Cache MSBuild `ProjectCollection` evaluation in `MsBuildEvaluationService`, keyed by workspace version + project path, so `evaluate_msbuild_properties`/`evaluate_msbuild_items` (and their `get_nuget_dependencies`/`get_security_status` consumers) stop re-parsing unchanged project XML on every call; invalidated on workspace reload/close.
