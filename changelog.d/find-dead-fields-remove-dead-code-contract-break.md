---
category: Fixed
---

- **Fixed:** `find_dead_fields(usageKind=never-read)` no longer flags DI-captured fields (e.g. `_options`, `_factory`) as removable when a chained `remove_dead_code_preview` will refuse with `"still has references"`. Every `DeadFieldDto` hit gains `removalBlockedBy` (nullable list of `Kind@Path:Line:Col` markers — ctor-enclosed writes tagged `ConstructorWrite@...`) and `safelyRemovable` (`false` when any source reference survives). Prefers metadata-surface over cascading remove, keeping `UnusedCodeAnalyzer` focused on classification (`find-dead-fields-remove-dead-code-contract-break`).
