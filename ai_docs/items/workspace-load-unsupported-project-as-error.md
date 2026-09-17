# workspace-load-unsupported-project-as-error — Report unsupported project types as skipped, not workspace errors

**row:** `workspace-load-unsupported-project-as-error` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/WorkspaceDiagnosticSeverityClassifier.cs`

## Acceptance

- [ ] A .slnx containing a .wixproj loads with workspaceErrorCount 0 and a skipped-project entry naming the file and reason.
- [ ] A genuinely failing C# project still reports WORKSPACE_FAILURE at Error.

## Evidence

- 2026-09-16 SnipCue.slnx: workspace_load returns WORKSPACE_FAILURE severity Error "Cannot open project ...SnipCue.Installer.wixproj because the file extension .wixproj is not associated with a language"; every implementation and review subagent this session had to explain the error count away.
