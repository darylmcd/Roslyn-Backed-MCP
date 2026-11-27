# composite-failed-apply-displaces-undo-target — Define revert_last_apply after a failed composite apply

**row:** `composite-failed-apply-displaces-undo-target` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:112`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:165`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:141`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`

## Acceptance

- [ ] After a composite apply fails (partially or before the first write), `revert_last_apply` either reverts the partial writes or targets the previous committed apply — whichever is decided — and the behavior is documented.
- [ ] A test drives the `ProjectFailure` path after capture and asserts the chosen semantics.

## Evidence

- `CompositeApplyOrchestrator.cs:112` captures composite undo before the first non-create write, through the helper at line 165. If a later step fails, the `ProjectFailure` branch can return with partial writes and without recording the change. `UndoService.cs:141` publishes the composite pre-image over the previous LIFO target even when the failure left no composite-owned effects.
- PR #1619 cold review identified the displacement. The edit, project-mutation, and refactoring paths now publish after their own effect boundaries. This row still owns the composite partial-write and no-effect cases.

## Context

Spin-off from `apply-composite-no-undo-capture` (PR #1619), which added the capture.
