# Runtime

This document is the canonical runtime and execution-context reference for AI agents and maintainers.

## Execution Context

- Primary runtime target: local stdio host process.
- Workspace model: `MSBuildWorkspace` over on-disk files.
- Unsaved editor buffers are not authoritative for semantic operations.

## Platform And Tooling

- .NET SDK: 10.0.100 (rollForward: latestFeature) — see `global.json`
- Primary v1 operating system target: Windows. Cross-platform (macOS, Linux) supported wherever .NET 10 SDK is available.
- Main local validation entry point: `./eng/verify-release.ps1`
- Fast manual commands:
  - `dotnet build RoslynMcp.slnx --nologo`
  - `dotnet test RoslynMcp.slnx --nologo`
  - `dotnet run --project src/RoslynMcp.Host.Stdio`

## MCP Runtime Notes

- `stdout` is reserved for MCP protocol traffic.
- Operational logging should go to `stderr`.

## Session And Mutation Safety

- Maintain and pass `workspaceId` for workspace-scoped operations.
- Use preview/apply flows for destructive or broad changes.
- Reject or regenerate previews if workspace version changed.

## Policy Ownership

- Git/worktree/PR behavior: `workflow.md`
- Validation and merge gating: `../CI_POLICY.md`
- Backlog of unfinished work: `backlog.md`
