# addenda-self-edit-caveat-contradicts-runtime — Align the addenda selfEditCaveat with runtime.md

**row:** `addenda-self-edit-caveat-contradicts-runtime` · **pri:** `Low` · **size:** `S`

# addenda-self-edit-caveat-contradicts-runtime — Align selfEditCaveat with runtime.md

## Anchors

- `ai_docs/prompts/backlog-sweep-addenda.md`
- `ai_docs/runtime.md`

## Acceptance

- [ ] The addenda selfEditCaveat block forbids only *_apply in the main checkout, matching runtime.md write-side rules.
- [ ] Dated anecdotes inside the parallel_safety evidence block are removed; parser (backlog.mjs, bsweep-worktree.mjs) still reads both blocks.

## Evidence

- addenda forbids mcp__roslyn__*_preview in main checkout; runtime.md and the primer allow it (doc-audit 2026-09-23).
