# undo-service-revert-unconditional-restore — Decide and enforce external-edit ownership for undo reverts

**row:** `undo-service-revert-unconditional-restore` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:368-415 (RevertFromFileSnapshotsAsync)`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:605-624`
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Product decision recorded: undo either refuses or reports a conflict when a file no longer matches the applied post-state, or documents last-writer-wins as intended for explicit user reverts.
- [ ] If ownership is enforced, reuse the persistence ownership ledger semantics from physical-compensation-external-edit-ownership; no second mechanism.
- [ ] Regression for an externally edited file between apply and revert.

## Evidence

- Failed-persistence compensation now preserves externally changed bytes (PR 1963). Explicit user undo was left unchanged because its policy differs; it needs its own decision.
