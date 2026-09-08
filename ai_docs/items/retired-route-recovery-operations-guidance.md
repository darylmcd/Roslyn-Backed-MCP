# retired-route-recovery-operations-guidance — Refresh recovery and flake remediation routes

**row:** `retired-route-recovery-operations-guidance` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.claude/skills/recover-stalled-subagent/SKILL.md`
- `ai_docs/known-flakes.md`

## Acceptance

- [ ] Recovery and known-flake guidance route active work through `/backlog-remediate` without changing durable recovery or flake-handling semantics.

## Regression shape

Run the retired-name inventory over both anchors and require every remaining match to be explicitly historical or compatibility-only.

## Evidence

- PR #1480 contains the prepared changes for both anchors.

## Context

Split from `retired-route-recovery-guidance`, sourced from global parent `bl-0318`; closing this row also deletes this detail file.
