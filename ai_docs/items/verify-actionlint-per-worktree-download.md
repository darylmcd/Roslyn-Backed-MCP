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

2026-10-15: fresh PR1781 worktree at5c91d362 failed the pinned actionlint download with HTTP504. Primary cache holds the exact pinned binary SHA25654ca21be3de4c7cfa26914aa8b61bd76bf573ef3caac5f80d110558cdf241718. Using the documented override exposed a separately tracked fixture-isolation defect actionlint-fixture-inherited-override; do not treat a retry as root-cause proof.
