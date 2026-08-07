---
category: Maintenance
---

- **Maintenance:** Extended the DI-resolution leak-guard test (`ToolDiResolutionTests`) to derive its guarded tool list by reflection instead of a hardcoded array, closing a gap where `compile_check` and `test_run` — which gained the same `ILoggerFactory` DI parameter in a prior PR — had no schema-leak regression coverage.
