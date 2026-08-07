---
category: Fixed
---

- **Fixed:** `ProjectFilterHelper.FilterProjects` now treats a whitespace-only `projectFilter` as "no filter" (matching the semantics `compile_check` already enforced), so `list_analyzers`, `format_verify_solution`, and the other 15 tools sharing the helper no longer silently return zero-project results on a blank filter string. Closes `project-filter-helper-whitespace-normalize`.
