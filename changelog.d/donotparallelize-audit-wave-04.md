---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `CodeActionServiceTests.cs`, `CompileCheckZeroProjectsTests.cs`, and `ConsumerAnalysisTests.cs`. All three classes only perform read-side operations (`GetCodeActionsAsync`, `CompileCheckService.CheckAsync`, `ConsumerAnalysisService.FindConsumersAsync`) through the already-synchronized `WorkspaceIdCache`, with no workspace reload, `*_apply` call, or other shared mutable state touched. Removed the opt-outs after 3 consecutive green concurrent runs (13/13 passing each time) alongside their wave siblings under the assembly's class-level parallelism.
