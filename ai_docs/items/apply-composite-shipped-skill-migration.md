# apply-composite-shipped-skill-migration — Migrate shipped skills to the canonical composite-apply name

**row:** `apply-composite-shipped-skill-migration` · **pri:** `Low` · **size:** `M` · **deps:** `apply-composite-canonical-alias-surface`

## Anchors

- `skills/migrate-package/SKILL.md`
- `skills/modernize/SKILL.md`
- `skills/refactor-loop/SKILL.md`
- `skills/refactor/SKILL.md`

## Acceptance

- [ ] Instruct new calls in all four shipped skills to use `apply_composite`.
- [ ] Permit `apply_composite_preview` only in explicit deprecation compatibility prose.
- [ ] Run shipped-skill genericity validation and require scoped stale-name search to find no active old-name instructions.

## Evidence

Four shipped skills currently teach new clients to invoke the deprecated name.

## Context

Depends on `apply-composite-canonical-alias-surface`. Migration child split from `tool-consolidation-adr-and-alias-machinery`.
