---
category: Fixed
---

- **Fixed:** `find_reflection_usages`, `semantic_search`, `find_duplicated_methods`, `find_unused_symbols`, `find_duplicate_helpers`, `find_dead_fields` and `find_dead_locals` now reject an unknown `projectName` with an InvalidArgument error listing the loaded projects instead of reporting a clean empty result. Migration: omit `projectName` or pass a name from `workspace_status`.
