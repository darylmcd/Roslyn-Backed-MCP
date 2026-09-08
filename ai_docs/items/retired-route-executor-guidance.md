# retired-route-executor-guidance — Refresh executor-agent remediation routes

**row:** `retired-route-executor-guidance` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.claude/agents/initiative-executor.md`
- `.claude/agents/pr-reconciler.md`

## Acceptance

- [ ] Both executor-agent surfaces route active work through `/backlog-remediate` while preserving explicitly historical or compatibility-only references.

## Regression shape

Run the retired-name inventory over both anchors and require every remaining match to be explicitly historical or compatibility-only.

## Evidence

- PR #1479 contains the prepared changes for both anchors.

## Context

Split from `retired-route-executor-intake-guidance`, sourced from global parent `bl-0318`; closing this row also deletes this detail file.
