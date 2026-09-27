---
category: Fixed
---
- **Fixed:** `project_diagnostics`, `security_diagnostics`, and `list_analyzers` now reject an unknown `projectName` with the same InvalidArgument error as `compile_check`, instead of returning an empty or all-zero result. (`unknown-projectname-silently-empty`)
