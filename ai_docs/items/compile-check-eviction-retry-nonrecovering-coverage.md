# compile-check-eviction-retry-nonrecovering-coverage — cover compile_check's non-recovering eviction-retry arm

**row:** `compile-check-eviction-retry-nonrecovering-coverage` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:278` (`ReadByWorkspaceIdWithEvictionRetryAsync` — `if (reloadedId is null) throw;`)
- `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs:46`
- `tests/RoslynMcp.Tests/WorkspaceEvictionAutoRetryTests.cs`

## Acceptance

- [ ] A test pins `compile_check` with an `IWorkspaceManager` wired and the evicted session's recorded `LoadedPath` deleted: the reload fails, `ReadByWorkspaceIdWithEvictionRetryAsync` rethrows, and the pre-existing NotFound error envelope reaches the caller unchanged.
- [ ] A test pins the never-loaded/typo'd `workspaceId` shape for `compile_check` (manager wired): `TryReclassifyAsEvicted` returns null, no MSBuild reload is attempted, envelope unchanged.

## Evidence

- Code-quality review of PR #1167 (`workspace-eviction-retry-untested-branches`): `rg -l ReadByWorkspaceIdWithEvictionRetryAsync tests/` returns nothing — `compile_check`'s two existing eviction tests cover only the reload-succeeds arm and the `workspaceManager: null` arm, never entering the `when (workspaceManager is not null)` catch filter that the non-recovering branches live in.

## Context

Spin-off from the `workspace-eviction-retry-untested-branches` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1167), which scoped its acceptance to `test_run` only. This is the `compile_check` sibling gap, not a duplicate.
**Amend from PR #1172 (`workspace-eviction-retry-swallowed-log`) code-quality review — fold into this row's scope when implemented, do not file a sibling:**

Consolidated dedupe alongside the coverage work this row already plans in `tests/RoslynMcp.Tests/WorkspaceEvictionAutoRetryTests.cs`:

- `RecordingLoggerFactory` / `RecordingLogger` / `LogEntry` in that file are byte-identical to `WorkspaceCloseDrainTests.cs:639-669` — hoist all three into `tests/RoslynMcp.Tests/TestInfrastructure/` (e.g. `RecordingLoggerFixtures.cs`) and have both test classes consume the shared copy.
- `ToolDispatch.cs:424`'s `private static ILogger? CreateLogger(ILoggerFactory? loggerFactory)` duplicates `WorkspaceTools.cs:702`'s helper (differs only in the category type) — extract one shared `ToolLogging.CreateLogger<T>(ILoggerFactory?)` helper in the Tools namespace and call it from both.

Evidence: direct comparison confirmed `WorkspaceEvictionAutoRetryTests.cs:616-646` and `WorkspaceCloseDrainTests.cs:639-669` are byte-identical for all three types (the new copy's own XML doc even states "Copied from WorkspaceCloseDrainTests.cs"); `ToolDispatch.cs:424` and `WorkspaceTools.cs:702` are the same two-line body differing only in `typeof()`.
