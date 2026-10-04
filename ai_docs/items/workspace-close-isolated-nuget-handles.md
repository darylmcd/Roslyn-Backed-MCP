# workspace-close-isolated-nuget-handles — workspace-close-isolated-nuget-handles

**row:** `workspace-close-isolated-nuget-handles` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:524` CloseCore invalidates previews and disposes the session.
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1619` session disposal clears Workspace and releases the analyzer lease.
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` workspace_close lifecycle and resource-release contract.
- `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs`; `tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs`.

## Acceptance

- Identify the exact retained files and owning managed/native metadata or assembly load path; do not infer the cause from process names alone.
- Close an isolated workspace after semantic queries/compile/preview, keep its MCP server alive with zero workspaces, then delete its private NuGet cache successfully.
- Correct resource ownership across close, reload, eviction and host disposal for the same retention mechanism; preserve other active workspaces.
- Retain an old-behavior failing Windows filesystem regression and passing fixed result. Do not kill the whole server, weaken deletion assertions, or extend retry windows to hide retention.

## Evidence

- SnipCue plan20261004T122130Z_backlog-remediate: bl-0515 cache deletion remains EPERM with idle Roslyn PID48528, workspace_list count0.
- bl-0519 cache deletion remains EPERM with Roslyn PIDs5256 and2412; both own agents independently confirmed workspace count0 and closed sessions.
- Repeated canonical bsweep-worktree reclaim-scratch calls identify those holders and quarantine the isolated caches after worktree removal. No unrelated process was terminated.
- Current WorkspaceManager code does call Dispose and invalidates preview tokens; the lower-level retention cause is not yet established. This row tracks the observed resource-release failure, not an assumed missing Dispose call.

