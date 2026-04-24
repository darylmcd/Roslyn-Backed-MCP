# MCP Server Audit Report — Roslyn MCP (plugin-roslyn-mcp-roslyn)

## Run status (closure)

**Final surface closure: NOT SATISFIED** against the full `ai_docs/prompts/deep-review-and-refactor.md` gate (Phase 6 product refactor not completed in this run; experimental write/scaffold families mostly uninvoked; Phase 8b concurrency N/A; prompts client-blocked). **Report format: COMPLETE** — §1–§20 plus **Appendix A** (Phase 0–1 evidence) in one export file; see **Appendix B** for retention.

## 1. Header

| Field | Value |
|-------|--------|
| **Date (UTC)** | 2026-04-13 |
| **Audit mode** | full-surface (intended) |
| **Isolation** | `c:\Code-Repo\IT-Chat-Bot-wt-mcp-audit`, branch `audit/mcp-full-surface-20260413` |
| **Entrypoint** | `c:\Code-Repo\IT-Chat-Bot-wt-mcp-audit\ITChatBot.sln` |
| **Client** | Cursor agent; MCP server id `plugin-roslyn-mcp-roslyn` |
| **Workspace id (this session)** | `f2ef1e1946f34b928bcb7c20241fb00d` (closed at end of audit) |
| **Server** | roslyn-mcp 1.11.2+d0f2680698687bd6fb93c4af5ddabe22f59eb0b2 |
| **Catalog** | 2026.04 (from live `server_info` / `roslyn://server/catalog` at audit time) |
| **Roslyn / runtime** | 5.3.0.0 / .NET 10.0.5 / Windows 10.0.26200 |
| **Scale** | 34 projects, 770 documents |
| **dotnet restore** | Precondition met on worktree solution (handoff + policy) |
| **Debug log channel** | no — client did not surface MCP `notifications/message` |
| **Plugin skills path (16b)** | `C:\Users\daryl\.claude\plugins\cache\roslyn-mcp-marketplace\roslyn-mcp\1.7.0\skills` (not Roslyn-Backed-MCP repo; **version skew 1.7.0 vs server 1.11.2**) |
| **Intended canonical store** | Copy into Roslyn-Backed-MCP `ai_docs/audit-reports/` per deep-review Output Format |
| **Mismatch finding (live wins)** | Prompt banner cites 128 tools / 59 experimental; live catalog **127 tools**, **58 experimental** |

## 2. Coverage summary (by Kind and Category)

Counts from live catalog and **§3** ledger. Tier columns are catalog inventory; Exercised…Blocked columns count ledger rows in that Kind×Category group.

| Kind | Category | Tier-stable | Tier-experimental | exercised | exercised-apply | exercised-preview-only | skipped-repo-shape | skipped-safety | blocked | Notes |
|------|----------|-------------|-------------------|-----------|-----------------|------------------------|--------------------|----------------|---------|-------|
| prompt | prompts | 0 | 19 | 0 | 0 | 0 | 0 | 0 | 19 | all prompts client-blocked |
| resource | analysis | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |  |
| resource | server | 2 | 0 | 2 | 0 | 0 | 0 | 0 | 0 |  |
| resource | workspace | 6 | 0 | 5 | 0 | 0 | 0 | 0 | 1 | includes file URI blocked row |
| tool | advanced-analysis | 9 | 2 | 11 | 0 | 0 | 0 | 0 | 0 |  |
| tool | analysis | 12 | 0 | 12 | 0 | 0 | 0 | 0 | 0 |  |
| tool | code-actions | 3 | 0 | 2 | 1 | 0 | 0 | 0 | 0 |  |
| tool | configuration | 0 | 3 | 1 | 0 | 0 | 0 | 0 | 2 |  |
| tool | cross-project-refactoring | 0 | 3 | 0 | 0 | 0 | 0 | 0 | 3 |  |
| tool | dead-code | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 2 |  |
| tool | editing | 0 | 3 | 0 | 0 | 0 | 0 | 0 | 3 |  |
| tool | file-operations | 0 | 6 | 0 | 0 | 0 | 0 | 0 | 6 |  |
| tool | orchestration | 0 | 4 | 0 | 0 | 0 | 0 | 0 | 4 |  |
| tool | project-mutation | 0 | 14 | 3 | 0 | 0 | 2 | 0 | 9 |  |
| tool | refactoring | 8 | 14 | 0 | 0 | 3 | 0 | 3 | 16 |  |
| tool | scaffolding | 0 | 4 | 0 | 0 | 0 | 0 | 0 | 4 |  |
| tool | scripting | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |  |
| tool | security | 3 | 0 | 3 | 0 | 0 | 0 | 0 | 0 |  |
| tool | server | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |  |
| tool | symbols | 16 | 0 | 16 | 0 | 0 | 0 | 0 | 0 |  |
| tool | syntax | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |  |
| tool | undo | 0 | 1 | 0 | 1 | 0 | 0 | 0 | 0 |  |
| tool | validation | 8 | 0 | 8 | 0 | 0 | 0 | 0 | 0 |  |
| tool | workspace | 8 | 1 | 9 | 0 | 0 | 0 | 0 | 0 |  |

**Rollup (all 155 rows):** blocked=69, exercised=76, exercised-apply=2, exercised-preview-only=3, skipped-repo-shape=2, skipped-safety=3.

## 3. Coverage ledger

One row per live tool, resource, and prompt (snapshot at audit close; no separate ledger file retained).

| kind | name | tier | category | status | phase | lastElapsedMs | notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | server_info | S | server | exercised | 0 | 8 | Handoff + continuation |
| tool | workspace_load | S | workspace | exercised | 0 | - | Worktree; WorkspaceId f2ef1e1946f34b928bcb7c20241fb00d |
| tool | workspace_reload | S | workspace | exercised | 8 | 6948 | WorkspaceVersion 4 to 5; verbose Projects in payload |
| tool | workspace_close | S | workspace | exercised | 17e | - | End of audit session |
| tool | workspace_list | S | workspace | exercised | 0,hb | 0 | Heartbeat |
| tool | workspace_status | S | workspace | exercised | 0,17 | 0 | Lean summary; Phase17 bad id actionable NotFound |
| tool | project_graph | S | workspace | exercised | 0 | 1 | 34 projects net10.0 |
| tool | source_generated_documents | S | workspace | exercised | 11 | - | No top-level _meta FLAG |
| tool | get_source_text | S | workspace | exercised | 4 | 0 | ChatOrchestrator.cs |
| tool | symbol_search | S | symbols | exercised | 3 | 152 | ChatOrchestrator |
| tool | symbol_info | S | symbols | exercised | 3 | 4 | Class |
| tool | go_to_definition | S | symbols | exercised | 14 | - | ProcessQuestionAsync |
| tool | find_references | S | symbols | exercised | 3 | 152 | Handoff + probes |
| tool | find_implementations | S | symbols | exercised | 3 | 182 | Interface to class |
| tool | document_symbols | S | symbols | exercised | 3 | - | ChatOrchestrator.cs |
| tool | find_overrides | S | symbols | exercised | 14 | - | Interface [] no meta; self on override FLAG |
| tool | find_base_members | S | symbols | exercised | 14 | - | ProcessQuestionAsync interface |
| tool | member_hierarchy | S | symbols | exercised | 3 | 7 | Teams override base virtual |
| tool | symbol_signature_help | S | symbols | exercised | 3 | 0 | ProcessQuestionAsync |
| tool | symbol_relationships | S | symbols | exercised | 3 | 3 | baseMembers ProcessQuestionAsync |
| tool | find_references_bulk | S | symbols | exercised | 14 | 9 | symbols array |
| tool | find_property_writes | S | symbols | exercised | 3 | 11 | GitSyncResult.Success |
| tool | enclosing_symbol | S | symbols | exercised | 14 | 3 | Caret promotion |
| tool | goto_type_definition | S | symbols | exercised | 14 | 0 | FAIL method name; PASS ChatRequest |
| tool | get_completions | S | symbols | exercised | 14 | 137 | IsIncomplete |
| tool | project_diagnostics | S | analysis | exercised | 1 | 17599 | Handoff paged totals |
| tool | diagnostic_details | S | analysis | exercised | 1 | - | Handoff CA1859 |
| tool | type_hierarchy | S | analysis | exercised | 3 | 19 | IChatOrchestrator |
| tool | callers_callees | S | analysis | exercised | 3 | 134 | ProcessQuestionAsync |
| tool | impact_analysis | S | analysis | exercised | 3 | 2 | Blast radius |
| tool | find_type_mutations | S | analysis | exercised | 3 | 2 | ChatOrchestrator |
| tool | find_type_usages | S | analysis | exercised | 3 | 1 | ChatOrchestrator |
| tool | build_workspace | S | validation | exercised | 8 | 13192 | Handoff sln build |
| tool | build_project | S | validation | exercised | 8 | 1531 | ITChatBot.Chat |
| tool | test_discover | S | validation | exercised | 8 | 21 | Handoff totalCount 897 |
| tool | test_run | S | validation | exercised | 8 | 5497 | Chat.Tests filtered5 pass |
| tool | test_related | S | validation | exercised | 8 | - | Array only no _meta FLAG |
| tool | test_related_files | S | validation | exercised | 8 | 11 | Filter string |
| tool | rename_preview | S | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | rename_apply | S | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | organize_usings_preview | S | refactoring | exercised-preview-only | 6e | 7560 | Empty diff |
| tool | organize_usings_apply | S | refactoring | skipped-safety | 6e | - | No material preview |
| tool | format_document_preview | S | refactoring | exercised-preview-only | 6e | 6974 | Empty diff |
| tool | format_document_apply | S | refactoring | skipped-safety | 6e | - | No material preview |
| tool | code_fix_preview | S | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | code_fix_apply | S | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | find_unused_symbols | S | advanced-analysis | exercised | 2 | 1619|7460 | false 0 hits; true limit=5 low-confidence enums |
| tool | get_di_registrations | S | advanced-analysis | exercised | 11 | 1906 | Large list |
| tool | get_complexity_metrics | S | advanced-analysis | exercised | 2 | 177 | Handoff limit=5 |
| tool | find_reflection_usages | S | advanced-analysis | exercised | 11 | 1725 | 54 hits |
| tool | get_namespace_dependencies | S | advanced-analysis | exercised | 2 | 1439 | Large output |
| tool | get_nuget_dependencies | S | advanced-analysis | exercised | 2 | 3080 | CPM-aware |
| tool | semantic_search | S | advanced-analysis | exercised | 11 | 94 | Fallback warning |
| tool | apply_text_edit | E | editing | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | apply_multi_file_edit | E | editing | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | create_file_preview | E | file-operations | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | create_file_apply | E | file-operations | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | delete_file_preview | E | file-operations | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | delete_file_apply | E | file-operations | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | move_file_preview | E | file-operations | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | move_file_apply | E | file-operations | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | add_package_reference_preview | E | project-mutation | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | remove_package_reference_preview | E | project-mutation | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | add_project_reference_preview | E | project-mutation | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | remove_project_reference_preview | E | project-mutation | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | set_project_property_preview | E | project-mutation | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | add_target_framework_preview | E | project-mutation | skipped-repo-shape | 13 | - | Single TFM |
| tool | remove_target_framework_preview | E | project-mutation | skipped-repo-shape | 13 | - | Single TFM |
| tool | set_conditional_property_preview | E | project-mutation | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | add_central_package_version_preview | E | project-mutation | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | remove_central_package_version_preview | E | project-mutation | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | apply_project_mutation | E | project-mutation | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | scaffold_type_preview | E | scaffolding | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | scaffold_type_apply | E | scaffolding | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | scaffold_test_preview | E | scaffolding | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | scaffold_test_apply | E | scaffolding | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | remove_dead_code_preview | E | dead-code | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | remove_dead_code_apply | E | dead-code | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | move_type_to_project_preview | E | cross-project-refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | extract_interface_cross_project_preview | E | cross-project-refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | dependency_inversion_preview | E | cross-project-refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | migrate_package_preview | E | orchestration | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | split_class_preview | E | orchestration | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | extract_and_wire_interface_preview | E | orchestration | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | apply_composite_preview | E | orchestration | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | test_coverage | S | validation | exercised | 8 | 4222 | coverlet missing Success+Error FLAG |
| tool | get_syntax_tree | E | syntax | exercised | 4 | 1 | MethodDeclaration |
| tool | get_code_actions | S | code-actions | exercised | 6g | 151 | GitSyncProvider |
| tool | preview_code_action | S | code-actions | exercised | 6g | 244 | Expression body |
| tool | apply_code_action | S | code-actions | exercised-apply | 6g | 36 | Then reverted |
| tool | security_diagnostics | S | security | exercised | 1 | - | Handoff |
| tool | security_analyzer_status | S | security | exercised | 1 | - | Handoff |
| tool | nuget_vulnerability_scan | S | security | exercised | 1 | - | Handoff 0 CVEs |
| tool | find_consumers | S | analysis | exercised | 3 | 9 | ChatOrchestrator metadataName |
| tool | get_cohesion_metrics | S | analysis | exercised | 2 | 119 | minMethods=3 limit=5 |
| tool | find_shared_members | S | analysis | exercised | 3 | 1 | AdapterRegistry |
| tool | move_type_to_file_preview | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | move_type_to_file_apply | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | extract_interface_preview | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | extract_interface_apply | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | bulk_replace_type_preview | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | bulk_replace_type_apply | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | extract_type_preview | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | extract_type_apply | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | workspace_changes | E | workspace | exercised | 6k | 2 | Audit trail includes reverted apply |
| tool | suggest_refactorings | E | advanced-analysis | exercised | 2 | 1149 | 20 suggestions |
| tool | extract_method_preview | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | extract_method_apply | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | revert_last_apply | E | undo | exercised-apply | 9 | 7567 | Undo code action |
| tool | analyze_data_flow | S | advanced-analysis | exercised | 4 | 0 | Expression-bodied method |
| tool | analyze_control_flow | S | advanced-analysis | exercised | 4 | 0 | Synthesized v1.8 |
| tool | compile_check | S | validation | exercised | 1 | 810 | limit=20 post-reload 0 diagnostics |
| tool | list_analyzers | S | analysis | exercised | 1 | - | Handoff paging |
| tool | fix_all_preview | E | refactoring | exercised-preview-only | 6a | 7054 | CA1859 doc 0 changes no fixer |
| tool | fix_all_apply | E | refactoring | skipped-safety | 6a | - | No preview token |
| tool | get_operations | E | advanced-analysis | exercised | 4 | 0 | RunAsync invocation |
| tool | format_range_preview | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | format_range_apply | E | refactoring | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | analyze_snippet | S | analysis | exercised | 5 | 109 | Handoff |
| tool | evaluate_csharp | S | scripting | exercised | 5 | 368 | Handoff |
| tool | get_editorconfig_options | E | configuration | exercised | 7 | 6907 | Merged options |
| tool | set_editorconfig_option | E | configuration | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | evaluate_msbuild_property | E | project-mutation | exercised | 7b | 55 | TargetFramework net10.0 |
| tool | evaluate_msbuild_items | E | project-mutation | exercised | 7b | 61 | Compile items |
| tool | get_msbuild_properties | E | project-mutation | exercised | 7b | 72 | RootNamespace filter |
| tool | set_diagnostic_severity | E | configuration | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| tool | add_pragma_suppression | E | editing | blocked | - | - | Not exercised in 2026-04-13 audit continuation |
| resource | `roslyn://server/catalog` | S | server | exercised | 0,15 | - | Catalog fetched during audit |
| resource | `roslyn://server/resource-templates` | S | server | exercised | 0,15 | - | Fetched during audit |
| resource | `roslyn://workspaces` | S | workspace | exercised | 15 | 0 | Fetched during audit |
| resource | `roslyn://workspaces/verbose` | S | workspace | exercised | 15 | - | Fetched during audit |
| resource | `roslyn://workspace/{workspaceId}/status` | S | workspace | exercised | 15 | - | Fetched during audit |
| resource | `roslyn://workspace/{workspaceId}/status/verbose` | S | workspace | exercised | 15 | - | Fetched during audit |
| resource | `roslyn://workspace/{workspaceId}/projects` | S | workspace | exercised | 15 | - | Fetched during audit |
| resource | `roslyn://workspace/{workspaceId}/diagnostics` | S | analysis | exercised | 15 | - | Fetched during audit |
| resource | `roslyn://workspace/{workspaceId}/file/{filePath}` | S | workspace | blocked | 15 | - | Cursor fetch_mcp_resource Windows path URI client limitation |
| prompt | explain_error | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | suggest_refactoring | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | review_file | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | analyze_dependencies | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | debug_test_failure | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | refactor_and_validate | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | fix_all_diagnostics | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | guided_package_migration | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | guided_extract_interface | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | security_review | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | discover_capabilities | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | dead_code_audit | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | review_test_coverage | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | review_complexity | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | cohesion_analysis | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | consumer_impact | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | guided_extract_method | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | msbuild_inspection | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |
| prompt | session_undo | E | prompts | blocked | 16 | - | Cursor prompts/get not invokable |


## 4. Verified tools (working)

- `server_info` — surface counts match catalog totals.
- `workspace_load` / `workspace_list` / `workspace_status` / `project_graph` — session healthy on disposable worktree.
- `compile_check` — 0 CS diagnostics paginated; solution-wide read ~8–11s (handoff + post-reload).
- `project_diagnostics` — stable totals 0/23/2282; ~17.6s full scan (handoff).
- `symbol_search` → `symbol_info` / `document_symbols` / `type_hierarchy` / `find_references` / `find_references_bulk` (`symbols` payload) / `find_implementations` — consistent graph for `ChatOrchestrator` / `IChatOrchestrator`.
- `get_code_actions` / `preview_code_action` / `apply_code_action` / `revert_last_apply` — full chain exercised; reverted before close.
- `workspace_reload` — WorkspaceVersion bump; verbose project payload.
- `get_complexity_metrics` — hotspots align with `ai_docs/backlog.md` queue-refactor rows.
- `build_workspace` — `dotnet build` exit 0.
- `test_discover` — totalCount **897**.
- `test_run` — filtered `Chat.Tests` run pass (~5.5s).
- `analyze_snippet` / `evaluate_csharp` — expression probes PASS.
- `fetch_mcp_resource` — 8/9 URIs OK; workspace file template FAIL (§14).
- Phase 1 tools (`security_*`, `nuget_vulnerability_scan`, `list_analyzers`, `diagnostic_details`) — call-level timings in **Appendix A**.

## 5. Phase 6 refactor summary

**Audit-only write path; no product refactor.** Workspace `f2ef1e1946f34b928bcb7c20241fb00d`: `get_code_actions` → `preview_code_action` → `apply_code_action` → `revert_last_apply` with `workspace_changes` trail. `fix_all_preview` for CA1859 returned zero edits / no `fix_all_apply` token. No rename/extract/move product refactor in this run.

## 6. Performance baseline (`_meta.elapsedMs`)

Single-call tools: p90=max=p50. Multi-call or ranged ms noted in **Notes**. Tier/Category from live catalog.

| Tool / URI | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| `server_info` | stable | server | 1 | 8 | 8 | 8 | ITChatBot.sln / 34 projects | within | Handoff + continuation |
| `workspace_load` | stable | workspace | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | Worktree; WorkspaceId f2ef1e1946f34b928bcb7c20241fb00d |
| `workspace_reload` | stable | workspace | 1 | 6948 | 6948 | 6948 | ITChatBot.sln / 34 projects | within | WorkspaceVersion 4 to 5; verbose Projects in payload |
| `workspace_close` | stable | workspace | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | End of audit session |
| `workspace_list` | stable | workspace | 1 | 0 | 0 | 0 | ITChatBot.sln / 34 projects | within | Heartbeat |
| `workspace_status` | stable | workspace | 1 | 0 | 0 | 0 | ITChatBot.sln / 34 projects | within | Lean summary; Phase17 bad id actionable NotFound |
| `project_graph` | stable | workspace | 1 | 1 | 1 | 1 | ITChatBot.sln / 34 projects | within | 34 projects net10.0 |
| `source_generated_documents` | stable | workspace | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | No top-level _meta FLAG |
| `get_source_text` | stable | workspace | 1 | 0 | 0 | 0 | ITChatBot.sln / 34 projects | within | ChatOrchestrator.cs |
| `symbol_search` | stable | symbols | 1 | 152 | 152 | 152 | ITChatBot.sln / 34 projects | within | ChatOrchestrator |
| `symbol_info` | stable | symbols | 1 | 4 | 4 | 4 | ITChatBot.sln / 34 projects | within | Class |
| `go_to_definition` | stable | symbols | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | ProcessQuestionAsync |
| `find_references` | stable | symbols | 1 | 152 | 152 | 152 | ITChatBot.sln / 34 projects | within | Handoff + probes |
| `find_implementations` | stable | symbols | 1 | 182 | 182 | 182 | ITChatBot.sln / 34 projects | within | Interface to class |
| `document_symbols` | stable | symbols | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | ChatOrchestrator.cs |
| `find_overrides` | stable | symbols | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | Interface [] no meta; self on override FLAG |
| `find_base_members` | stable | symbols | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | ProcessQuestionAsync interface |
| `member_hierarchy` | stable | symbols | 1 | 7 | 7 | 7 | ITChatBot.sln / 34 projects | within | Teams override base virtual |
| `symbol_signature_help` | stable | symbols | 1 | 0 | 0 | 0 | ITChatBot.sln / 34 projects | within | ProcessQuestionAsync |
| `symbol_relationships` | stable | symbols | 1 | 3 | 3 | 3 | ITChatBot.sln / 34 projects | within | baseMembers ProcessQuestionAsync |
| `find_references_bulk` | stable | symbols | 1 | 9 | 9 | 9 | ITChatBot.sln / 34 projects | within | symbols array |
| `find_property_writes` | stable | symbols | 1 | 11 | 11 | 11 | ITChatBot.sln / 34 projects | within | GitSyncResult.Success |
| `enclosing_symbol` | stable | symbols | 1 | 3 | 3 | 3 | ITChatBot.sln / 34 projects | within | Caret promotion |
| `goto_type_definition` | stable | symbols | 1 | 0 | 0 | 0 | ITChatBot.sln / 34 projects | within | FAIL method name; PASS ChatRequest |
| `get_completions` | stable | symbols | 1 | 137 | 137 | 137 | ITChatBot.sln / 34 projects | within | IsIncomplete |
| `project_diagnostics` | stable | analysis | 1 | 17599 | 17599 | 17599 | ITChatBot.sln / 34 projects | warn | Handoff paged totals |
| `diagnostic_details` | stable | analysis | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | Handoff CA1859 |
| `type_hierarchy` | stable | analysis | 1 | 19 | 19 | 19 | ITChatBot.sln / 34 projects | within | IChatOrchestrator |
| `callers_callees` | stable | analysis | 1 | 134 | 134 | 134 | ITChatBot.sln / 34 projects | within | ProcessQuestionAsync |
| `impact_analysis` | stable | analysis | 1 | 2 | 2 | 2 | ITChatBot.sln / 34 projects | within | Blast radius |
| `find_type_mutations` | stable | analysis | 1 | 2 | 2 | 2 | ITChatBot.sln / 34 projects | within | ChatOrchestrator |
| `find_type_usages` | stable | analysis | 1 | 1 | 1 | 1 | ITChatBot.sln / 34 projects | within | ChatOrchestrator |
| `build_workspace` | stable | validation | 1 | 13192 | 13192 | 13192 | ITChatBot.sln / 34 projects | within | Handoff sln build |
| `build_project` | stable | validation | 1 | 1531 | 1531 | 1531 | ITChatBot.sln / 34 projects | within | ITChatBot.Chat |
| `test_discover` | stable | validation | 1 | 21 | 21 | 21 | ITChatBot.sln / 34 projects | within | Handoff totalCount 897 |
| `test_run` | stable | validation | 1 | 5497 | 5497 | 5497 | ITChatBot.sln / 34 projects | within | Chat.Tests filtered5 pass |
| `test_related` | stable | validation | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | Array only no _meta FLAG |
| `test_related_files` | stable | validation | 1 | 11 | 11 | 11 | ITChatBot.sln / 34 projects | within | Filter string |
| `organize_usings_preview` | stable | refactoring | 1 | 7560 | 7560 | 7560 | ITChatBot.sln / 34 projects | warn | Empty diff |
| `format_document_preview` | stable | refactoring | 1 | 6974 | 6974 | 6974 | ITChatBot.sln / 34 projects | warn | Empty diff |
| `find_unused_symbols` | stable | advanced-analysis | 1 | 1619 | 1619 | 1619 | ITChatBot.sln / 34 projects | within | 7460 |
| `get_di_registrations` | stable | advanced-analysis | 1 | 1906 | 1906 | 1906 | ITChatBot.sln / 34 projects | within | Large list |
| `get_complexity_metrics` | stable | advanced-analysis | 1 | 177 | 177 | 177 | ITChatBot.sln / 34 projects | within | Handoff limit=5 |
| `find_reflection_usages` | stable | advanced-analysis | 1 | 1725 | 1725 | 1725 | ITChatBot.sln / 34 projects | within | 54 hits |
| `get_namespace_dependencies` | stable | advanced-analysis | 1 | 1439 | 1439 | 1439 | ITChatBot.sln / 34 projects | within | Large output |
| `get_nuget_dependencies` | stable | advanced-analysis | 1 | 3080 | 3080 | 3080 | ITChatBot.sln / 34 projects | within | CPM-aware |
| `semantic_search` | stable | advanced-analysis | 1 | 94 | 94 | 94 | ITChatBot.sln / 34 projects | within | Fallback warning |
| `test_coverage` | stable | validation | 1 | 4222 | 4222 | 4222 | ITChatBot.sln / 34 projects | within | coverlet missing Success+Error FLAG |
| `get_syntax_tree` | experimental | syntax | 1 | 1 | 1 | 1 | ITChatBot.sln / 34 projects | within | MethodDeclaration |
| `get_code_actions` | stable | code-actions | 1 | 151 | 151 | 151 | ITChatBot.sln / 34 projects | within | GitSyncProvider |
| `preview_code_action` | stable | code-actions | 1 | 244 | 244 | 244 | ITChatBot.sln / 34 projects | within | Expression body |
| `apply_code_action` | stable | code-actions | 1 | 36 | 36 | 36 | ITChatBot.sln / 34 projects | within | Then reverted |
| `security_diagnostics` | stable | security | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | Handoff |
| `security_analyzer_status` | stable | security | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | Handoff |
| `nuget_vulnerability_scan` | stable | security | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | Handoff 0 CVEs |
| `find_consumers` | stable | analysis | 1 | 9 | 9 | 9 | ITChatBot.sln / 34 projects | within | ChatOrchestrator metadataName |
| `get_cohesion_metrics` | stable | analysis | 1 | 119 | 119 | 119 | ITChatBot.sln / 34 projects | within | minMethods=3 limit=5 |
| `find_shared_members` | stable | analysis | 1 | 1 | 1 | 1 | ITChatBot.sln / 34 projects | within | AdapterRegistry |
| `workspace_changes` | experimental | workspace | 1 | 2 | 2 | 2 | ITChatBot.sln / 34 projects | within | Audit trail includes reverted apply |
| `suggest_refactorings` | experimental | advanced-analysis | 1 | 1149 | 1149 | 1149 | ITChatBot.sln / 34 projects | within | 20 suggestions |
| `revert_last_apply` | experimental | undo | 1 | 7567 | 7567 | 7567 | ITChatBot.sln / 34 projects | within | Undo code action |
| `analyze_data_flow` | stable | advanced-analysis | 1 | 0 | 0 | 0 | ITChatBot.sln / 34 projects | within | Expression-bodied method |
| `analyze_control_flow` | stable | advanced-analysis | 1 | 0 | 0 | 0 | ITChatBot.sln / 34 projects | within | Synthesized v1.8 |
| `compile_check` | stable | validation | 1 | 810 | 810 | 810 | ITChatBot.sln / 34 projects | within | limit=20 post-reload 0 diagnostics |
| `list_analyzers` | stable | analysis | 1 | — | — | — | ITChatBot.sln / 34 projects | n/a | Handoff paging |
| `fix_all_preview` | experimental | refactoring | 1 | 7054 | 7054 | 7054 | ITChatBot.sln / 34 projects | warn | CA1859 doc 0 changes no fixer |
| `get_operations` | experimental | advanced-analysis | 1 | 0 | 0 | 0 | ITChatBot.sln / 34 projects | within | RunAsync invocation |
| `analyze_snippet` | stable | analysis | 1 | 109 | 109 | 109 | ITChatBot.sln / 34 projects | within | Handoff |
| `evaluate_csharp` | stable | scripting | 1 | 368 | 368 | 368 | ITChatBot.sln / 34 projects | within | Handoff |
| `get_editorconfig_options` | experimental | configuration | 1 | 6907 | 6907 | 6907 | ITChatBot.sln / 34 projects | within | Merged options |
| `evaluate_msbuild_property` | experimental | project-mutation | 1 | 55 | 55 | 55 | ITChatBot.sln / 34 projects | within | TargetFramework net10.0 |
| `evaluate_msbuild_items` | experimental | project-mutation | 1 | 61 | 61 | 61 | ITChatBot.sln / 34 projects | within | Compile items |
| `get_msbuild_properties` | experimental | project-mutation | 1 | 72 | 72 | 72 | ITChatBot.sln / 34 projects | within | RootNamespace filter |
| `roslyn://server/catalog` | stable | server | 1 | — | — | — | workspace session | n/a | Catalog fetched during audit |
| `roslyn://server/resource-templates` | stable | server | 1 | — | — | — | workspace session | n/a | Fetched during audit |
| `roslyn://workspaces` | stable | workspace | 1 | 0 | 0 | 0 | workspace session | within | Fetched during audit |
| `roslyn://workspaces/verbose` | stable | workspace | 1 | — | — | — | workspace session | n/a | Fetched during audit |
| `roslyn://workspace/{workspaceId}/status` | stable | workspace | 1 | — | — | — | workspace session | n/a | Fetched during audit |
| `roslyn://workspace/{workspaceId}/status/verbose` | stable | workspace | 1 | — | — | — | workspace session | n/a | Fetched during audit |
| `roslyn://workspace/{workspaceId}/projects` | stable | workspace | 1 | — | — | — | workspace session | n/a | Fetched during audit |
| `roslyn://workspace/{workspaceId}/diagnostics` | stable | analysis | 1 | — | — | — | workspace session | n/a | Fetched during audit |

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `find_references_bulk` | parameter naming | transcript name `symbolHandles` | server **`symbols`** | FLAG | Wrong name yields argument error. |
| MSBuild tools | parameter naming | path-style `projectPath` | **`project`** = loaded project name | FLAG | Align examples with schema. |
| `test_coverage` | return_shape | failure when collector missing | success + **Error** text re Coverlet | FLAG | Clients must parse message body. |
| `find_references` | validation | reject corrupt handle | `InvalidArgument` | — | Correct behaviour. |

**Residual:** file-resource URI gap (§14). `fix_all_preview` may return empty fix — no `fix_all_apply` token.

## 8. Error message quality

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `find_references` | Corrupt `symbolHandle` base64 | actionable | — | Clear InvalidArgument + hint. |
| `workspace_status` | Non-existent workspace id | actionable | — | NotFound suitable for retry. |
| `test_coverage` | Coverlet collector missing | vague | Return non-success or structured error code when collector missing. | Error string buried in success path. |

## 9. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|-------------------------|--------|-------|
| `project_diagnostics` | project + severity + paging | pass | Totals invariant under severity filter (handoff). |
| `compile_check` | offset/limit; file filter (handoff) | pass | `emitValidation=true` in draft; not re-probed final segment. |
| `list_analyzers` | paging + project filter | pass | Handoff. |
| `symbol_search` | limit=5 | pass |  |
| `find_references_bulk` | `symbols` array | pass | Not `symbolHandles`. |
| MSBuild | `project` = loaded name | pass |  |
| `test_discover` | paging | pass | hasMore. |
| `test_run` | filter only | pass | Full solution run not exercised. |
| Resources | workspaceId substitution | pass | file URI exception. |

## 10. Prompt verification (Phase 16)

One row per catalog prompt. All **blocked**: Cursor `prompts/get` not available on `call_mcp_tool` path.

| prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | notes |
|--------|-----------|------------|--------------------|------------|-----------|---------------------|-------|
| `explain_error` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `suggest_refactoring` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `review_file` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `analyze_dependencies` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `debug_test_failure` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `refactor_and_validate` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `fix_all_diagnostics` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `guided_package_migration` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `guided_extract_interface` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `security_review` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `discover_capabilities` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `dead_code_audit` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `review_test_coverage` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `review_complexity` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `cohesion_analysis` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `consumer_impact` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `guided_extract_method` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `msbuild_inspection` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |
| `session_undo` | n/a | n/a | n/a | n/a | — | needs-more-evidence | client-blocked |

## 11. Skills audit (Phase 16b)

Static parity: 19 `SKILL.md` under plugin **1.7.0**; tool names match catalog. **FLAG:** plugin version vs server **1.11.2**.

| skill | frontmatter_ok | tool_refs_valid | dry_run | safety_rules | notes |
|-------|----------------|-----------------|---------|--------------|-------|
| `analyze` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `bump` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `code-actions` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `complexity` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `dead-code` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `document` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `explain-error` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `extract-method` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `migrate-package` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `project-inspection` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `publish-preflight` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `refactor` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `review` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `security` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `session-undo` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `snippet-eval` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `test-coverage` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `test-triage` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |
| `update` | yes | yes (0 invalid) | pass | pass | static + catalog cross-check; no codegen execution |

## 12. Experimental promotion scorecard

| kind | name | category | status_from_ledger | p50_elapsedMs | schema_ok | error_ok | round_trip_ok | failures | recommendation | evidence |
|------|------|----------|-------------------|---------------|-----------|----------|---------------|----------|----------------|----------|
| tool | `apply_text_edit` | editing | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `apply_multi_file_edit` | editing | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `create_file_preview` | file-operations | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `create_file_apply` | file-operations | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `delete_file_preview` | file-operations | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `delete_file_apply` | file-operations | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `move_file_preview` | file-operations | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `move_file_apply` | file-operations | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `add_package_reference_preview` | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `remove_package_reference_preview` | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `add_project_reference_preview` | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `remove_project_reference_preview` | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `set_project_property_preview` | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `add_target_framework_preview` | project-mutation | skipped-repo-shape | - | n/a | yes | n/a | 0 | needs-more-evidence | Single TFM |
| tool | `remove_target_framework_preview` | project-mutation | skipped-repo-shape | - | n/a | yes | n/a | 0 | needs-more-evidence | Single TFM |
| tool | `set_conditional_property_preview` | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `add_central_package_version_preview` | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `remove_central_package_version_preview` | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `apply_project_mutation` | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `scaffold_type_preview` | scaffolding | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `scaffold_type_apply` | scaffolding | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `scaffold_test_preview` | scaffolding | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `scaffold_test_apply` | scaffolding | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `remove_dead_code_preview` | dead-code | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `remove_dead_code_apply` | dead-code | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `move_type_to_project_preview` | cross-project-refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `extract_interface_cross_project_preview` | cross-project-refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `dependency_inversion_preview` | cross-project-refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `migrate_package_preview` | orchestration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `split_class_preview` | orchestration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `extract_and_wire_interface_preview` | orchestration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `apply_composite_preview` | orchestration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `get_syntax_tree` | syntax | exercised | 1 | yes | yes | n/a | 0 | needs-more-evidence | MethodDeclaration |
| tool | `move_type_to_file_preview` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `move_type_to_file_apply` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `extract_interface_preview` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `extract_interface_apply` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `bulk_replace_type_preview` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `bulk_replace_type_apply` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `extract_type_preview` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `extract_type_apply` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `workspace_changes` | workspace | exercised | 2 | yes | yes | n/a | 0 | needs-more-evidence | Audit trail includes reverted apply |
| tool | `suggest_refactorings` | advanced-analysis | exercised | 1149 | yes | yes | n/a | 0 | needs-more-evidence | 20 suggestions |
| tool | `extract_method_preview` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `extract_method_apply` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `revert_last_apply` | undo | exercised-apply | 7567 | yes | yes | yes | 0 | needs-more-evidence | Undo code action |
| tool | `fix_all_preview` | refactoring | exercised-preview-only | 7054 | yes | yes | preview-only | 0 | needs-more-evidence | CA1859 doc 0 changes no fixer |
| tool | `fix_all_apply` | refactoring | skipped-safety | - | n/a | yes | n/a | 0 | needs-more-evidence | No preview token |
| tool | `get_operations` | advanced-analysis | exercised | 0 | yes | yes | n/a | 0 | needs-more-evidence | RunAsync invocation |
| tool | `format_range_preview` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `format_range_apply` | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `get_editorconfig_options` | configuration | exercised | 6907 | yes | yes | n/a | 0 | needs-more-evidence | Merged options |
| tool | `set_editorconfig_option` | configuration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `evaluate_msbuild_property` | project-mutation | exercised | 55 | yes | yes | n/a | 0 | needs-more-evidence | TargetFramework net10.0 |
| tool | `evaluate_msbuild_items` | project-mutation | exercised | 61 | yes | yes | n/a | 0 | needs-more-evidence | Compile items |
| tool | `get_msbuild_properties` | project-mutation | exercised | 72 | yes | yes | n/a | 0 | needs-more-evidence | RootNamespace filter |
| tool | `set_diagnostic_severity` | configuration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| tool | `add_pragma_suppression` | editing | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Not exercised in 2026-04-13 audit continuation |
| prompt | `explain_error` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `suggest_refactoring` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `review_file` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `analyze_dependencies` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `debug_test_failure` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `refactor_and_validate` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `fix_all_diagnostics` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `guided_package_migration` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `guided_extract_interface` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `security_review` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `discover_capabilities` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `dead_code_audit` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `review_test_coverage` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `review_complexity` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `cohesion_analysis` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `consumer_impact` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `guided_extract_method` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `msbuild_inspection` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |
| prompt | `session_undo` | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Cursor prompts/get not invokable |

## 13. Debug log capture

client did not surface MCP log notifications

## 14. MCP server issues (bugs)

| area | severity | notes |
|------|----------|-------|
| Resource `roslyn://workspace/{workspaceId}/file/{filePath}` | missing data | Windows path via `fetch_mcp_resource` not found — URI normalization FLAG. |
| Host continuity | degraded performance | Session lost between turns; `workspace_load` again — may be operator/client. |
| Prompt surface | N/A | Client limitation for `prompts/get`. |

**No new issues** for core symbol/build tools beyond file-resource FLAG.

## 15. Improvement suggestions

1. Document file-resource URI examples for Windows paths (encoding, slash direction).
2. Align `deep-review-and-refactor.md` banner counts with live catalog (127 / 58).
3. Run Phase 8b when client supports concurrent MCP tool calls.

## 16. Concurrency matrix (Phase 8b)

**N/A — client serializes tool calls** (Cursor MCP bridge); parallel fan-out not attempted.

## 17. Writer reclassification (Phase 8b.5)

**N/A** — six-writer probe not executed.

## 18. Response contract consistency

| tools | concept | inconsistency | notes |
|-------|---------|---------------|-------|
| `test_related`, `source_generated_documents` | `_meta` envelope | array-only or missing top-level `_meta` | harder unified telemetry |
| `test_coverage` | success vs error | HTTP/tool OK + Error string | treat as logical failure |

## 19. Known issue regression check (Phase 18)

Prior source: `ai_docs/backlog.md` (IT-Chat-Bot).

| item | result |
|------|--------|
| Complexity hotspots (`queue-refactor-cc-*`) | **Still reproduces** — `get_complexity_metrics` consistent with backlog. |
| Test coverage gap rows | **N/A** — product gap, not MCP defect. |
| RR-P* plan gaps | **N/A** — not server regressions. |

## 20. Known issue cross-check

No overlap between new MCP findings and IT-Chat-Bot `queue-rr-*` ids.

---

## Appendix A. Phase 0–1 detailed evidence (rolled up)

Per-call notes from the incremental audit draft, merged for a **single export artifact**. **Workspace ids:** Phase 0 initial `workspace_load` used `e80a3316e3c74f6f9d5c1c4c2f980563`; continuation and §3 ledger use `f2ef1e1946f34b928bcb7c20241fb00d` (reload/handoff).

### Phase 0 — Setup, live surface, repo shape

- **Mode:** full-surface.
- **Disposable isolation:** `c:\Code-Repo\IT-Chat-Bot-wt-mcp-audit` (git worktree, branch `audit/mcp-full-surface-20260413`). Cleanup: `git worktree remove` when finished; branch may be deleted.
- **Entrypoint:** `c:\Code-Repo\IT-Chat-Bot-wt-mcp-audit\ITChatBot.sln`.
- **dotnet restore:** succeeded on worktree solution (host shell).
- **Debug log channel:** Cursor did not surface MCP `notifications/message` — no verbatim structured logs.
- **server_info:** roslyn-mcp **1.11.2+d0f2680698687bd6fb93c4af5ddabe22f59eb0b2**, catalog **2026.04**, runtime **.NET 10.0.5**, OS Windows 10.0.26200, Roslyn **5.3.0.0**. Surface: tools stable **69** / experimental **58**; resources stable **9**; prompts experimental **19**.
- **Mismatch (live wins):** `deep-review-and-refactor.md` banner claims **59** experimental tools; live `server_info` reports **58**.
- **roslyn://server/catalog:** **127** tools, **9** resources, **19** prompts (69+58=127).
- **workspace_load (Phase 0):** WorkspaceId `e80a3316e3c74f6f9d5c1c4c2f980563`, ProjectCount **34**, DocumentCount **770**, `_meta.elapsedMs` **10502**.
- **workspace_list / workspace_status:** one session; WorkspaceDiagnosticCount **0**; status `_meta.elapsedMs` **7**.
- **project_graph:** 34 projects, **net10.0**, multi-project solution, test projects present.
- **Repo shape (IT-Chat-Bot):** Central Package Management / `Directory.Packages.props`; `.editorconfig` present; DI-heavy; source generators possible (implicit usings); single TFM **net10.0**; large test matrix.
- **Ledger seed:** **155** catalog rows (127+9+19); promotion scorecard seed **77** experimental rows (58 tools + 0 resources + 19 prompts).

### Phase 1 — Broad diagnostics scan

- **project_diagnostics** (full, offset 0 limit 30): totalErrors **0**, totalWarnings **23**, totalInfo **2282**; 23 analyzer warnings in page; `_meta.elapsedMs` **19502**.
- **project_diagnostics** (project=`ITChatBot.Chat`, severity=Warning): invariant totals; **1** warning in page; elapsed **11911** ms.
- **compile_check** (default): **0** CS diagnostics; elapsed **11774** ms.
- **compile_check** (`emitValidation=true`): **0** diagnostics; elapsed **16561** ms (emit path; restore done).
- **security_diagnostics:** **0** findings; SecurityCodeScan present.
- **security_analyzer_status:** NetAnalyzers + SecurityCodeScan present.
- **nuget_vulnerability_scan:** **0** CVEs, **34** projects, duration **10490** ms.
- **list_analyzers:** offset 0 limit 25 — totalRules **663**, hasMore true; second call project=`ITChatBot.Api` — paging/filter OK.
- **diagnostic_details** (CA1859 @ GitSyncProvider.cs:188): description + help link; SupportedFixes **[]** for this probe.
- **project_diagnostics** (severity=Error): totals **0/23/2282** invariant; error page empty as expected.
- **compile_check** (file=ChatOrchestrationPipeline.cs): **0** diagnostics; **30** ms.

## Appendix B. Retention

All working copies used to produce this report (catalog JSON, machine ledger, resource snapshots, draft notes, compile/finalize scripts) were **removed** after the final export. **This file is the only retained artifact** under `ai_docs/audit-reports/`.
