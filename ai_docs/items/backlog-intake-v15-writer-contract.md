# backlog-intake-v15-writer-contract — Align backlog intake with the v15 writer contract

**row:** `backlog-intake-v15-writer-contract` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.claude/skills/backlog-intake/SKILL.md`
- `eng/verify-ai-docs.ps1`

## Acceptance

- [ ] Replace active P2/P3/P4 assumptions with the v15 `Critical`/`High`/`Medium`/`Low`/`Defer` vocabulary and current slim-index shape.
- [ ] Route every backlog mutation through the global `backlog.mjs` writer while preserving issue intake, archive, and public-issue reconciliation behavior.
- [ ] Add one static contract regression that rejects legacy priority headings, direct table-edit instructions, and stale count output in the active skill.

## Evidence

- `.claude/skills/backlog-intake/SKILL.md:40,179,226,296` still requires and reports the retired P2/P3/P4 schema.
Scope correction (2026-09-10): include .claude/agents/backlog-intake-extractor.md and ai_docs/items/backlog-d-fragment-schema.md alongside the skill and eng/verify-ai-docs.ps1. Define P0→Critical, P1→High, P2→Medium, P3→Low mapping for raw fragments; extractor emits v15 bands; active skill routes all mutations through backlog.mjs.
