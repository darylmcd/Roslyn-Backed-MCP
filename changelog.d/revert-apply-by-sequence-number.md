---
category: Added
---

- **Added:** New MCP tool `revert_apply_by_sequence(workspaceId, sequenceNumber)` for non-LIFO session revert — `revert_last_apply` was LIFO-only, so an earlier apply buried by later applies or cleared by auto-reload required hand edits. The new tool targets any apply still in session history (sequence numbers come from `workspace_changes`); `UndoService` retains a per-workspace committed-snapshot list and `IChangeTracker.ChangeRecorded` promotes pending snapshots in order so the LIFO and by-sequence pathways stay consistent. Conservative file-overlap check blocks the revert and returns `blockingSequences` when later applies touch the target's files (no silent partial rollback). Surface counts bumped to 162 tools / 108 stable (`revert-apply-by-sequence-number`).
