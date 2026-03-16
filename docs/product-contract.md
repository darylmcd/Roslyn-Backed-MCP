# Product Contract

`roslyn-mcp` ships as a local-first MCP server for developer workstations. The canonical machine-readable contract is the `server_catalog` resource at `roslyn://server/catalog`.

## Stable Surface

Stable support is for the local stdio host only. Stable entries follow the compatibility and deprecation rules in `docs/release-policy.md`.

Supported stable tool families:

- `server_info`
- workspace session management and inspection
- source text and source-generated document reads
- semantic symbol navigation and relationship tools
- diagnostics and impact-analysis tools
- build/test discovery and execution tools
- preview/apply refactoring tools

Stable resources:

- `server_catalog`
- `workspaces`
- `workspace_status`
- `workspace_projects`
- `workspace_diagnostics`
- `source_file`

## Experimental Surface

Experimental entries are intentionally discoverable but may evolve faster before a second transport or editor-backed host exists.

Current experimental families:

- advanced-analysis tools
- direct text-edit tools
- syntax-tree inspection
- generic Roslyn code actions
- coverage collection
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
