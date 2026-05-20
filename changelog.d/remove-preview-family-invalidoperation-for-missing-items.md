---
category: Fixed
---

- **Fixed:** `remove_package_reference_preview`, `remove_project_reference_preview`, `remove_target_framework_preview`, and `remove_central_package_version_preview` now return an empty preview (`changes: []`) when the specified item is not present, instead of throwing `InvalidOperationException`. Shape-probing callers can detect no-ops without exception-handling. Fixes gh #769 §13.29.
