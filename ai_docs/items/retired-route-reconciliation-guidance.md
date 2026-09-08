# retired-route-reconciliation-guidance — Refresh reconciliation remediation routes

**row:** `retired-route-reconciliation-guidance` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.claude/skills/reconcile-backlog-sweep-plan/SKILL.md`
- `.claude/skills/reconcile-backlog-vs-issues/SKILL.md`

## Acceptance

- [ ] Both reconciliation skills route active work through `/backlog-remediate` without changing their durable reconciliation semantics or compatibility names.

## Regression shape

Run the retired-name inventory over both anchors and require every remaining match to be explicitly historical or compatibility-only.

## Evidence

- PR #1480 contains the prepared changes for both anchors.

## Context

Split from `retired-route-recovery-guidance`, sourced from global parent `bl-0318`; closing this row also deletes this detail file.
