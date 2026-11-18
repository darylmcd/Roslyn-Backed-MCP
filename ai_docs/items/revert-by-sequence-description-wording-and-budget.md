# revert-by-sequence-description-wording-and-budget — revert_apply_by_sequence description is stale and the per-tool budget blocks the fix

**row:** `revert-by-sequence-description-wording-and-budget` · **pri:** `Low` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs:73`
- `tests/RoslynMcp.Tests/ToolDescriptionBudgetHarness.cs`

## Acceptance

- [ ] Description and McpToolMetadata summary state the actual behaviour (file-based snapshots; created files deleted) within the budget, trimming elsewhere if needed.
- [ ] Surface/catalog tests regenerated and green.

## Evidence

- undo-a report sketches 4-5: PR 2140 fixed revert_last_apply but left revert_apply_by_sequence wording; revert_last_apply has 2 chars of headroom and the aggregate budget 14, so any added caveat must trim elsewhere.
