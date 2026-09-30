# logging-dependency-boundary-duration-missing — Emit duration and outcome for subprocess and workspace-load boundaries

**row:** `logging-dependency-boundary-duration-missing` · **pri:** `Medium` · **size:** `M` · **deps:** `logging-jsonl-drops-structured-state`

## Anchors

- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs`
- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `tests/RoslynMcp.Tests/ServerObservabilitySinkTests.cs`

## Acceptance

- [ ] Subprocess boundary record carries target kind, exitCode/outcome and `durationMs` fields.
- [ ] workspace_load logs a boundary record with durationMs and project count.
- [ ] Both join the tool record by one correlationId query (V7 re-run shown in the PR).
- [ ] No command arguments containing secrets are captured (arguments already path-only; add a sanitizer test).

## Evidence

- Logging audit 20260930-1340 (dimension A13); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

V7 (build_workspace): the only boundary record is "Executed dotnet command … (ExitCode=0)" (GatedCommandExecutor.cs:16-18) with the correlation id but no duration_ms; total tool time was 2220 ms and the agent cannot split process time from gate/queue time. workspace_load took 3725 ms in V1 with three MSBuild warnings and a single "Loaded workspace" line — no boundary duration for MSBuild evaluation vs file-watcher start. NuGet update check records (default HttpClient logging) carry no outcome/duration in the captured window.

**Approach:** Stopwatch around DotnetCommandRunner process lifetime and workspace load; emit through the structured-fields path with catalog EventIds. No new abstraction layer.
