---
category: Maintenance
---

- **Maintenance:** Improve C# session coverage: add Roslyn workspace-bookend prelude to routine-flow prompts and refactor skills. The backlog-sweep plan/execute prompts and the `refactor`/`refactor-loop` skills now probe CWD for `*.csproj`/`*.sln`/`*.slnx` on entry; positive detection triggers `workspace_load` and surfaces the top 5 Roslyn semantic primitives as a recommended-tool block. Non-C# repos skip cleanly. `validate_workspace` call added as post-mutation exit gate.
