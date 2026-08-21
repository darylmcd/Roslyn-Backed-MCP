# filewatcher-worktree-root-relative-ignore — keep worktree sessions observable

**row:** `filewatcher-worktree-root-relative-ignore` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`
- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs`

## Acceptance

- [ ] Ignore rules are evaluated relative to the watched workspace root, so loading a solution beneath a parent `.worktrees/<id>` directory does not suppress every event inside that workspace.
- [ ] A regression loads or models a workspace rooted below `.worktrees/<id>` and proves an external source edit still marks the workspace stale.

## Evidence

- `FileWatcherService` currently applies an absolute-path `.worktrees` segment exclusion. When the watched root itself is under `.worktrees/<id>`, every descendant inherits that segment and is ignored.

## Context

Keep generated/nested repository worktree noise excluded without making the loaded workspace's own root invisible.
