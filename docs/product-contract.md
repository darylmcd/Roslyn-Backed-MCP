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

- `server_info` and `server_heartbeat`
- workspace session management and inspection (`workspace_load`, `workspace_reload`, `workspace_close`, `workspace_list`, `workspace_status`, `workspace_health`, `workspace_readiness_report`, `workspace_support_bundle`, `workspace_changes`, `project_graph`)
- source text and source-generated document reads
- semantic symbol navigation and relationship tools
- syntax-tree inspection (`get_syntax_tree`) and IDE-style operation inspection (`get_operations`)
- diagnostics and impact-analysis tools
- editorconfig / diagnostic-severity configuration (`get_editorconfig_options`, `set_editorconfig_option`, `set_diagnostic_severity`), MSBuild evaluation (`evaluate_msbuild_property`, `evaluate_msbuild_items`, `get_msbuild_properties`), and bounded direct text-edit helpers (`apply_text_edit`, `add_pragma_suppression`, `pragma_scope_widen`)
- apply undo (`revert_last_apply`, `revert_apply_by_sequence`)
- build/test discovery, execution, and coverage collection (`test_discover`, `test_run`, `test_related`, `test_related_files`, `test_coverage`, etc.)
- security diagnostics and vulnerability scanning (`security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan`)
- read-only analysis helpers promoted in v1.6.0: `compile_check`, `list_analyzers`, `find_consumers`, `get_cohesion_metrics`, `find_shared_members`, `analyze_snippet`
- read-only advanced-analysis helpers promoted in v1.8.0: `find_unused_symbols`, `get_di_registrations`, `get_complexity_metrics`, `find_reflection_usages`, `get_namespace_dependencies`, `get_nuget_dependencies`
- promoted in v1.9.0: `semantic_search`, `analyze_data_flow`, `analyze_control_flow`, `evaluate_csharp`
- promoted in v1.11.0: `get_code_actions`, `preview_code_action`, `apply_code_action`
- promoted in v1.12.0: `get_syntax_tree`, `workspace_changes`, `suggest_refactorings`, `get_operations`, `get_editorconfig_options`, `evaluate_msbuild_property`, `evaluate_msbuild_items`
- preview/apply refactoring tools whose complete token-redemption route is stable; the stable-only
  profile omits a preview when its compatible apply route is experimental

Stable resources (9):

- `server_catalog`
- `resource_templates`
- `workspaces`
- `workspaces_verbose`
- `workspace_status`
- `workspace_status_verbose`
- `workspace_projects`
- `workspace_diagnostics`
- `source_file`

### Public command-diagnostic paths

Build and test response fields may retain child-process diagnostics, but they do not expose absolute
filesystem paths. Absolute paths lexically contained by the command working directory are emitted
as `/`-normalized relative paths. Other Windows drive, UNC, and POSIX absolute paths are emitted as
the stable `<external-path>` placeholder. Compiler `(line,column)` and stack `:line N` suffixes are
preserved; URI and ratio-like text is not treated as a filesystem path.

This policy applies to command stdout, stderr, early-kill reasons, test failure messages and stack
traces, and failure-envelope summaries and tails. Internal execution records remain full fidelity.

## Experimental Surface

Experimental entries are intentionally discoverable but may evolve faster before a second transport or editor-backed host exists.

Current experimental families:

- selected analysis tools (`symbol_impact_sweep`, `semantic_grep`, `preview_record_field_addition`, `trace_exception_flow`, `find_duplicate_helpers`, `find_dead_locals`, `find_dead_fields`, `find_overloads`, `find_type_consumers`, `probe_position`)
- machine-readable surface helpers beyond the stable summary: the `server_catalog_full`, paginated catalog slice, and `server_catalog_version_diff` resources, plus the `source_file_lines` resource (5 experimental resources in total)
- guided orchestration prompts and `get_prompt_text` / `recommend_workflow`
- multi-file edit tools (`preview_multi_file_edit`, `preview_multi_file_edit_apply`, `apply_multi_file_edit`) and `apply_with_verify`
- workspace file operation apply routes (`create_file_apply`, `delete_file_apply`, `move_file_apply`; their previews are stable)
- project mutation apply routes, including `apply_project_mutation` and central package management mutations
- cross-project semantic refactoring previews, including interface extraction and bounded dependency inversion
- orchestration tools for package migration, class splitting, and extract-and-wire workflows, plus composite apply
- scaffolding tools (all but `scaffold_test_preview`)
- dead-code removal apply and interface-member removal
- refactoring families whose apply route is still experimental (for example extract-method/type/interface apply, `fix_all_*`, bulk type replacement, signature change, restructure)
- workspace validation and fork workflows (`validate_workspace`, `validate_recent_git_changes`, `workspace_fork_apply`, `test_reference_map`, `workspace_warm`, `workspace_drift_check`)
- all prompts

The live catalog (`roslyn://server/catalog`, or `server_catalog_full` for the unpaginated form) is authoritative for exact membership; this list is a family-level orientation.

## Product Boundaries

- The production target is the local stdio host on a developer workstation.
- Workspace state comes from `MSBuildWorkspace` and on-disk files, not unsaved editor buffers.
- HTTP/SSE hosting is a future host tier, not part of the current stable contract.
- Visual Studio or editor-backed live-workspace parity is a separate integration path, not a promise of the current host.
- Destructive operations remain bounded to explicit edit requests or preview/apply flows.

## Claude Code Plugin Surface

The server is also distributed as a Claude Code plugin (`roslyn-mcp`) providing:

- **MCP server**: a release-pinned `dnx Darylmcd.RoslynMcp@<version>` stdio launch declared in `.claude-plugin/mcp.json`, with `ROSLYNMCP_SANCTIONED_ROOTS="."` shipped as its filesystem boundary (no global `roslynmcp` shim required)
- **32 bundled skills** across analysis, refactoring, search, testing, workspace/session, documentation, and release workflows
- **Hooks** (`hooks/hooks.json`, PostToolUse only): a server-update notice after `server_info` and a reminder to run a compile check after structural `*_apply` calls. The hooks do not gate anything; preview-before-apply is enforced server-side, because every `*_apply` tool requires a valid, unexpired `previewToken` issued by the matching preview.

Skills compose multiple Roslyn MCP tools into guided workflows. They are not part of the MCP protocol surface — they are Claude Code-specific orchestration on top of the stable and experimental tool tiers documented above.

Install: `/plugin marketplace add darylmcd/Roslyn-Backed-MCP` then `/plugin install roslyn-mcp@roslyn-mcp-marketplace`

## Agent Guidance

- Load a workspace first and keep using the returned `workspaceId`.
- Prefer stable tools for navigation, diagnostics, validation, and preview-first refactoring flows.
- Treat experimental tools as opt-in accelerators rather than required dependencies.
- Read `server_info` and `server_catalog` at session start when you need to adapt automatically to support tiers.
- Use `roslyn://server/catalog-diff/{fromVersion}/{toVersion}` when comparing supported release surfaces; the first supported pair is `v2.3.1` to the running server version (`toVersion` also accepts the aliases `current` and `latest`).

## Ownership Map By Change Type

- host/tool wrapper and catalog wiring changes: `src/RoslynMcp.Host.Stdio/`
- DTO and boundary contract changes: `src/RoslynMcp.Core/`
- Roslyn semantic/refactoring implementation changes: `src/RoslynMcp.Roslyn/`
- behavior validation and regression checks: `tests/RoslynMcp.Tests/`
