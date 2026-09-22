---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `DocumentSymbolsSymbolHandleTests.cs`, `DotnetCommandRunnerPipeLifetimeTests.cs`, and `ExpandedSurfaceIntegrationTests.CoverageProcess.cs` — removed the opt-outs proven safe by repeated concurrent runs (both classes only read through the already-synchronized `WorkspaceIdCache`, with no other shared mutable state), and documented the retained one with its concrete shared-state dependency (machine-global `dotnet build-server shutdown` and `-nodeReuse:true` worker nodes).
