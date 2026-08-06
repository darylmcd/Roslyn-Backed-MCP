# compile-check-project-filter-normalize — normalize compile_check's projectFilter once so a blank projectName cannot misreport scope

**row:** `compile-check-project-filter-normalize` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:47`
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:74`
- `src/RoslynMcp.Roslyn/Helpers/ProjectFilterHelper.cs:9`
- `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs:57`

## Acceptance

- [ ] `projectFilter` is trimmed to null once at `CheckAsync` entry (or `FilterProjects` switches to `IsNullOrWhiteSpace`) so `FilterProjects`, `ResolveProjectScope`, `BuildHint`'s zero-projects branch, `ComputeRequestedScope`, and the tool-layer 0-project throw all agree on "no filter".
- [ ] Regression test: `projectName=" "` with multi-project `files[]` no longer returns `actualScope:"solution"` alongside `TotalProjects:0`.

## Evidence

- Code-quality review of PR #1151 (`compile-check-multi-project-fallback-structured-scope`): traced at code level — `FilterProjects` branches on `projectFilter is null` while `ResolveProjectScope`/`ComputeRequestedScope` branch on `IsNullOrWhiteSpace`, so a whitespace-only `projectName` yields zero filtered projects but reports `actualScope:"solution"`, and the tool-layer guard (which uses `IsNullOrWhiteSpace`) doesn't throw either.

## Context

Spin-off from the `compile-check-multi-project-fallback-structured-scope` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1151).
