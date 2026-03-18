# Workflow

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

## Post-Merge Cleanup

- Delete merged task branches.
- Remove temporary worktrees created for the task.
- Start follow-up work on a new branch.

## Ownership

- Validation and merge gating belong to `../CI_POLICY.md`.
- Runtime constraints belong to `runtime.md`.
