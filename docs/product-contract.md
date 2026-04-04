# Product Contract

`roslyn-mcp` ships as a local-first MCP server for developer workstations. The canonical machine-readable contract is the `server_catalog` resource at `roslyn://server/catalog`.

For AI-session operating flow and repository layout, use `AGENTS.md` as the first read.

## Session Operating Contract

The expected execution sequence for agent sessions is:

1. load workspace and keep the returned `workspaceId`
2. use stable tools/resources first
3. use preview/apply for bounded mutation
4. run build/test validation before completion

## Contract Routing Matrix

| Need | Default tier | Escalate when |
|---|---|---|
| workspace/session management, semantic navigation, diagnostics, build/test, bounded refactoring | stable | stable surface cannot express the required operation |
| scaffolding, project mutation, cross-project orchestration, dead-code removal, direct edit helpers | experimental | no stable equivalent exists and preview-first constraints are acceptable |
| prompts | experimental | never; prompts are not compatibility-stable API |

## Stable Surface

Stable support is for the local stdio host only. Stable entries follow the compatibility and deprecation rules in `docs/release-policy.md`.

Supported stable tool families:

- `server_info`
- workspace session management and inspection
- source text and source-generated document reads
- semantic symbol navigation and relationship tools
- diagnostics and impact-analysis tools
- build/test discovery, execution, and coverage collection (`test_discover`, `test_run`, `test_related`, `test_related_files`, `test_coverage`, etc.)
- security diagnostics and vulnerability scanning (`security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan`)
- preview/apply refactoring tools

Stable resources:

- `server_catalog`
- `resource_templates`
- `workspaces`
- `workspace_status`
- `workspace_projects`
- `workspace_diagnostics`
- `source_file`

## Experimental Surface

Experimental entries are intentionally discoverable but may evolve faster before a second transport or editor-backed host exists.

Current experimental families:

- advanced-analysis tools
- guided orchestration prompts
- direct text-edit tools
- workspace file operation tools
- project mutation tools, including central package management and multi-targeting mutations
- cross-project semantic refactoring previews, including interface extraction and bounded dependency inversion
- orchestration tools for package migration, class splitting, and extract-and-wire workflows
- scaffolding tools
- dead-code removal tools
- syntax-tree inspection
- generic Roslyn code actions
- all prompts

## Product Boundaries

- The production target is the local stdio host on a developer workstation.
- Workspace state comes from `MSBuildWorkspace` and on-disk files, not unsaved editor buffers.
- HTTP/SSE hosting is a future host tier, not part of the current stable contract.
- Visual Studio or editor-backed live-workspace parity is a separate integration path, not a promise of the current host.
- Destructive operations remain bounded to explicit edit requests or preview/apply flows.

## Agent Guidance

- Load a workspace first and keep using the returned `workspaceId`.
- Prefer stable tools for navigation, diagnostics, validation, and preview-first refactoring flows.
- Treat experimental tools as opt-in accelerators rather than required dependencies.
- Read `server_info` and `server_catalog` at session start when you need to adapt automatically to support tiers.

## Ownership Map By Change Type

- host/tool wrapper and catalog wiring changes: `src/RoslynMcp.Host.Stdio/`
- DTO and boundary contract changes: `src/RoslynMcp.Core/`
- Roslyn semantic/refactoring implementation changes: `src/RoslynMcp.Roslyn/`
- behavior validation and regression checks: `tests/RoslynMcp.Tests/`
