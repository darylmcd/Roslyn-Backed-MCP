# apply-composite-no-undo-capture — Capture undo state in CompositeApplyOrchestrator

**row:** `apply-composite-no-undo-capture` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:117`

## Acceptance

- [ ] revert_last_apply immediately after apply_composite_preview restores the composite's files
- [ ] It does not touch the previous apply
- [ ] workspace_changes records the composite apply

## Evidence

- revert_last_apply after composite seq37 reverted seq36 create_file instead. CompositeApplyOrchestrator has no CaptureBeforeApply call (only EditorConfigService, EditService, ProjectMutationService, RefactoringService do). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
