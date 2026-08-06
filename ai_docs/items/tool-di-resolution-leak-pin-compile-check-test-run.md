# tool-di-resolution-leak-pin-compile-check-test-run — extend the ILoggerFactory schema-leak pin to compile_check and test_run

**row:** `tool-di-resolution-leak-pin-compile-check-test-run` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/ToolDiResolutionTests.cs:99-115` (hardcoded 3-element tool-name array)
- `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs:37`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:202`

## Acceptance

- [ ] `WorkspaceLifecycleTools_DoNotExposeLoggerFactoryInMcpInputSchema` (renamed to drop the now-inaccurate `WorkspaceLifecycleTools_` prefix) asserts the absence of a `loggerFactory` input-schema property for `compile_check` and `test_run` in addition to `workspace_load`/`workspace_reload`/`workspace_close`.
- [ ] The guarded tool list is derived from the registered `McpServerTool` set whose method signature carries an `ILoggerFactory` parameter, so future tools that gain the DI parameter are pinned automatically rather than by a hand-maintained string list.

## Evidence

- Code-quality review of PR #1172 (`workspace-eviction-retry-swallowed-log`): traced at code level — `ToolDiResolutionTests.cs:106` iterates a hardcoded three-element array; PR #1172 added `ILoggerFactory? loggerFactory = null` to `compile_check` and `test_run` without touching that array, so the published schemas for those two tools have no leak guard. No leak observed today — this is a gap in the regression pin, not a confirmed defect.

## Context

Spin-off from the `workspace-eviction-retry-swallowed-log` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1172).
