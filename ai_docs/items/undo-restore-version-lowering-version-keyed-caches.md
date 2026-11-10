# undo-restore-version-lowering-version-keyed-caches — Verify version-keyed per-workspace caches cannot serve stale content after an undo lowers the workspace version

**row:** `undo-restore-version-lowering-version-keyed-caches` · **pri:** `Medium` · **size:** `M` · **verified:** `df2f06c1dc495c3e0c4dfaf4ae7e67a54b044efa`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs`
- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs`

## Acceptance

- [ ] Determine whether `RestoreVersion` (`UndoService.cs:~452`, `~676`) can set a workspace version equal to one a version-keyed cache (`TestDiscoveryService`, `DiagnosticQueryService`, `NuGetDependencyService`, others) already holds an entry for, with different content; if so, either evict per-workspace caches on restore or make cache keys a per-session generation, not the version alone.
- [ ] A red-first deterministic test reproduces the stale hit (apply, discover, undo, change, rediscover) on the current code, or the row closes with evidence that the cache is evicted before the version can recur.
- [ ] Every version-keyed cache in `src/` is listed with `file:line` and covered or shown safe.

## Evidence

- PR 2059 delta review (2026-11-10): `RestoreVersion` can lower the version after `ReloadAsync` has evicted, so an in-flight publish at a bumped version could leave a higher-version entry; an equality-keyed read could hit stale content when that version is reached again. The newer-version guard that made this reachable was reverted from PR 2059, but the underlying version-reuse question is unverified.

## Context

- Premise is unverified: re-derive before implementing. Internal caches only; patch-level.
