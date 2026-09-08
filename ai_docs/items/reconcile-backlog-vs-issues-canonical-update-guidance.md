# reconcile-backlog-vs-issues-canonical-update-guidance — Use canonical backlog updates during issue reconciliation

**row:** `reconcile-backlog-vs-issues-canonical-update-guidance` · **pri:** `Low` · **size:** `M`

## Anchors

- `.claude/skills/reconcile-backlog-vs-issues/SKILL.md`
- `eng/verify-ai-docs.ps1`

## Acceptance

- [ ] Route reserved-state, stale-metadata, and label-driven row repairs through `backlog.mjs update` or `note`; do not instruct direct index/detail edits.
- [ ] Preserve read-only classification, explicit operator choice, and the existing no-automatic-close boundary.
- [ ] Add one static guidance-matrix regression covering classify-only, canonical update, note, and explicit close routing.

## Evidence

- The active issue-reconciliation skill predates the v15 writer-only mutation contract and can direct metadata repair without naming the canonical transactional writer.
