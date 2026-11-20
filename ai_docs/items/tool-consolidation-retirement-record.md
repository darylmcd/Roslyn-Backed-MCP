# tool-consolidation-retirement-record — Record the retirement of tool-surface consolidation

**row:** `tool-consolidation-retirement-record` · **pri:** `Low` · **size:** `S` · **verified:** `9feb0f3a356b435b925407c13240e1a730cfa872`

## Anchors

- `docs/decisions/0009-tool-surface-policy.md`
- `docs/decisions/0011-v5-major-release-contract.md:95`
- `docs/release-policy.md:40-46` (Tool-Surface Consolidation)

## Acceptance

- [ ] A decision record states that the planned preview/apply tool merges are retired and why; ADR 0009 risk buckets stay in force as the rule for any future proposal.
- [ ] ADR 0011 line 95 and the release-policy section no longer imply pending `tool-merge-*` work.

## Evidence

- Operator decision 2026-11-20: retire consolidation. The 21 `tool-merge-*` rows, `tool-merge-rows-dangling-consolidation-dep` and `tool-consolidation-merge-child-dependency-repair` were closed in the same change.

## Context

- Recorded reasoning: clients defer tool schemas, so catalog size costs little; merged tools need discriminated-union parameters, which weakens per-tool schema validation and name-based discovery; aliases double the surface for at least one minor.

## Notes

Also sweep prose references to the closed tool-merge rows: ai_docs/items/apply-composite-shipped-skill-migration.md, apply-composite-operator-guidance-and-hook.md, apply-composite-canonical-owner-routing.md, apply-composite-guidance-wave-1.md, apply-composite-guidance-wave-2.md.
