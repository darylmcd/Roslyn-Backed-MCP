# Workflow

This is the canonical git and pull request workflow policy for this repository.

## Branching Rules

- For production-code changes, create a task branch before editing.
- For documentation-only changes, use a branch unless explicit maintainer policy permits direct edits.
- Keep branch names scoped to one concern.

## Concurrent Write Isolation

- If another write-capable session is active or likely, use a dedicated git worktree.
- Prefer one worktree per active branch to prevent cross-task contamination.
- Do not run concurrent edits against the same branch in multiple worktrees.

## Development Loop

1. Create/switch to the task branch.
2. Implement changes.
3. Run required validation from `../CI_POLICY.md`.
4. Update docs/tests for behavior or surface changes.
5. Re-run validation if code changed after fixes.

## Merge-Ready Handoff

- Ensure branch is synchronized with base branch when required by branch protection.
- Resolve merge conflicts before requesting merge.
- Confirm required status checks are green.
- Keep handoff notes concise and factual.

## Pull Request Behavior

- Use one focused PR per concern.
- Link related issue/work item when available.
- Avoid mixing unrelated refactors with functional changes.

## Post-Merge Cleanup

- Delete merged task branches according to repository policy.
- Clean up temporary worktrees after branch deletion.
- Start follow-up work on a new branch, not a recycled closed/merged branch.

## Ownership Boundaries

- Validation and merge gating policy belongs to `../CI_POLICY.md`.
- Runtime assumptions and execution context belong to `runtime.md`.
- This file owns git/worktree/branch/PR workflow guidance.
