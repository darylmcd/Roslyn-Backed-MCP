# actionlint-release-download-api-fallback — Keep pinned CI validation available

**row:** `actionlint-release-download-api-fallback` · **pri:** `High` · **size:** `S`

## Anchors

- `eng/verify-actionlint.ps1:256`
- `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs:308`

## Acceptance

- [ ] When the pinned release URL fails, fetch the same version's pinned GitHub API asset and verify the existing archive and extracted-binary SHA-256 values before execution.
- [ ] If both endpoints fail or either downloaded archive has the wrong hash, fail closed with an actionable diagnostic.
- [ ] Exercise the fallback with a forced primary failure and run the actionlint contract and workflow gates.

## Evidence

- PR #2025 Linux docs shard 3, PR #2026 Linux shard 3, and PR #2027 Linux shard 3 each failed in `eng/verify-actionlint.ps1` because the pinned release URL returned HTTP 504. PR #2024 passed the same gate on the unchanged script.
- On 2026-11-08, the pinned Linux x64 GitHub API asset ID `384924896` downloaded successfully and matched the committed archive SHA-256 `8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8`.

## Context

- The GitHub release gateway is external. This row addresses the single-endpoint bootstrap fragility while retaining the existing pin and fail-closed checks.
- `verify-actionlint-per-worktree-download` separately tracks reuse of a verified binary across local worktrees; ephemeral hosted runners still need this network fallback.
