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
