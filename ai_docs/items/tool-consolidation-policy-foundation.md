# tool-consolidation-policy-foundation — Record tool-surface consolidation and alias policy

**row:** `tool-consolidation-policy-foundation` · **pri:** `Low` · **size:** `M`

## Anchors

- `docs/decisions/0009-tool-surface-policy.md`
- `docs/release-policy.md`

## Acceptance

- [ ] Record formatting, text-edit, code-transform, file-lifecycle, and project-file risk buckets; prohibit cross-bucket apply merges.
- [ ] Require deprecated aliases to remain callable for at least one minor release and permit removal only at the next major.
- [ ] Link ADR 0009 from the release policy and add the published-surface migration-policy changelog fragment.
- [ ] Extend AI-doc validation to prove the decision and cross-reference remain valid.

## Evidence

The live repository has no recorded risk-bucket consolidation policy. The current alias helper only formats a response notice and does not define a surface lifecycle.

## Context

Unblocking child split from `tool-consolidation-adr-and-alias-machinery` on 2026-09-04.
