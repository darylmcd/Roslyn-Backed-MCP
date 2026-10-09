| Field | Content |
|---|---|
| Route | split-child of preview-token-consumed-reason |
| Diagnosis | `RefactoringService.cs:356-368` removes even when persistence returns false; `DocumentSetPersistenceService.cs:61-90` confirms success only after persistence and rolls back failures. |
| Approach | Capture completion identity after refusal/preparation checks, before persistence. Confirm only when PersistAsync returns true, BEFORE incidental tracker/resolver failures; false/exception/cancellation must never record Applied. Use typed reason projection for inside-gate missing-token paths. Completion survives reload pruning through owned metadata; do not mark before rollback can contradict success. Preserve provenance/truncation refusals and intermediate extraction discard. Test apply_with_verify rollback: AlreadyApplied means the token was previously redeemed, not that edits remain. |
| Scope | 1 production: `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`; 3 tests: `tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs`, `tests/RoslynMcp.Tests/RefactoringToolsIntegrationTests.cs`, `tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs`; docs: none.  Additional exact docs: `changelog.d/preview-token-solution-confirmation.md`. |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | Shape C/A production ripple 1. No DocumentSetPersistenceService edit or optional callback needed: confirm after its true return. Tests prove persistence failure/rollback, reload pruning before successful return and ancillary post-commit failure cannot alter confirmed redemption. |
| Validation | Observe new regressions fail on old code; per-edit compile_check/targeted test_run, documented CLI fallback after workspace-load failure; serialized just ci and required hosted Windows/Linux checks. |
| Performance review | Bound payload-free metadata count/TTL; verify atomic transitions and concurrency. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Successful solution token consumption is distinguished from failed persistence. |
| Backlog sync | Close rows: []. |
