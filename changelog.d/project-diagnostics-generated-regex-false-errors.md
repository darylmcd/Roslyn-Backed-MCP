---
category: Fixed
---

- **Fixed:** `compile_check` and `project_diagnostics` now execute source generators against an idempotent compilation snapshot, retain genuine generator and compiler diagnostics, centralize correlation-id suffix formatting, and isolate supported-fix enumeration with partial-result and cancellation guarantees. Closes `project-diagnostics-generated-regex-false-errors`, `diagnostic-supported-fix-enumeration-collaborator-extraction`, and `correlation-id-suffix-single-source`.
