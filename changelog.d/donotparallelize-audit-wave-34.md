---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `ValidateWorkspaceSummaryTests`, `ValidationIntegrationTests`, and `ValidationToolsIntegrationTests`. Removed the opt-out from `ValidateWorkspaceSummaryTests` (read-only validation through the synchronized `WorkspaceIdCache`, proven safe by repeated concurrent runs); retained it on the other two with source-adjacent comments naming the child `dotnet build`/`dotnet test` runs against in-repo fixtures, the path-deduplicated `BuildFailureSolution` session, and the exclusive analyzer-DLL open they depend on.
