---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `GetSyntaxTreeRangeOverlapTests.cs`, `GoToTypeDefinitionTests.cs`, and `HardeningBehaviorTests.cs`. Repeated concurrent runs proved `GetSyntaxTreeRangeOverlapTests` and `GoToTypeDefinitionTests` safe, so their opt-outs are removed. The opt-out on `HardeningBehaviorTests` stays, with a source comment naming its dependency: a before/after count of workspaces on the assembly-shared `WorkspaceManager`. Closes `donotparallelize-audit-wave-13`.
