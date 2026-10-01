---
category: Fixed
---

- **Fixed:** Explicit `autoRestore:true` failures on `workspace_load`/`workspace_reload` now return a path-free public reason (exit code plus a "run dotnet restore and retry workspace_reload" hint) instead of generic redaction; the detailed path and output tails stay in logs.
