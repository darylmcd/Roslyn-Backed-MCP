---
category: Fixed
---

- **Fixed:** `build_workspace`, `build_project`, `test_discover`, `test_related`, and `test_related_files` now attach the same schemaHint-on-failure recovery guidance as `test_run` on any error category, instead of only ever hinting on `InvalidArgument` via the global filter default — consistent recovery guidance across the validation tool family. Added direct Tools-layer test coverage for `ScaffoldingTools`, `SecurityTools`, `SuppressionTools`, `FixAllTools`, `EditorConfigTools`, `MSBuildTools`, `ScriptingTools`, and `TestReferenceMapTools`, previously verified only at the Core-service layer.
