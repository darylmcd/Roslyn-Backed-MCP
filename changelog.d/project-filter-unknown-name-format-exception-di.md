---
category: Fixed
---
- **Fixed:** `format_check`, `trace_exception_flow` and `get_di_registrations` now reject an unknown `projectName` / project filter with the loaded-project list instead of reporting zero findings (a false clean for `format_check`). Migration: callers passing a mistyped or stale project name now get an error; omit the filter for a whole-solution scan.
