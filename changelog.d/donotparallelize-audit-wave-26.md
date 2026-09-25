---
category: Maintenance
---
- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs (wave 26): removed it from `NavigationToolsNotFoundMessageTests` (read-gate-only tool calls, green across repeated concurrent runs) and documented the retained opt-outs on `ServerInfoPathBoundaryTests` (writes the process-global `SecurityOptionsSnapshot`) and `ServiceCoverageTests` (spawns a real `dotnet test` that rewrites the shared sample's `obj/` tree while parallel classes copy it). (`donotparallelize-audit-wave-26`)
