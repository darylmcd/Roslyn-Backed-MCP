# tool-consolidation-merge-child-dependency-repair — tool-consolidation-merge-child-dependency-repair

**row:** `tool-consolidation-merge-child-dependency-repair` · **pri:** `Low` · **size:** `S` · **deps:** `tool-merge-rows-dangling-consolidation-dep`

## Anchors

- `ai_docs/backlog.md`
- `docs/decisions/0009-tool-surface-policy.md`
- `docs/decisions/0011-v5-major-release-contract.md:25-38`

## Acceptance

- [ ] If consolidation is approved, repoint every live tool-consolidation merge child from retired `tool-consolidation-adr-and-alias-machinery` to the current prerequisite chain.
- [ ] Align each affected item detail's dependency prose with its slim-row dependency cell.
- [ ] Run backlog lint/audit with no dependency or anchor drift.

## Evidence

- The 21 live merge-child rows still name the retired umbrella as their dependency in both cells and prose. ADR 0011 records removal of the prior alias lifecycle. `tool-merge-rows-dangling-consolidation-dep` tracks the product decision about consolidation.

- The [former composite-alias item](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/67bd28d905d96b79d5a5d484eaacd95da500696e/ai_docs/items/apply-composite-canonical-alias-surface.md) records the historical prerequisite approach.
