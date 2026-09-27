---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `WorkspaceReloadedEventTests.cs`, `WorkspaceResourceTests.cs` and `WorkspaceSessionLoaderFailureTests.cs`. Removed the `WorkspaceResourceTests` (read-only through the shared `WorkspaceIdCache`) and `WorkspaceSessionLoaderFailureTests` (private `WorkspaceManager` and loader double) opt-outs after repeated concurrent runs. Retained `WorkspaceReloadedEventTests` with a source-adjacent comment naming its dependency: it reloads and closes the SampleSolution session on the assembly-shared `WorkspaceManager` that parallel readers hold by id. Closes `donotparallelize-audit-wave-39`.
