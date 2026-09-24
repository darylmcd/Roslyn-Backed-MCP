---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `PreviewRouteBindingEditingTests.cs`, `ProgressEmissionTests.cs`, and `ProjectFilterTests.cs` — removed the opt-outs on `ProjectFilterTests` and `PreviewRouteBindingEditingProducerTests` (read-only through the synchronized `WorkspaceIdCache`, proven safe by repeated concurrent runs) and documented the retained `ProgressEmissionTests` opt-out (child `dotnet build` / `dotnet test` against the shared SampleSolution fixture).
