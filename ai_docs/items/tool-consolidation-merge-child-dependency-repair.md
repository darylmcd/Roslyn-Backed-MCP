# tool-consolidation-merge-child-dependency-repair — tool-consolidation-merge-child-dependency-repair

**row:** `tool-consolidation-merge-child-dependency-repair` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/backlog.md`
- `ai_docs/items/apply-composite-canonical-alias-surface.md`

## Acceptance

- [ ] Repoint every live tool-consolidation merge child from retired `tool-consolidation-adr-and-alias-machinery` to the current prerequisite chain.
- [ ] Align each affected item detail's dependency prose with its slim-row dependency cell.
- [ ] Run backlog lint/audit with no dependency or anchor drift.

## Evidence

- The policy foundation and ADR are shipped, but 17 live merge-child rows still name the retired umbrella as their dependency in both cells and prose.
