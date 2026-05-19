---
category: Fixed
---

- **Fixed:** `find_consumers` and `symbol_impact_sweep` returning empty results for static extension-host classes whose members are consumed exclusively via extension-method syntax (e.g. `app.MapImportEndpoints()`). `find_consumers` now aggregates member-level consumers as a fallback; `symbol_impact_sweep` emits a `suggestedTasks` hint pointing to `callers_callees`. Fixes gh #768 §13.3.
