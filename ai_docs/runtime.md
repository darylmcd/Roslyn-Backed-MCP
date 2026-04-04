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

## Roslyn MCP client policy (AI sessions)

Use the **Roslyn MCP server** for C# work in this repository—not only for discovery (navigation, search, diagnostics) but also for **refactoring and other structured edits**.

- **Enable the server:** Repo root `.mcp.json` registers the `roslyn` MCP server (`type: stdio`, `command: roslynmcp`). Cursor may also use a user-level MCP config; keep a `roslyn` / `roslynmcp` entry so agents can reach the same host.
- **Prefer Roslyn tools for C# changes:** When a Roslyn-backed tool exists for the task (for example `rename_*`, `extract_*`, `move_type_*`, `code_fix_*`, `organize_usings_*`, `bulk_replace_type_*`, `split_class_*`), use it instead of hand-editing multiple files or relying on generic text replacement across the solution.
- **Preview before apply:** Use `*_preview` (or equivalent preview flows), review the diff, then call `*_apply` with the returned handles. Align with [Session And Mutation Safety](#session-and-mutation-safety) (workspace id, version checks).
- **Discovery is not a substitute for refactors:** Navigation and read-only tools (`find_references`, `symbol_search`, `go_to_definition`, etc.) inform the plan; they do not replace semantic refactors when a tool implements the change safely.

For tool selection and workflows, see `domains/tool-usage-guide.md`.

## Known issues (local validation)

- **Parallel test hosts / MSBuild file locks:** If `dotnet test` or `dotnet build` fails with errors copying the test assembly (`RoslynMcp.Tests.dll`) because `testhost.exe` still holds the file, close other test runners or IDE test sessions that loaded that output, then run a full `dotnet test RoslynMcp.slnx --nologo` again from a clean state.

## Session And Mutation Safety

- Maintain and pass `workspaceId` for workspace-scoped operations.
- Use preview/apply flows for destructive or broad changes.
- Reject or regenerate previews if workspace version changed.

## Policy Ownership

- Git/worktree/PR behavior: `workflow.md`
- Validation and merge gating: `../CI_POLICY.md`
- Backlog of unfinished work: `backlog.md`
