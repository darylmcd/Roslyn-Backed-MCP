---
category: Fixed
---

- **Fixed:** `build_workspace`, `build_project`, `test_discover`, `test_run`, `test_related`, and `test_related_files` now return failures with `isError: true` through the shared error pipeline, keeping `category` and `schemaHint` and gaining `_meta`, instead of reporting them as successful calls. Unexpected (`InternalError`) failures in these tools are now correlated and reported like every other tool's.
