# Workflow

<!-- purpose: Git branches, worktrees, PRs, and merge-ready handoff for this repo. -->

This document is the single owner for branch, worktree, pull-request, and merge-ready handoff workflow.

## Branching

- Use a task branch for write work.
- Keep one branch per concern.

## Concurrent Write Isolation

- If another write-capable session is active or likely, use a dedicated git worktree.
- Do not run concurrent write sessions against the same branch.

## Pull Requests

- Use a pull request for merge-ready handoff.
- Keep the pull request scoped to one concern.

## Merge-Ready Handoff

- Run the required validation from `../CI_POLICY.md`.
- Resolve merge conflicts before merge handoff.
- If repository settings require the branch to be up to date with base, sync it before merge.

## Backlog closure

- When a change **closes** rows in `ai_docs/backlog.md`, update that file in the **same PR** (or an immediate follow-up).
- **Implementation plans** must include a final todo such as `backlog: sync ai_docs/backlog.md` so the backlog stays aligned with shipped work.
- **Multi-repo deep-review campaigns:** run `eng/new-deep-review-batch.ps1` from repo root (imports sibling audits, rollup, backlog merge). See `ai_docs/procedures/deep-review-backlog-intake.md` for switches.

## Post-Merge Cleanup

- Delete merged task branches.
- Remove temporary worktrees created for the task.
- Start follow-up work on a new branch.

### Worktree `gh pr merge` discipline

`gh pr merge` fails inside a worktree with `fatal: '<branch>' is already used by
worktree at <primary-path>` because `gh` tries to update / delete a local
branch checked out in the primary repo. Run merge commands from the primary
repo root:

    cd "$(git rev-parse --git-common-dir)/.." && gh pr merge <n> --squash --delete-branch

See `prompts/backlog-sweep-execute.md` § Step 8 for the subagent-flow wording.

## Ownership

- Validation and merge gating belong to `../CI_POLICY.md`.
- Runtime constraints belong to `runtime.md`.
