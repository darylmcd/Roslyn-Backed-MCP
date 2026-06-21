---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Renamed 5 non-canonical parameters on **experimental** MCP tools to the server's `projectName` convention: `trace_exception_flow` (`scopeProjectFilter` → `projectName`); `find_dead_locals`, `find_dead_fields`, `find_duplicate_helpers`, `semantic_grep` (`projectFilter` → `projectName`). Callers passing any of these keys by name must update to `projectName`. No deprecation aliases (experimental tier). Stable-tier tools (`find_duplicated_methods`, `find_duplicated_code`, `find_type_consumers`) are unaffected and retain `projectFilter`.
