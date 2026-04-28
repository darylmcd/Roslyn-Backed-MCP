---
category: Maintenance
---

- **Maintenance:** consolidated host-process composition root via a new shared `AddRoslynMcpHostServices` extension consumed by `Program.cs`, `StartupDiagnosticsTests`, and `ToolDiResolutionTests`. The `9 service types × 3 registrations` pattern lived 1× in `Program.cs` + 2× in test-fixture DI builders (not in 3 production composition fragments as originally suspected); collapsing the duplicates into one extension eliminates the documented drift hazard rather than just deleting `AddSingleton` lines. 18 duplicate registration lines reduced to 9; 3 regression tests added. Closes `di-graph-triple-registration-cleanup` (PR #477).
