# MCP Server Audit Report

> **Execution scope:** This run exercised Phases **0–1 (core)**, **Phase 5 (subset)**, **Phase 11 (semantic_search)**, **Phase 15 (resources subset)**, **Phase 17 (negative sample)**, plus **host `dotnet restore` / `dotnet test`** for validation. It did **not** complete the full sequential phase plan in `ai_docs/prompts/deep-review-and-refactor.md` (no disposable worktree, no Phase 6 applies, no Phase 8b concurrency matrix, no full prompt invocations). Ledger uses `skipped-safety` / `blocked` with explicit reasons so the live catalog remains fully accounted for.

## Header

- **Date:** 2026-04-08T03:46:00Z (report finalized UTC; tool calls ~03:41–03:45Z)
- **Audited solution:** `c:\Code-Repo\Roslyn-Backed-MCP\RoslynMcp.slnx`
- **Audited revision:** (not pinned in session — workspace from local disk)
- **Entrypoint loaded:** `RoslynMcp.slnx`
- **Audit mode:** `conservative` — no disposable git worktree; primary repo path `c:\Code-Repo\Roslyn-Backed-MCP`; destructive preview→apply families not driven end-to-end
- **Isolation:** Same working tree as developer checkout; Phase 6 / 8b / 10–13 applies intentionally omitted
- **Client:** Cursor agent session with `user-roslyn` MCP; prompts not invoked via MCP prompt API
- **Workspace id (during run):** `56ed07501b6546c49c3c9b511049ea3c` (closed at end of session)
- **Server:** `roslyn-mcp` **1.6.1** (commit f16452924ef03cd3ed17dc36b53efab29c72217a per `server_info`)
- **Roslyn / .NET:** Roslyn **5.3.0.0**, runtime **.NET 10.0.5**, OS Windows 10.0.26200
- **Scale:** 4 projects, 303 documents (`workspace_load`)
- **Repo shape:** Multi-project solution; test project **RoslynMcp.Tests**; single target **net10.0** per project; no Central Package Management in tree; analyzers present (e.g. `list_analyzers`: 17 assemblies, 451 rules); restore **passed** before semantic tools
- **Prior issue source:** `ai_docs/backlog.md` (**updated_at** 2026-04-08T03:00:00Z)
- **Debug log channel:** `no` — MCP `notifications/message` not surfaced in this Cursor agent transcript
- **Report path note:** Canonical under Roslyn-Backed-MCP `ai_docs/audit-reports/`

## Coverage summary

| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-----------|------------------|--------------|--------------------|----------------|---------|-------|
| tool | mixed | 19 | 0 | 0 | 1 | 103 | 0 | See ledger; `diagnostic_details` skipped (no diagnostics) |
| resource | server + workspace | 3 | n/a | n/a | 0 | 0 | 4 | Workspace-scoped URIs blocked after intentional `workspace_close` |
| prompt | prompts | 0 | n/a | n/a | 0 | 0 | 16 | Prompt tools not invoked in this client session |

_Counts align with catalog **2026.03**: 123 tools + 7 resources + 16 prompts = **146** ledger rows._

## Coverage ledger

| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| tool | `add_central_package_version_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `add_package_reference_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `add_pragma_suppression` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `add_project_reference_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `add_target_framework_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `analyze_control_flow` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `analyze_data_flow` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `analyze_snippet` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `apply_code_action` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `apply_composite_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `apply_multi_file_edit` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `apply_project_mutation` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `apply_text_edit` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `build_project` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `build_workspace` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `bulk_replace_type_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `bulk_replace_type_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `callers_callees` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `code_fix_apply` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `code_fix_preview` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `compile_check` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `create_file_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `create_file_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `delete_file_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `delete_file_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `dependency_inversion_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `diagnostic_details` | stable | skipped-repo-shape | 1 | No diagnostics in workspace; cannot exercise representative diagnostic_details. |
| tool | `document_symbols` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `enclosing_symbol` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `evaluate_csharp` | experimental | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `evaluate_msbuild_items` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `evaluate_msbuild_property` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `extract_and_wire_interface_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `extract_interface_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `extract_interface_cross_project_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `extract_interface_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `extract_type_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `extract_type_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `find_base_members` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `find_consumers` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `find_implementations` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `find_overrides` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `find_property_writes` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `find_references` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `find_references_bulk` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `find_reflection_usages` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `find_shared_members` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `find_type_mutations` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `find_type_usages` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `find_unused_symbols` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `fix_all_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `fix_all_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `format_document_apply` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `format_document_preview` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `format_range_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `format_range_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `get_code_actions` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `get_cohesion_metrics` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `get_completions` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `get_complexity_metrics` | experimental | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `get_di_registrations` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `get_editorconfig_options` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `get_msbuild_properties` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `get_namespace_dependencies` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `get_nuget_dependencies` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `get_operations` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `get_source_text` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `get_syntax_tree` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `go_to_definition` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `goto_type_definition` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `impact_analysis` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `list_analyzers` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `member_hierarchy` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `migrate_package_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `move_file_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `move_file_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `move_type_to_file_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `move_type_to_file_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `move_type_to_project_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `nuget_vulnerability_scan` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `organize_usings_apply` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `organize_usings_preview` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `preview_code_action` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `project_diagnostics` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `project_graph` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `remove_central_package_version_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `remove_dead_code_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `remove_dead_code_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `remove_package_reference_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `remove_project_reference_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `remove_target_framework_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `rename_apply` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `rename_preview` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `revert_last_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `scaffold_test_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `scaffold_test_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `scaffold_type_apply` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `scaffold_type_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `security_analyzer_status` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `security_diagnostics` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `semantic_search` | experimental | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `server_info` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `set_conditional_property_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `set_diagnostic_severity` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `set_editorconfig_option` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `set_project_property_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `source_generated_documents` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `split_class_preview` | experimental | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `symbol_info` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `symbol_relationships` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `symbol_search` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `symbol_signature_help` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `test_coverage` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `test_discover` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `test_related` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `test_related_files` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `test_run` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `type_hierarchy` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `workspace_close` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `workspace_list` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `workspace_load` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| tool | `workspace_reload` | stable | skipped-safety | partial-session | Partial deep-review session: not invoked here. Operator should rerun full phased prompt or `audit-feasibility-companion-cli` backlog item. |
| tool | `workspace_status` | stable | exercised | 0-5,17,15(res) | Invoked in this session. |
| resource | `server_catalog` | stable | exercised | 0,15 | `fetch_mcp_resource` roslyn://server/catalog |
| resource | `resource_templates` | stable | exercised | 0,15 | `fetch_mcp_resource` roslyn://server/resource-templates |
| resource | `workspaces` | stable | exercised | 15 | `fetch_mcp_resource` roslyn://workspaces |
| resource | `workspace_status` | stable | blocked | 15 | Session ended before workspace-scoped URI with live id |
| resource | `workspace_projects` | stable | blocked | 15 | Same — close before fetch |
| resource | `workspace_diagnostics` | stable | blocked | 15 | Same |
| resource | `source_file` | stable | blocked | 15 | Same — no file resource compares post-close |
| prompt | `explain_error` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `suggest_refactoring` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `review_file` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `analyze_dependencies` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `debug_test_failure` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `refactor_and_validate` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `fix_all_diagnostics` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `guided_package_migration` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `guided_extract_interface` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `security_review` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `discover_capabilities` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `dead_code_audit` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `review_test_coverage` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `review_complexity` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `cohesion_analysis` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |
| prompt | `consumer_impact` | experimental | blocked | 16 | Cursor session used tools/resources only; prompt invocation API not exercised |

## Verified tools (working)

- `server_info` — counts match catalog **2026.03** (56/67 tools, 7 resources, 16 prompts)
- `workspace_load` / `workspace_list` / `workspace_status` — clean load, 4 projects, 303 docs, `_meta.gateMode` rw-lock on readers
- `project_graph` — consistent project refs with `workspace_load` tree
- `project_diagnostics` — 0 diagnostics, paging; held ~8–9s wall-clock on full-workspace calls (see performance)
- `compile_check` — 0 CS diagnostics default path `ElapsedMs` ~2051; `emitValidation=true` ~9127ms, still 0 diagnostics (emit path slower but structured)
- `security_diagnostics` / `security_analyzer_status` — structured empty findings + analyzer presence
- `nuget_vulnerability_scan` — 0 CVEs, 4 projects, ~3124ms
- `list_analyzers` — pagination works (`hasMore: true` with `limit=5`)
- `get_complexity_metrics` — plausible hotspots (`ParseNuGetVulnerabilityJson` CC=27, etc.)
- `symbol_search` — resolved `WorkspaceExecutionGate` and related symbols
- `find_references` — 13 refs for `WorkspaceExecutionGate` class symbol; junk handle returned empty success (issue below)
- `analyze_snippet` (`kind=expression`) — valid, 0 diagnostics
- `evaluate_csharp` — `Enumerable.Range(1,10).Sum()` → `55`, `ElapsedMs` 97
- `semantic_search` — paired queries `"async methods returning Task<bool>"` (2 hits) vs `"methods returning Task<bool>"` (3 hits); demonstrates modifier-sensitive delta on this repo
- `test_discover` — large structured test list (host `dotnet test` **267 passed**)
- `workspace_status` (unknown id) — actionable `NotFound` with `tool: workspace_status` (contrast with `find_references` empty success)
- `workspace_close` — success
- `roslyn://server/catalog`, `roslyn://server/resource-templates`, `roslyn://workspaces` — fetched via MCP resources

## Phase 6 refactor summary

- **Target repo:** Roslyn-Backed-MCP (this repository)
- **Scope:** **N/A** — no MCP-driven applies in this conservative/partial session (no `*_apply`, no disk mutations via refactor tools)
- **Changes:** none
- **Verification:** Host `dotnet test RoslynMcp.slnx` — **Passed: 267**, Failed: 0, ~2m52s

## Concurrency matrix (Phase 8b)

**N/A — partial session.** Phase 8b (parallel read fan-out, read/write probes, lifecycle stress) was not executed. Backlog `reader-tool-elapsed-ms` notes reader tools often omit `ElapsedMs`, which limits quantitative speedup from inside the agent loop.

## Writer reclassification verification (Phase 8b.5)

**N/A — partial session.** No writer-chain probes in this run.

## Debug log capture

`client did not surface MCP log notifications` for this Cursor agent session.

## MCP server issues (bugs)

### 1. `find_references` — invalid symbol handle returns empty success

| Field | Detail |
|--------|--------|
| Tool | `find_references` |
| Input | `symbolHandle`: base64 payload `{"MetadataName":"fake"}`-style junk (`eyJNZXRhZGF0YU5hbWUiOiJmYWtlIn0=`) |
| Expected | Structured `NotFound` / validation error (per backlog `error-response-observability`) |
| Actual | `{ count: 0, totalCount: 0, references: [], hasMore: false }` (indistinguishable from legitimate zero refs) |
| Severity | incorrect result / error-message-quality (consumer ambiguity) |
| Reproducibility | always (this session) |

### 2. No new crash or timeout observed

No tool crash or hang within exercised calls; `evaluate_csharp` completed under default script timeout.

## Improvement suggestions

- **`test_run` MCP vs host tests** — session used shell `dotnet test` instead of MCP `test_run`; suggest full deep-review on disposable worktree include `test_run` for ledger parity.
- **Phase 15 resource parity** — re-run with workspace still open to populate `roslyn://workspace/{id}/diagnostics` vs `project_diagnostics` diff and `source_file` vs `get_source_text`.
- **Prompt tier documentation** — `server_info` reports `prompts: stable=0, experimental=16`; backlog `server-info-prompts-tier-doc` still relevant for first-time readers.
- **Companion CLI** — backlog `audit-feasibility-companion-cli` matches observed session limits (full phased walkthrough is larger than one agent pass).

## Performance observations

| Tool | Input scale | Wall-clock (ms) | Notes |
|------|-------------|------------------|-------|
| `project_diagnostics` | full solution, limit 30 | ~8900–9300 `_meta.heldMs` | \>5s for simple read — FLAG per deep-review threshold; likely solution-wide analyzer graph |
| `compile_check` | full solution, limit 30 | ~2051 default; ~9127 `emitValidation=true` | Emit path ~4.4× slower here (not 50–100× on this small solution) |
| `nuget_vulnerability_scan` | 4 projects | ~3124 | Network/subprocess bounded |
| `get_complexity_metrics` | limit 15 | ~301 | Fast |

## Known issue regression check

| Source id | Summary | Status |
|-----------|---------|--------|
| `semantic-search-async-modifier-doc` | async vs non-async `Task<bool>` query doc gotcha | **partially fixed / context-dependent** — on RoslynMcp.slnx, `semantic_search` `"async methods returning Task<bool>"` returned **2** results (not zero); broader query returned **3** |
| `compile-check-emit-validation-regression-check` | emitValidation behavior vs unrestored baseline | **still reproduces investigation need** — on restored clean workspace, emitValidation slower (~9s vs ~2s) but diagnostic **count** unchanged (0); investigation remains whether emit adds value when CS clean |
| `error-response-observability` | junk symbol handle empty success | **still reproduces** — `find_references` with fake handle returned empty graph (see issues §1) |
| `reader-tool-elapsed-ms` | Phase 8b quantitative matrix | **still reproduces** — partial session; most readers lack `ElapsedMs` in response payloads |

## Known issue cross-check

- New observation this run: `workspace_status` error path correctly sets `"tool": "workspace_status"` for unknown workspace id (good contrast to backlog’s `tool: unknown` note for some other tools — not reproduced here).

---

_End of report._
