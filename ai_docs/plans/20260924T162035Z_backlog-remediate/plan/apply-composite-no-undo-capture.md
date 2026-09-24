| Field | Content |
|---|---|
| Route | `direct` |
| Diagnosis | `CompositeApplyOrchestrator` (`src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:117`) writes mutations and records a change via `_changeTracker` but never calls `IUndoService.CaptureBeforeApply` (only EditorConfigService, EditService, ProjectMutationService, RefactoringService do), so `revert_last_apply` reverts the previous apply instead. Confirms the row. |
| Approach | Row Acceptance verbatim: (1) revert_last_apply immediately after apply_composite_preview restores the composite's files; (2) It does not touch the previous apply; (3) workspace_changes records the composite apply. Regression test first in `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`. |
| Scope | Production files (1): `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`. Test files (1): `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`. No deletions. |
| Tool policy | `edit-only` |
| Estimated context cost | 35000 |
| Risks | Capture pre-apply snapshots (including files the composite creates or deletes) before the first write, mirroring `CaptureBeforeApply` + explicit-snapshot usage in `ProjectMutationService`/`EditService`. `IUndoService` is not injected today (ctor fields: workspace, store, changeTracker?, logger?, exceptionReporter?); add it as an optional ctor parameter consistent with the existing optional dependencies. Fanout probe (grep `new CompositeApplyOrchestrator(`): 8 direct constructions, all in tests (`CompositeApplyOrchestratorTests.cs` x5, `SymbolRefactorPreviewTests.cs` x3) — positional callers compile unchanged with a trailing optional parameter; production resolves via DI. Acceptance bullet 3 already holds via `_changeTracker.RecordChange` — assert it in the test. |
| Validation | Targeted `test_run --filter "FullyQualifiedName~<TestClass>"` per edit; full addenda `ci_equivalent` (`just ci`) before PR. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | `revert_last_apply` immediately after `apply_composite_preview` now restores the composite's files instead of reverting the previous apply. |
| Backlog sync | Close rows: [apply-composite-no-undo-capture]. Mark obsolete: []. |
