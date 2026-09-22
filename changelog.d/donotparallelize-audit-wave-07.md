---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `DocumentSymbolsSymbolHandleTests.cs`, `DotnetCommandRunnerPipeLifetimeTests.cs`, and `ExpandedSurfaceIntegrationTests.CoverageProcess.cs` — removed the opt-out on `DocumentSymbolsSymbolHandleTests.cs` (proven safe by repeated concurrent runs: reads only through the already-synchronized `WorkspaceIdCache`, no other shared mutable state), and documented the two retained opt-outs with their concrete shared-state dependency: `DotnetCommandRunnerPipeLifetimeTests.cs` (machine-global `dotnet build-server shutdown` and `-nodeReuse:true` worker nodes) and `ExpandedSurfaceIntegrationTests.CoverageProcess.cs` (a real out-of-process `dotnet test` build against the shared, non-isolated `SampleLib.Tests` project).
