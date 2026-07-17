# undo-revert-preserve-snapshot-on-failure-or-cancellation — Preserve undo snapshot until restore succeeds

**row:** `undo-revert-preserve-snapshot-on-failure-or-cancellation` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:97-134`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:202-233`
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] `RevertAsync` and `RevertBySequenceAsync` do not irreversibly remove history/snapshots before cancellable restore work succeeds.
- [ ] Failure or cancellation leaves the same snapshot retryable and preserves dependency ordering.
- [ ] Successful revert consumes exactly the intended snapshot once.
- [ ] Tests cover cancellation, restore failure, retry success, sequence dependencies, and ordinary success.

## Evidence

- Cold apply/undo review found both methods remove history before `TryApplyChanges`/restore completion, defeating a later compensation retry.
