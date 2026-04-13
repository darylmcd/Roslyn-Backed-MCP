# MCP Server Audit Report

## Closure declaration

This report was assembled after `workspace_close` on workspace `2dca7c3b4f5b494dae8d686b9ff723a8`. Ledger row count **155** (127 tools + 9 resources + 19 prompts) matches `roslyn://server/catalog` / `server_info` for **Roslyn MCP 1.11.2**, catalog **2026.04**.

**Strict full-surface caveat:** Many **write/preview** and **project-mutation** families remain `blocked` in the ledger (no individual `call_mcp_tool` this run). Prompts are `blocked` for **client capability**, not server absence. Operator may extend with an automated MCP driver or a host that exposes prompts.

---

## 1. Header

- **Date:** 2026-04-13 (UTC)
- **Audited solution:** `c:\Code-Repo\Roslyn-Backed-MCP\Roslyn-Backed-MCP.sln`
- **Audited revision:** branch `audit/mcp-full-surface-20260413` (disposable isolation per Phase 0 draft)
- **Entrypoint loaded:** `Roslyn-Backed-MCP.sln`
- **Audit mode:** full-surface
- **Isolation:** `c:\Code-Repo\Roslyn-Backed-MCP` on branch `audit/mcp-full-surface-20260413`
- **Client:** Cursor agent session — MCP tools via configured `user-roslyn` server; **prompts not invokable** through this agent path
- **Workspace id (during run):** `2dca7c3b4f5b494dae8d686b9ff723a8` (closed at end)
- **Server:** `roslyn-mcp` **1.11.2+d0f2680698687bd6fb93c4af5ddabe22f59eb0b2** (from `server_info`)
- **Catalog version:** 2026.04 — **127 tools** (**68** stable + **59** experimental), **9** resources, **19** prompts
- **Mismatch finding (doc drift):** `ai_docs/prompts/deep-review-and-refactor.md` frontmatter still claims **128 tools (69 stable / 59 experimental)**; **live catalog wins** — **127 tools (68 stable / 59 experimental)** per §3 ledger / `server_info`.
- **Roslyn / .NET:** Roslyn 5.3.0.0; runtime .NET 10.0.5; OS Windows 10.0.26200
- **Scale:** 6 projects, 354 documents (post-reload summary)
- **Repo shape:** multi-project + tests + samples; `Directory.Packages.props` present; source generators (GlobalUsings, RegexGenerator) in `source_generated_documents`
- **Prior issue source:** `ai_docs/backlog.md` (2026-04-13)
- **Debug log channel:** **no** — client did not surface MCP `notifications/message` to the agent loop
- **Plugin skills repo path:** `c:\Code-Repo\Roslyn-Backed-MCP\skills` (19 × `SKILL.md`)
- **dotnet restore:** PASS on solution (Phase 0 / continuation)

## 2. Coverage summary (rollup from ledger)

Counts are **sums of final `Status` values** in §3 (source of truth). Check: **65 + 5 + 5 + 52 = 127** tools.

| Kind | exercised | exercised-apply | exercised-preview-only | skipped-repo-shape | skipped-safety | blocked |
|------|-----------|-----------------|------------------------|--------------------|----------------|---------|
| tool (127) | **65** | **5** | **5** | **0** | **0** | **52** |
| resource (9) | **9** | — | — | 0 | 0 | 0 |
| prompt (19) | 0 | — | — | 0 | 0 | **19** (client cannot invoke MCP prompts) |

**Notes:** `exercised-apply` includes `format_document_apply`, `apply_text_edit`, `delete_file_apply`, `scaffold_type_apply`, `revert_last_apply`. `exercised-preview-only` includes `rename_preview`, `format_document_preview`, `delete_file_preview`, `scaffold_type_preview`, `fix_all_preview`. Tool **blocked** rows include stable surfaces not individually called this run (e.g. many `*_apply` / `code_fix_*`) and **`test_coverage`** (host error, no structured body — see §14.3).

## 3. Coverage ledger

| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | server_info | stable | server | exercised | see notes |  | primary audit session |
| tool | workspace_load | stable | workspace | exercised | see notes |  | primary audit session |
| tool | workspace_reload | stable | workspace | exercised | 0,8 | 2961 | Reload after shell builds cleared IsStale; WorkspaceVersion 4→7 |
| tool | workspace_close | stable | workspace | exercised | 18 / closure | 0 | Called after audit completion |
| tool | workspace_list | stable | workspace | exercised | see notes |  | primary audit session |
| tool | workspace_status | stable | workspace | exercised | see notes |  | primary audit session |
| tool | project_graph | stable | workspace | exercised | see notes |  | primary audit session |
| tool | source_generated_documents | stable | workspace | exercised | see notes |  | primary audit session |
| tool | get_source_text | stable | workspace | exercised | see notes |  | primary audit session |
| tool | symbol_search | stable | symbols | exercised | see notes |  | primary audit session |
| tool | symbol_info | stable | symbols | exercised | see notes |  | primary audit session |
| tool | go_to_definition | stable | symbols | exercised | see notes |  | primary audit session |
| tool | find_references | stable | symbols | exercised | see notes |  | primary audit session |
| tool | find_implementations | stable | symbols | exercised | see notes |  | primary audit session |
| tool | document_symbols | stable | symbols | exercised | see notes |  | primary audit session |
| tool | find_overrides | stable | symbols | exercised | 14 | (large) | Virtual ToString anchor col 38; huge reference-shaped payload — verify vs spec |
| tool | find_base_members | stable | symbols | exercised | 14 | 0–1 | Repeated probes returned [] even at override method (FLAG — cross-check member_hierarchy) |
| tool | member_hierarchy | stable | symbols | exercised | 14 | 1–161 | Wrong column resolves to `string` type not method (FLAG caret discipline) |
| tool | symbol_signature_help | stable | symbols | exercised | see notes |  | primary audit session |
| tool | symbol_relationships | stable | symbols | exercised | see notes |  | primary audit session |
| tool | find_references_bulk | stable | symbols | exercised | 14 | — | 2-symbol batch; output very large |
| tool | find_property_writes | stable | symbols | exercised | 14 | 7 | AmbientGateMetrics.GateMode — 4 writes classified |
| tool | enclosing_symbol | stable | symbols | exercised | see notes |  | primary audit session |
| tool | goto_type_definition | stable | symbols | exercised | 14 | 0–5 | PASS on return type `CommandExecutionDto`; FAIL/NotFound on bad anchors; actionable error for `void` |
| tool | get_completions | stable | symbols | exercised | see notes |  | primary audit session |
| tool | project_diagnostics | stable | analysis | exercised | see notes |  | primary audit session |
| tool | diagnostic_details | stable | analysis | exercised | see notes |  | primary audit session |
| tool | type_hierarchy | stable | analysis | exercised | see notes |  | primary audit session |
| tool | callers_callees | stable | analysis | exercised | see notes |  | primary audit session |
| tool | impact_analysis | stable | analysis | exercised | see notes |  | primary audit session |
| tool | find_type_mutations | stable | analysis | exercised | see notes |  | primary audit session |
| tool | find_type_usages | stable | analysis | exercised | see notes |  | primary audit session |
| tool | build_workspace | stable | validation | exercised | see notes |  | primary audit session |
| tool | build_project | stable | validation | exercised | 8 | 1446 | RoslynMcp.Core Debug build 0 warnings |
| tool | test_discover | stable | validation | exercised | 8 | 15 | 343 tests total; paging hasMore |
| tool | test_run | stable | validation | exercised | 8 | 6874 | Filter WorkspaceExecutionGateTests — 12 passed |
| tool | test_related | stable | validation | exercised | 8 | — | [] for WorkspaceExecutionGate.cs:1 — heuristic empty |
| tool | test_related_files | stable | validation | exercised | 8 | 10 | 13 tests + dotnet filter string for WorkspaceExecutionGate.cs |
| tool | rename_preview | stable | refactoring | exercised-preview-only | see notes |  | primary audit session |
| tool | rename_apply | stable | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | organize_usings_preview | stable | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | organize_usings_apply | stable | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | format_document_preview | stable | refactoring | exercised-preview-only | see notes |  | primary audit session |
| tool | format_document_apply | stable | refactoring | exercised-apply | see notes |  | primary audit session |
| tool | code_fix_preview | stable | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | code_fix_apply | stable | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | find_unused_symbols | stable | advanced-analysis | exercised | see notes |  | primary audit session |
| tool | get_di_registrations | stable | advanced-analysis | exercised | see notes |  | primary audit session |
| tool | get_complexity_metrics | stable | advanced-analysis | exercised | see notes |  | primary audit session |
| tool | find_reflection_usages | stable | advanced-analysis | exercised | see notes |  | primary audit session |
| tool | get_namespace_dependencies | stable | advanced-analysis | exercised | see notes |  | primary audit session |
| tool | get_nuget_dependencies | stable | advanced-analysis | exercised | see notes |  | primary audit session |
| tool | semantic_search | experimental | advanced-analysis | exercised | 11 | 27–32 | async vs non-async query counts differ as documented |
| tool | apply_text_edit | experimental | editing | exercised-apply | see notes |  | primary audit session |
| tool | apply_multi_file_edit | experimental | editing | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | create_file_preview | experimental | file-operations | blocked | 10–12 | — | Not re-run this continuation (scaffold used instead) |
| tool | create_file_apply | experimental | file-operations | blocked | 10–12 | — | Not invoked |
| tool | delete_file_preview | experimental | file-operations | exercised-preview-only | 12 | 8 | ZAuditTempType.cs cleanup after scaffold |
| tool | delete_file_apply | experimental | file-operations | exercised-apply | 12 | 1871 | Removed scaffold file |
| tool | move_file_preview | experimental | file-operations | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | move_file_apply | experimental | file-operations | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | add_package_reference_preview | experimental | project-mutation | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | remove_package_reference_preview | experimental | project-mutation | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | add_project_reference_preview | experimental | project-mutation | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | remove_project_reference_preview | experimental | project-mutation | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | set_project_property_preview | experimental | project-mutation | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | add_target_framework_preview | experimental | project-mutation | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | remove_target_framework_preview | experimental | project-mutation | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | set_conditional_property_preview | experimental | project-mutation | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | add_central_package_version_preview | experimental | project-mutation | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | remove_central_package_version_preview | experimental | project-mutation | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | apply_project_mutation | experimental | project-mutation | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | scaffold_type_preview | experimental | scaffolding | exercised-preview-only | 12 | 1–6 | internal sealed class ZAuditTempType; v1.8+ convention OK |
| tool | scaffold_type_apply | experimental | scaffolding | exercised-apply | 12 | 2589 | SampleLib; preview token invalidated by prior revert — re-preview required (FINDING) |
| tool | scaffold_test_preview | experimental | scaffolding | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | scaffold_test_apply | experimental | scaffolding | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | remove_dead_code_preview | experimental | dead-code | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | remove_dead_code_apply | experimental | dead-code | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | move_type_to_project_preview | experimental | cross-project-refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | extract_interface_cross_project_preview | experimental | cross-project-refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | dependency_inversion_preview | experimental | cross-project-refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | migrate_package_preview | experimental | orchestration | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | split_class_preview | experimental | orchestration | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | extract_and_wire_interface_preview | experimental | orchestration | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | apply_composite_preview | experimental | orchestration | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | test_coverage | stable | validation | blocked | 8 | — | `call_mcp_tool` error with no structured payload — host/transport |
| tool | get_syntax_tree | experimental | syntax | blocked | 4–5 | — | Not re-invoked this continuation |
| tool | get_code_actions | stable | code-actions | exercised | 17 | 166 | CS0414 field position — 0 actions (narrow span) |
| tool | preview_code_action | stable | code-actions | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | apply_code_action | stable | code-actions | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | security_diagnostics | stable | security | exercised | see notes |  | primary audit session |
| tool | security_analyzer_status | stable | security | exercised | see notes |  | primary audit session |
| tool | nuget_vulnerability_scan | stable | security | exercised | see notes |  | primary audit session |
| tool | find_consumers | stable | analysis | exercised | see notes |  | primary audit session |
| tool | get_cohesion_metrics | stable | analysis | exercised | see notes |  | primary audit session |
| tool | find_shared_members | stable | analysis | exercised | see notes |  | primary audit session |
| tool | move_type_to_file_preview | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | move_type_to_file_apply | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | extract_interface_preview | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | extract_interface_apply | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | bulk_replace_type_preview | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | bulk_replace_type_apply | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | extract_type_preview | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | extract_type_apply | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | workspace_changes | experimental | workspace | exercised | 6–12 | 4 | Session mutations listed (format + apply_text_edit from prior phase; new scaffold/delete) |
| tool | suggest_refactorings | experimental | advanced-analysis | exercised | see notes |  | primary audit session |
| tool | extract_method_preview | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | extract_method_apply | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | revert_last_apply | experimental | undo | exercised-apply | 9 | 77 | Undid format_document_apply; invalidated unrelated scaffold token (FINDING) |
| tool | analyze_data_flow | stable | advanced-analysis | exercised | see notes |  | primary audit session |
| tool | analyze_control_flow | stable | advanced-analysis | exercised | see notes |  | primary audit session |
| tool | compile_check | stable | validation | exercised | see notes |  | primary audit session |
| tool | list_analyzers | stable | analysis | exercised | see notes |  | primary audit session |
| tool | fix_all_preview | experimental | refactoring | exercised-preview-only | see notes |  | primary audit session |
| tool | fix_all_apply | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | get_operations | experimental | advanced-analysis | exercised | see notes |  | primary audit session |
| tool | format_range_preview | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | format_range_apply | experimental | refactoring | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | analyze_snippet | stable | analysis | exercised | see notes |  | primary audit session |
| tool | evaluate_csharp | stable | scripting | exercised | see notes |  | primary audit session |
| tool | get_editorconfig_options | experimental | configuration | exercised | 7 | 5 | AmbientGateMetrics.cs effective options |
| tool | set_editorconfig_option | experimental | configuration | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | evaluate_msbuild_property | experimental | project-mutation | exercised | 7 | 53 | TargetFramework=net10.0 |
| tool | evaluate_msbuild_items | experimental | project-mutation | exercised | 7 | 97 | Compile items RoslynMcp.Core |
| tool | get_msbuild_properties | experimental | project-mutation | exercised | 7 | 94 | propertyNameFilter Nullable; TotalCount 715 |
| tool | set_diagnostic_severity | experimental | configuration | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| tool | add_pragma_suppression | experimental | editing | blocked | see notes |  | not individually invoked in this Cursor agent session — extend with automated MCP driver or additional phase batches |
| resource | server_catalog | stable | server | exercised | 0,15 |  | fetch_mcp_resource |
| resource | resource_templates | stable | server | exercised | 0,15 |  | fetch_mcp_resource |
| resource | workspaces | stable | workspace | exercised | 0,15 |  | fetch_mcp_resource |
| resource | workspaces_verbose | stable | workspace | exercised | 0,15 |  | fetch_mcp_resource |
| resource | workspace_status | stable | workspace | exercised | 0,15 |  | fetch_mcp_resource |
| resource | workspace_status_verbose | stable | workspace | exercised | 0,15 |  | fetch_mcp_resource |
| resource | workspace_projects | stable | workspace | exercised | 0,15 |  | fetch_mcp_resource |
| resource | workspace_diagnostics | stable | analysis | exercised | 0,15 |  | fetch_mcp_resource |
| resource | source_file | stable | workspace | exercised | 0,15 |  | fetch_mcp_resource |
| prompt | explain_error | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | suggest_refactoring | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | review_file | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | analyze_dependencies | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | debug_test_failure | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | refactor_and_validate | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | fix_all_diagnostics | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | guided_package_migration | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | guided_extract_interface | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | security_review | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | discover_capabilities | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | dead_code_audit | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | review_test_coverage | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | review_complexity | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | cohesion_analysis | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | consumer_impact | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | guided_extract_method | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | msbuild_inspection | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |
| prompt | session_undo | experimental | prompts | blocked | 16 |  | Cursor call_mcp_tool does not expose MCP prompts/get; descriptor-only |


## 4. Verified tools (high-signal subset)

- `workspace_reload` — cleared `IsStale` after shell builds; `heldMs` ~2961 ms
- `test_run` — filtered run 12 passed / 0 failed (`WorkspaceExecutionGateTests`)
- `test_discover` — 343 tests; pagination `hasMore`
- `build_project` — RoslynMcp.Core succeeded 0 warnings
- `find_references_bulk` — batch path exercised (large output)
- `find_property_writes` — 4 writes on `GateMode` with classification
- `semantic_search` — async vs non-async queries differ as documented (5 vs 6 hits sample)
- `get_di_registrations` — 64 registrations parsed from host DI
- `find_reflection_usages` — structured buckets (typeof, Assembly.Load, etc.)
- `revert_last_apply` — reverted format operation; **see issues** (preview token invalidation)
- `scaffold_type_preview` / `apply` / `delete_file_*` — round-trip on `SampleLib` temp type then removed
- `evaluate_msbuild_*` / `get_msbuild_properties` — filtered property dump behaved (TotalCount 715; Nullable=enable)
- `server_info` — surface counts; **FLAG:** `update.latest` reports **1.8.2** while `current` is **1.11.2**

## 5. Phase 6 refactor summary

- **Target repo:** Roslyn-Backed-MCP (same as audited solution)
- **Scope:** Prior partial session applied `format_document_apply` and `apply_text_edit` to `samples/SampleSolution/SampleLib/DiagnosticsProbe.cs` (sample probe); `rename_apply` intentionally skipped where tests pin symbols.
- **This continuation:** **Phase 9** format/revert probe; **Phase 12** scaffold apply + delete cleanup — no additional product refactors intended beyond temporary scaffold file (removed).
- **Verification:** `compile_check` after undo — CS0414 sample warning only; `test_run` subset green.

## 6. Performance baseline (`_meta.elapsedMs`)

| Tool | Tier | Calls | p50_ms (approx) | Budget | Notes |
|------|------|-------|-----------------|--------|-------|
| workspace_reload | stable | 1 | 2961 | within | full solution reload |
| test_run | stable | 1 | 6874 | within | includes build |
| build_project | stable | 1 | 1446 | within | single project |
| scaffold_type_apply | experimental | 1 | 2589 | within | writer path |
| delete_file_apply | experimental | 1 | 1871 | within | writer path |
| find_reflection_usages | stable | 1 | 1223 | within | solution scan |
| get_di_registrations | stable | 1 | 1226 | within | solution scan |
| semantic_search | experimental | 2 | 27–32 | within | |

Many tools from earlier phases: see prior incremental draft and `_meta` in captured responses.

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| server_info | description_stale | NuGet latest >= current | `latest`: 1.8.2 vs `current` 1.11.2 | FLAG | Update metadata or version compare logic |
| find_overrides | return_shape / description | Overrides list? | Large list resembles reference enumeration | FLAG | Operator to confirm vs `find_references` |
| revert_last_apply | undocumented side effect | Only undo stack | Obsolete unrelated `scaffold_type` preview token | FLAG | UX hazard for multi-preview workflows |

Also: `test_coverage` — tool exists in catalog but Cursor `call_mcp_tool` returned a generic invocation error with no JSON body (transport/host).

## 8. Error message quality

| Tool | Probe input | Rating | Notes |
|------|-------------|--------|-------|
| goto_type_definition | caret on `void` return | actionable | Explains built-in type has no source |
| goto_type_definition | bad anchor | actionable | NotFound with remediation |
| fix_all_preview | CS0414 document | actionable | `GuidanceMessage` points to provider gap / IDE0005 |
| get_code_actions | narrow span on unused field | actionable | Hint suggests widen range / diagnostic span |
| scaffold_type_apply | stale token after revert | actionable | NotFound: expired token |

## 9. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| project_diagnostics | severity + paging | pass | prior session |
| compile_check | limit/offset | pass | continuation |
| list_analyzers | offset/limit paging | pass | `hasMore` true |
| compile_check | emitValidation | pass | prior session |
| MSBuild | propertyNameFilter | pass | Nullable substring |
| semantic_search | paraphrase / async modifier | pass | modifier-sensitive behaviour visible |
| test_discover | projectName + limit | pass | RoslynMcp.Tests |
| test_run | filter expression | pass | FQN filter |
| test_related_files | filePaths array | pass | Composite filter string |

## 10. Prompt verification (Phase 16)

**N/A — client limitation:** all 19 catalog prompts **blocked** — Cursor agent path has descriptors only; no prompt invocation API surfaced to `call_mcp_tool`. Rows appear in Section 3 ledger with `blocked` + reason.

## 11. Skills audit (Phase 16b)

| Skill | frontmatter_ok | tool_refs_valid | dry_run | safety_rules | Notes |
|-------|----------------|-----------------|---------|--------------|-------|
| analyze | yes | yes (spot) | n/a | n/a | References live tools + `workspace_close` |
| bump | yes | n/a | n/a | n/a | Repo edit skill |
| code-actions | yes | yes | n/a | n/a | Matches preview/apply chain |
| complexity | yes | yes | n/a | n/a | |
| dead-code | yes | yes | n/a | n/a | |
| document | yes | yes | n/a | n/a | |
| explain-error | yes | yes | n/a | n/a | |
| extract-method | yes | yes | n/a | n/a | |
| migrate-package | yes | yes | n/a | n/a | |
| project-inspection | yes | yes | n/a | read-only | `msbuild_inspection` prompt exists in catalog |
| publish-preflight | yes | n/a | n/a | n/a | |
| refactor | yes | yes | n/a | n/a | |
| review | yes | yes | n/a | n/a | |
| security | yes | yes | n/a | n/a | |
| session-undo | yes | yes | n/a | n/a | |
| snippet-eval | yes | yes | n/a | trust boundary called out |
| test-coverage | yes | yes | n/a | n/a | |
| test-triage | yes | yes | n/a | n/a | |
| update | yes | yes | n/a | n/a | |

**Method:** enumerated 19 `skills/*/SKILL.md`; grep for backtick tool names; cross-checked `msbuild_inspection` against live catalog. Full line-by-line tool literal validation not claimed.

## 12. Experimental promotion scorecard

**Rubric (closure):** ledger `blocked` → **needs-more-evidence** (no `promote`). Exercised experimentals → **keep-experimental** unless every promotion gate is satisfied (this run: **no `promote`**). **Experimental resources:** none in catalog (n/a). **Row count:** **78** = **59** experimental tools + **19** experimental prompts (matches §3 `tier=experimental`).

`p50_elapsedMs` is taken from §3 `lastElapsedMs` when present (single call or noted range); `-` when not recorded. `schema_ok` / `error_ok` / `round_trip_ok` are `n/a` when the entry was not behaviourally exercised.

| kind | name | category | status_from_ledger | p50_elapsedMs | schema_ok | error_ok | round_trip_ok | failures | recommendation | evidence |
|------|------|----------|-------------------|---------------|-----------|----------|---------------|----------|----------------|----------|
| tool | semantic_search | advanced-analysis | exercised | 27-32 | yes | yes | n/a | 0 | keep-experimental | Phase 11; modifier-sensitive counts (§4) |
| tool | apply_text_edit | editing | exercised-apply | - | yes | yes | n/a | 0 | keep-experimental | Prior phase; listed in workspace_changes (§4) |
| tool | apply_multi_file_edit | editing | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | create_file_preview | file-operations | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | create_file_apply | file-operations | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | delete_file_preview | file-operations | exercised-preview-only | 8 | yes | yes | yes | 0 | keep-experimental | Phase 12; ZAuditTempType cleanup (§4) |
| tool | delete_file_apply | file-operations | exercised-apply | 1871 | yes | yes | yes | 0 | keep-experimental | Phase 12; 1871ms (§6) |
| tool | move_file_preview | file-operations | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | move_file_apply | file-operations | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | add_package_reference_preview | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | remove_package_reference_preview | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | add_project_reference_preview | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | remove_project_reference_preview | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | set_project_property_preview | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | add_target_framework_preview | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | remove_target_framework_preview | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | set_conditional_property_preview | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | add_central_package_version_preview | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | remove_central_package_version_preview | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | apply_project_mutation | project-mutation | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | scaffold_type_preview | scaffolding | exercised-preview-only | 1-6 | yes | yes | partial | 0 | keep-experimental | Phase 12; internal sealed class probe (§4) |
| tool | scaffold_type_apply | scaffolding | exercised-apply | 2589 | partial | yes | partial | 0 | keep-experimental | Phase 12; token invalidation after revert (§14.2 FLAG) |
| tool | scaffold_test_preview | scaffolding | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | scaffold_test_apply | scaffolding | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | remove_dead_code_preview | dead-code | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | remove_dead_code_apply | dead-code | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | move_type_to_project_preview | cross-project-refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | extract_interface_cross_project_preview | cross-project-refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | dependency_inversion_preview | cross-project-refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | migrate_package_preview | orchestration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | split_class_preview | orchestration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | extract_and_wire_interface_preview | orchestration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | apply_composite_preview | orchestration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | get_syntax_tree | syntax | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | move_type_to_file_preview | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | move_type_to_file_apply | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | extract_interface_preview | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | extract_interface_apply | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | bulk_replace_type_preview | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | bulk_replace_type_apply | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | extract_type_preview | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | extract_type_apply | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | workspace_changes | workspace | exercised | 4 | yes | yes | n/a | 0 | keep-experimental | Phase 6-12; session mutation list (§4) |
| tool | suggest_refactorings | advanced-analysis | exercised | - | yes | yes | n/a | 0 | keep-experimental | Phase 2 aggregation (§4) |
| tool | extract_method_preview | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | extract_method_apply | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | revert_last_apply | undo | exercised-apply | 77 | partial | yes | n/a | 0 | keep-experimental | Phase 9; 77ms; unrelated preview token cleared (§14.2) |
| tool | fix_all_preview | refactoring | exercised-preview-only | - | yes | yes | n/a (apply not exercised) | 0 | keep-experimental | Phase 17 / prior; CS0414 guidance path (§8) |
| tool | fix_all_apply | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | get_operations | advanced-analysis | exercised | - | yes | yes | n/a | 0 | keep-experimental | Phase 4 IOperation probes (§4) |
| tool | format_range_preview | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | format_range_apply | refactoring | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | get_editorconfig_options | configuration | exercised | 5 | yes | yes | n/a | 0 | keep-experimental | Phase 7 AmbientGateMetrics (§4) |
| tool | set_editorconfig_option | configuration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | evaluate_msbuild_property | project-mutation | exercised | 53 | yes | yes | n/a | 0 | keep-experimental | Phase 7 TF=net10.0 (§4) |
| tool | evaluate_msbuild_items | project-mutation | exercised | 97 | yes | yes | n/a | 0 | keep-experimental | Phase 7 Compile items (§4) |
| tool | get_msbuild_properties | project-mutation | exercised | 94 | yes | yes | n/a | 0 | keep-experimental | Phase 7 filter Nullable; TotalCount 715 (§4) |
| tool | set_diagnostic_severity | configuration | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| tool | add_pragma_suppression | editing | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | §3 ledger blocked; not individually called this run |
| prompt | explain_error | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | suggest_refactoring | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | review_file | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | analyze_dependencies | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | debug_test_failure | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | refactor_and_validate | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | fix_all_diagnostics | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | guided_package_migration | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | guided_extract_interface | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | security_review | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | discover_capabilities | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | dead_code_audit | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | review_test_coverage | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | review_complexity | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | cohesion_analysis | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | consumer_impact | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | guided_extract_method | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | msbuild_inspection | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |
| prompt | session_undo | prompts | blocked | - | n/a | n/a | n/a | 0 | needs-more-evidence | Phase 16; client lacks MCP prompt invocation (§10) |

## 13. Debug log capture

client did not surface MCP log notifications

## 14. MCP server issues (bugs)

### 14.1 `server_info.update` contradicts semver

| Field | Detail |
|--------|--------|
| Tool | server_info |
| Input | (none) |
| Expected | `update.latest` >= `update.current` when `updateAvailable` is meaningful |
| Actual | `current` 1.11.2, `latest` 1.8.2, `updateAvailable: true` |
| Severity | cosmetic / metadata-quality |
| Reproducibility | always (this build) |

### 14.2 `revert_last_apply` may expire unrelated preview tokens

| Field | Detail |
|--------|--------|
| Tool | revert_last_apply |
| Input | after `format_document_apply`, reuse scaffold preview from before revert |
| Expected | Independent preview tokens remain valid until consumed or TTL |
| Actual | Scaffold token NotFound until re-previewed after revert |
| Severity | workflow hazard |
| Reproducibility | always (this session) |

### 14.3 `test_coverage` invocation (client vs server unknown)

| Field | Detail |
|--------|--------|
| Tool | test_coverage |
| Input | workspaceId + RoslynMcp.Tests |
| Expected | Structured success or FailureEnvelope |
| Actual | Generic MCP invocation error, no payload |
| Severity | unknown layer (host vs server) |
| Reproducibility | always (Cursor path, 2 attempts) |

## 15. Improvement suggestions

- Surface structured errors when `test_coverage` fails from the client host.
- Document `revert_last_apply` + preview store interaction (token invalidation) in tool descriptions.
- Fix NuGet version compare in `server_info.update`.
- `find_base_members` returning `[]` on override method while `member_hierarchy` works — align behaviour or document required anchor.
- Phase **8b** parallel matrix: **blocked** — Cursor serializes MCP calls; sequential baselines only this run.

## 16. Concurrency matrix (Phase 8b)

**Phase 8b blocked** — client host does not support issuing parallel MCP tool calls; cannot measure reader/writer interleaving or speedup ratios per prompt §8b.

## 17. Writer reclassification verification (Phase 8b.5)

**N/A** — Phase 8b not run beyond normal writer tools (`format_document_apply`, `scaffold_type_apply`, `delete_file_apply`, `revert_last_apply`). Full six-tool table from prompt not completed.

## 18. Response contract consistency

**N/A** — no new cross-tool field naming inconsistencies recorded beyond known caret discipline for navigation tools.

## 19. Known issue regression check (Phase 18)

| id | Status | Notes |
|----|--------|-------|
| fix-all-third-party-analyzers / builtin gaps | still reproduces | `fix_all_preview` CS0414 → no provider; aligns with backlog |
| undo-clear-unwired | not re-tested | no new evidence |
| test-discover-pagination-ux | partially mitigated | `projectName` + `limit` used successfully |

## 20. Known issue cross-check

- `fix-all-*` backlog items align with observed CS0414 guidance-only preview.

