---
category: Fixed
---

- **Fixed:** `WorkspaceExecutionGate` no longer depends on ambient telemetry to reset post-reload timeout budgets or retry transient stale-snapshot errors after a successful auto-reload; also closed the remaining CA2016 cancellation-token propagation diagnostic in `ParameterObjectService`.
