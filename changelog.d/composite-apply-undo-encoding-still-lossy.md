---
category: Fixed
---

- **Fixed:** `apply_composite_preview` and `revert_last_apply`'s solution-snapshot restore path now preserve a file's original BOM/encoding instead of silently re-encoding as UTF-8-no-BOM, closing the two write paths PR #1157 (`mutation-write-paths-drop-original-encoding`) deferred.
