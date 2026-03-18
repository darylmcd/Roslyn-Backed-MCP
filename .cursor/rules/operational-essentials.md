# Operational Essentials

Compact reminder layer aligned with `ai_docs/workflow.md`.

## Branching And Isolation

- Use a task branch before production-code edits.
- Use a dedicated worktree when concurrent write-capable sessions are active or likely.

## Merge-Ready Handoff

- Sync with base branch when branch protection requires it.
- Resolve conflicts and ensure required checks pass before merge request.

## Validation

- Follow `CI_POLICY.md` for required build/test/release validation.

## Ownership

- Canonical git/worktree/PR policy: `ai_docs/workflow.md`
- Canonical validation and merge gating: `CI_POLICY.md`
- Canonical runtime context: `ai_docs/runtime.md`
