# test-local-manager-dispose-before-copy-delete — Await local manager shutdown before deleting copied roots

**row:** `test-local-manager-dispose-before-copy-delete` · **pri:** `Medium` · **size:** `L` · **deps:** `test-owned-copy-workspace-close-before-delete`

## Anchors

- `tests/RoslynMcp.Tests/HardeningBehaviorTests.cs:42`
- `tests/RoslynMcp.Tests/WorkspaceCapLruEvictionTests.cs:82`, `:117`, `:179`
- `tests/RoslynMcp.Tests/WorkspaceEvictionAutoRetryTests.cs:59`, `:97`, `:141`, `:180`, `:237`, `:294`, `:373`, `:434`, `:490`
- `tests/RoslynMcp.Tests/WorkspaceManagerEvictionTests.cs:281`
- `tests/RoslynMcp.Tests/WorkspaceReloadedEventTests.cs:127`

## Acceptance

- [ ] Fix the 15 listed methods that declare a local manager before `try` and delete copied roots in `finally` before manager disposal begins; preserve partial setup ownership.
- [ ] Observe red-first lifetime assertions on actual affected tests, then await `WorkspaceManager.DisposeAsync()` before deleting each owned copy. Keep existing scenario assertions and intentional mid-test deletions.
- [ ] Run cleanup steps independently and preserve body and cleanup failures. File dependency-chained remainder rows before the first slice merges if reviewable slices are needed.

## Evidence

- 2026-11-05 cold remediation audit: 15 methods across five files declare local `using var manager` outside `try` and delete copied roots in `finally` before disposal begins. `WorkspaceManager.Dispose()` only calls `RequestStop`; `DisposeAsync()` awaits `ShutdownAsync` and session removal (`src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1266-1335`).

## Context

- This is the local-manager shutdown-order counterpart to `test-owned-copy-workspace-close-before-delete`, which fixes three assembly-owned manager sessions that never close until assembly teardown. `test-local-manager-sync-dispose-shutdown-qualification` tracks unproven races where lexical synchronous disposal precedes deletion but may leave shutdown pending. The existing `test-fixture-primary-cleanup-preservation` row tracks sequential cleanup failure masking as a separate mechanism.
