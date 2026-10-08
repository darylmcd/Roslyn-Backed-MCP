| Field | Content |
|---|---|
| Route | split-child of preview-token-consumed-reason |
| Diagnosis | `CompositePreviewStore.cs:35-43` claims before mutation; `PersistentCompositeStorage.cs:163-195` deletes claim artifacts before return; `CompositeApplyOrchestrator.cs:176-200` promises retry even for disk-consumed tokens. |
| Approach | Retain bounded payload-free persistent Claimed evidence separately from atomically published Applied confirmation. Publish claim authority before returning ownership, preserve one-time cross-process semantics and never fall back to creator memory after a lost claim. Override CompositePreviewStore lifecycle query/completion for disk authority. Confirm captured owner identity only after every mutation AND required reload succeeds, before ancillary tracking. Failed/partial/cancelled/lost owners remain Claimed/indeterminate, never replay or AlreadyApplied. Fix false retry-same-token advice/comments; restart and retention loss yield neutral Unknown. Generic Invalidate remains discard. |
| Scope | 3 production: `src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs`, `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs`, `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`; 3 tests: `tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs`, `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`, `tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs`; docs: none.  Additional exact docs: `changelog.d/preview-token-composite-confirmation.md`. |
| Tool policy | edit-only |
| Estimated context cost | 60000 |
| Risks | Shape C/A production ripple 3. defect-forced-companion: claim PersistentCompositeStorage.cs:163 -> Retrieve CompositePreviewStore.cs:35 -> writes CompositeApplyOrchestrator.cs:65 -> completion :159. Exercise two hosts/concurrent claims, metadata I/O failure, owner crash/restart, expiry, partial writes, reload failure and cross-process success. Fail closed when authority is uncertain. |
| Validation | Observe new regressions fail on old code; per-edit compile_check/targeted test_run, documented CLI fallback after workspace-load failure; serialized just ci and required hosted Windows/Linux checks. |
| Performance review | Bound payload-free metadata count/TTL; verify atomic transitions and concurrency. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Persistent composite tokens distinguish claim ownership from confirmed application. |
| Backlog sync | Close rows: []. |
