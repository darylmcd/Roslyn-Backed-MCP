# composite-failed-apply-displaces-undo-target — Define revert_last_apply after a failed composite apply

**row:** `composite-failed-apply-displaces-undo-target` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:165`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:141`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`

## Acceptance

- [ ] After a composite apply fails (partially or before the first write), `revert_last_apply` either reverts the partial writes or targets the previous committed apply — whichever is decided — and the behavior is documented.
- [ ] A test drives the `ProjectFailure` path after capture and asserts the chosen semantics.

## Evidence

- `CompositeApplyOrchestrator.cs:165` captures a pending undo before the composite writes. The `ProjectFailure` branch at line 128 can return without recording that change. `UndoService.cs:141` replaces the pending slot when the capture is published.
- PR #1619 cold review identified the displacement. The edit, project-mutation, and refactoring paths now publish only after their own effect boundaries. This row still owns the composite partial-write behaviour.

## Context

Spin-off from `apply-composite-no-undo-capture` (PR #1619), which added the capture.
