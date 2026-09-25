# verify-actionlint-per-worktree-download — Cache the pinned actionlint binary outside the worktree

**row:** `verify-actionlint-per-worktree-download` · **pri:** `Low` · **size:** `S`

## Anchors

- `eng/verify-actionlint.ps1:235`

## Acceptance

- [ ] The pinned, SHA-256-verified actionlint binary is cached in a per-user location keyed by version + hash, reused across worktrees, and re-verified before use.
- [ ] A fresh worktree with no network still passes `verify-actionlint` when the per-user cache is warm.

## Evidence

- HEAD `eng/verify-actionlint.ps1:235` `$toolRoot = Join-Path $RepoRoot "artifacts/tools/actionlint/$PinnedVersion"` — the cache lives inside each worktree. On 2026-09-24 the integration gate for PR #1612 failed at `:267` `no cached actionlint binary ... could not be downloaded ... The SSL connection could not be established` in a fresh integration worktree; a retry passed.

## Context

Observed during strict per-PR landing (each gate cuts a new throwaway worktree).
