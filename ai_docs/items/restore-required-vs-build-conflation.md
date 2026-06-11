# restore-required-vs-build-conflation — distinguish buildRequired from restoreRequired

**row:** `restore-required-vs-build-conflation` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:76`

## Acceptance

- [ ] A `buildRequired`/build-hint state distinguished when the unmet dependency is an analyzer/project build output rather than a NuGet restore input
- [ ] Regression: fixture with an unbuilt analyzer dependency asserts `buildRequired` (not `restoreRequired`)

## Evidence

- `audit-reports/20260531T192823Z_roslyn-backed-mcp_mcp-server-surface-test.md` Phase 0, server v2.3.1. Source: 2026-05-31 surface-test.

## Context

On a freshly-checked-out worktree, `workspace_load`/`workspace_reload` (even `autoRestore=true`) reported `restoreRequired=true` + restoreHint "Run `dotnet restore`", but the real unmet input was a BUILD output (`WORKSPACE_UNRESOLVED_ANALYZER`: the unbuilt netstandard2.0 `ServerSurfaceCatalogAnalyzer.dll`) — `dotnet restore` said "All projects are up-to-date", sending the caller into a no-op restore loop. (The workspace diagnostic itself correctly said "Run `dotnet build` on the analyzer project.")
