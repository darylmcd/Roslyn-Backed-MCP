---
category: Fixed
---

- **Fixed:** `workspace_load`/`workspace_reload` no longer report `restoreRequired=true` when the unmet dependency is a missing analyzer **build** output (`WORKSPACE_UNRESOLVED_ANALYZER`) rather than a NuGet restore input. A new `buildRequired` field on the workspace status DTO signals "run `dotnet build` on the analyzer project, then `workspace_reload`" instead of sending callers into a no-op `dotnet restore` loop; the summary `restoreHint` and the `workspace_readiness_report` verdict are now consistent.
