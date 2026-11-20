# undo-service-revert-unconditional-restore — Decide and enforce external-edit ownership for undo reverts

**row:** `undo-service-revert-unconditional-restore` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:573` (RevertFromFileSnapshotsAsync; prior anchors :368-415 and :605-624 were stale after PRs 2140/2141)
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:666` (RevertFromSolutionSnapshotAsync)
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Product decision recorded: undo either refuses or reports a conflict when a file no longer matches the applied post-state, or documents last-writer-wins as intended for explicit user reverts.
- [ ] If ownership is enforced, reuse the persistence ownership ledger semantics from physical-compensation-external-edit-ownership; no second mechanism.
- [ ] Regression for an externally edited file between apply and revert.

## Evidence

- Failed-persistence compensation now preserves externally changed bytes (PR 1963). Explicit user undo was left unchanged because its policy differs; it needs its own decision.

## Notes

Remediation run 2026-11-18 (PR 2140 attempt): not implemented. Needs operator decision: refuse/report conflict when a file no longer matches the applied post-state, or document last-writer-wins. Recommended: enforce, reusing the persistence ownership ledger semantics. A contingent plan was drafted in the undo-a session (D:/Roslyn-Backed-MCP/.worktrees/undo-a.report.md only records that the row was left untouched pending this decision). Note PR 2140 now scopes solution-snapshot revert to the apply own documents, which narrows but does not remove the clobber of same-file external edits.

Operator decision 2026-11-20: refuse on drift everywhere. Undo refuses or reports a conflict when a file no longer matches the applied post-state, reusing the persistence ownership ledger. One ADR shared with apply-project-mutation-last-apply-wins-decision.
