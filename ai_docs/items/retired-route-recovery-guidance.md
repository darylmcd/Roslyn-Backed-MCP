# retired-route-recovery-guidance — Refresh reconciliation and recovery routes

**row:** `retired-route-recovery-guidance` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.claude/skills/reconcile-backlog-sweep-plan/SKILL.md`
- `.claude/skills/reconcile-backlog-vs-issues/SKILL.md`
- `.claude/skills/recover-stalled-subagent/SKILL.md`
- `ai_docs/known-flakes.md`

## Acceptance

- [ ] Active reconciliation, recovery, and known-flake guidance routes through `/backlog-remediate` without changing durable recovery semantics or historical labels.

## Regression shape

Run the retired-name inventory over the four active surfaces and require every remaining match to be an explicit compatibility or historical label.

## Evidence

- The `bl-0318` inventory identified these four bounded active guidance surfaces; implementation is prepared in PR #1480.
