# items-gate-forced-companion-prose-dedupe — Drop the duplicated gate-forced-companion paragraph from 34 items files

**row:** `items-gate-forced-companion-prose-dedupe` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/items/promotion-tier-refactoring-batch-1.md` — representative of 34 detail files carrying a hand-written "Gate-forced companions (NOT anchored above, but they WILL be edited)" paragraph.
- `ai_docs/prompts/backlog-sweep-addenda.md` — `## mandatory_companion_files` now expresses the same rule once, machine-readably.

## Acceptance

- The duplicated gate-forced-companion paragraph is removed from every `ai_docs/items/*.md` that carries it, replaced by a one-line pointer to the addenda section.
- The backlog audit still reports the `ReadmeSurfaceCountTests.cs` companion on those rows (expansion comes from the addenda, not the prose).
- The AI-docs verifier passes.

## Evidence

`grep -rl "Gate-forced companion" ai_docs/items/*.md` → 34 files on 2026-09-15, each restating the same README-surface-count gate rule because the addenda's companion table was prose-only and invisible to `mandatoryCompanionSection()`. With the addenda block in place the audit expands the companion mechanically (`promotion-tier-refactoring-batch-1` went 4 prod / 0 test → 4 prod / 1 test), so the per-row prose is now duplicated guidance that will drift.
