# other-failed-apply-displaces-undo-target — Preserve prior undo across failed mutators

**row:** `other-failed-apply-displaces-undo-target` · **pri:** `Medium` · **size:** `L` · **deps:** `editorconfig-failed-write-displaces-undo-target`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs:103`
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:564`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:349`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:79`

## Acceptance

- [ ] Failed EditService, ProjectMutationService, and RefactoringService mutations do not displace the previous successful revert_last_apply target
- [ ] Each mutation commits its undo capture only at its own successful commit boundary while preserving the exact pre-image
- [ ] Regression tests cover failure followed by revert at each caller
- [ ] Reconcile the separate `composite-failed-apply-displaces-undo-target` row without duplicating its partial-apply semantics

## Evidence

- PR #1678 cold review traced `CaptureBeforeApply` calls before mutation success in these services. `UndoService` immediately replaces `_snapshots[workspaceId]`. The editorconfig caller is fixed first under `editorconfig-failed-write-displaces-undo-target`; these callers have distinct success and partial-apply boundaries and remain a dependency-chained follow-up.

## Notes

5.x compatibility screen (52f2c418): UndoService.cs:89 replaces the current LIFO target on capture. UndoServiceTests.FailedApply_LeavesPendingCaptureUndoable_AndAuditDoesNotPromoteIt explicitly requires stable revert_last_apply to compensate surviving writes, and MultiFileEditTools.cs documents partial recovery. Before selection, demonstrate the complete caller-specific fix preserves retryable recovery of surviving owned effects as well as the prior successful target when there are no effects. A blanket change to target only successful operations would break the stable recovery contract; reconsider only at an operator-approved major with ADR, migration and raw-wire coverage.
