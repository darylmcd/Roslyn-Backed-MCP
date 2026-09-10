# pr-reconciler-fail-closed-ship-cleanup — Route reconciler cleanup through fail-closed shipping

**row:** `pr-reconciler-fail-closed-ship-cleanup` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.claude/agents/pr-reconciler.md`
- `eng/verify-ai-docs.ps1`

## Acceptance

- [ ] Replace manual `git worktree remove --force` and swallowed remote-branch deletion failures with canonical `/ship --land=<pr>` cleanup or its fail-closed helper contract.
- [ ] Preserve explicit reporting for dirty retained worktrees and failed local or remote cleanup.
- [ ] Add a static AI-doc regression that rejects forced worktree removal and swallowed branch-deletion failures in active reconciler guidance.

## Evidence

- `.claude/agents/pr-reconciler.md:79-80` can discard dirty worktree residue and hides remote-delete failures, conflicting with the current operator-safe `/ship` cleanup contract.
Scope correction (2026-09-10): include ai_docs/prompts/backlog-sweep-addenda.md alongside .claude/agents/pr-reconciler.md and eng/verify-ai-docs.ps1. Replace raw cleanup with parent /ship --land=<pr> handoff, require SHIP_STATUS=landed-cleanup-failed reporting, and statically reject force removal, swallowed remote deletion, and continue-on-cleanup-error guidance.
