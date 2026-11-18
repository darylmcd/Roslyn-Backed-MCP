# undo-uncommitted-sentinel-sequence-zero — UndoSnapshot uses SequenceNumber 0 as an implicit uncommitted marker

**row:** `undo-uncommitted-sentinel-sequence-zero` · **pri:** `Low` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:424`
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] UndoSnapshot exposes an explicit committed/uncommitted state (or a named property) used at UndoService.cs sites :424, :466, :498, :523, :768, :839; behaviour and tests unchanged.

## Evidence

- undo-b report sketch 4 (PR 2141): the sentinel multiplied across the claim/commit/unclaim races.
