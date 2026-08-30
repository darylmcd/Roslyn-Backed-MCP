---
category: Fixed
---

- **Fixed:** `compile_check` and `project_diagnostics` now execute source generators against an idempotent compilation snapshot, retain genuine generator and compiler diagnostics, centralize correlation-id suffix formatting, and isolate supported-fix enumeration with partial-result and cancellation guarantees. Windows analyzer shadow isolation now targets only workspace-owned binaries, preventing collectible SDK/NuGet analyzer contexts from crashing later completion-provider discovery, and its lifecycle events retain actionable failure types without raw exceptions or filesystem paths. Closes `project-diagnostics-generated-regex-false-errors`, `diagnostic-supported-fix-enumeration-collaborator-extraction`, `correlation-id-suffix-single-source`, `analyzer-shadow-loader-external-scope-crash`, `analyzer-shadow-sweep-failure-observability`, and `analyzer-shadow-log-redaction`.
