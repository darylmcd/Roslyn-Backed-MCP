# composite-failed-apply-displaces-undo-target — Define revert_last_apply after a failed composite apply

**row:** `composite-failed-apply-displaces-undo-target` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:55`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:80`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`

## Acceptance

- [ ] After a composite apply fails (partially or before the first write), `revert_last_apply` either reverts the partial writes or targets the previous committed apply — whichever is decided — and the behavior is documented.
- [ ] A test drives the `ProjectFailure` path after capture and asserts the chosen semantics.

## Evidence

- HEAD `CompositeApplyOrchestrator.cs:55` `await CaptureUndoSnapshotAsync(...)` runs before the writes; the catch path `:65` `return ProjectFailure(ex, appliedFiles, mutations.Count);` never records the change, and `UndoService.cs:80` `_snapshots[workspaceId] = new UndoSnapshot(` overwrites the prior slot on capture. Traced by the cold review of PR #1619; `EditService` shares the capture-before-write pattern.

## Context

Spin-off from `apply-composite-no-undo-capture` (PR #1619), which added the capture.
