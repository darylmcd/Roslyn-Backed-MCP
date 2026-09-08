# pr-reconciler-fail-closed-ship-cleanup — Route reconciler cleanup through fail-closed shipping

**row:** `pr-reconciler-fail-closed-ship-cleanup` · **pri:** `Medium` · **size:** `S`

## Anchors

- `.claude/agents/pr-reconciler.md`
- `eng/verify-ai-docs.ps1`

## Acceptance

- [ ] Replace manual `git worktree remove --force` and swallowed remote-branch deletion failures with canonical `/ship --land=<pr>` cleanup or its fail-closed helper contract.
- [ ] Preserve explicit reporting for dirty retained worktrees and failed local or remote cleanup.
- [ ] Add a static AI-doc regression that rejects forced worktree removal and swallowed branch-deletion failures in active reconciler guidance.

## Evidence

- `.claude/agents/pr-reconciler.md:79-80` can discard dirty worktree residue and hides remote-delete failures, conflicting with the current operator-safe `/ship` cleanup contract.
