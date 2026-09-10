# reconcile-plan-force-worktree-removal-safety — reconcile-plan-force-worktree-removal-safety

**row:** `reconcile-plan-force-worktree-removal-safety` · **pri:** `Medium` · **size:** `S`

## Anchors

- `.claude/skills/reconcile-backlog-sweep-plan/SKILL.md`
- `eng/verify-ai-docs.ps1`

## Acceptance

- [ ] Normal reconciliation refuses dirty initiative worktrees and hands cleanup to canonical shipping; it never force-removes normal retained work.
- [ ] Keep the explicitly disposable audit-worktree exception documented separately.
- [ ] Add one static verifier regression that rejects blanket force-removal guidance in the active reconciliation skill.

## Evidence

- The reconciliation skill currently directs normal post-merge cleanup to `git worktree remove --force`, which can discard unreviewed residue.
