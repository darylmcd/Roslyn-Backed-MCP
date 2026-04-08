# MCP Server Audit Report

## Header
- **Date:** 2026-04-08 (UTC tool timestamps ~03:39–03:40Z)
- **Audited solution:** ITChatBot.sln — IT-Chat-Bot
- **Audited revision:** 8c2e3f6 (short)
- **Entrypoint loaded:** c:\Code-Repo\IT-Chat-Bot\ITChatBot.sln
- **Audit mode:** conservative — primary worktree; no disposable clone/worktree; partial phase coverage in one agent session
- **Isolation:** N/A (same as audited path); high-impact preview/apply families not run end-to-end
- **Client:** Cursor (MCP server id: user-roslyn); MCP **prompts** not invoked via prompts API
- **Workspace id:** 7e4b839b24784118b953b0c03a4676fa
- **Server:** roslyn-mcp 1.6.1+f16452924ef03cd3ed17dc36b53efab29c72217a; catalog 2026.03; tools 56 stable / 67 experimental; resources 7; prompts 16 experimental
- **Roslyn / .NET:** Roslyn 5.3.0.0; runtime .NET 10.0.5; OS Windows 10.0.26200
- **Scale:** 34 projects, 750 documents
- **Repo shape:** multi-project net10.0; test projects present; analyzers present (663 rules / 34 analyzer assemblies per `list_analyzers`); `dotnet restore` clean before semantic tools; no multi-targeting
- **Prior issue source:** ai_docs/backlog.md — no Roslyn MCP issue ids; Phase 18 MCP regression list N/A
- **Lock mode at audit time:** rw-lock (`ROSLYNMCP_WORKSPACE_RW_LOCK=true` in process env; `evaluate_csharp` returned `"true"`; workspace tool responses often `_meta.gateMode`: `rw-lock`)
- **Lock mode source of truth:** launch-config + behavioral fingerprint
- **Debug log channel:** no — MCP `notifications/message` not surfaced in this agent turn
- **Report path note:** Saved under IT-Chat-Bot per deep-review fallback. Copy to Roslyn-Backed-MCP `ai_docs/audit-reports/` if that repo owns rollups.
- **Auto-pair detection:** run 1; Session B matrix `skipped-pending-second-run`
- **Run scope:** Mandatory sections present; **not** an exhaustive execution of every phase step in deep-review-and-refactor.md (representative diagnostics, symbols, snippets, tests only).

## Coverage summary

| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-----------|-----------------|--------------|--------------------|----------------|---------|-------|
| tool | (all categories) | 19 | 0 | 0 | 0 | 104 | 0 | Partial session |
| resource | server + workspace | 4 | n/a | n/a | 0 | 3 | 0 | projects/diagnostics/source_file URI not fetched |
| prompt | prompts | 0 | n/a | n/a | 0 | 0 | 16 | Client limitation |

## Coverage ledger

| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| tool | `server_info` | stable | exercised | 0-8 | See Verified tools |
| tool | `workspace_load` | stable | exercised | 0-8 | See Verified tools |
| tool | `workspace_reload` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `workspace_close` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `workspace_list` | stable | exercised | 0-8 | See Verified tools |
| tool | `workspace_status` | stable | exercised | 0-8 | See Verified tools |
| tool | `project_graph` | stable | exercised | 0-8 | See Verified tools |
| tool | `source_generated_documents` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `get_source_text` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `symbol_search` | stable | exercised | 0-8 | See Verified tools |
| tool | `symbol_info` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `go_to_definition` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `find_references` | stable | exercised | 0-8 | See Verified tools |
| tool | `find_implementations` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `document_symbols` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `find_overrides` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `find_base_members` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `member_hierarchy` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `symbol_signature_help` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `symbol_relationships` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `find_references_bulk` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `find_property_writes` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `enclosing_symbol` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `goto_type_definition` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `get_completions` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `project_diagnostics` | stable | exercised | 0-8 | See Verified tools |
| tool | `diagnostic_details` | stable | exercised | 0-8 | See Verified tools |
| tool | `type_hierarchy` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `callers_callees` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `impact_analysis` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `find_type_mutations` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `find_type_usages` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `build_workspace` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `build_project` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `test_discover` | stable | exercised | 0-8 | See Verified tools |
| tool | `test_run` | stable | exercised | 0-8 | See Verified tools |
| tool | `test_related` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `test_related_files` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `rename_preview` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `rename_apply` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `organize_usings_preview` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `organize_usings_apply` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `format_document_preview` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `format_document_apply` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `code_fix_preview` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `code_fix_apply` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `find_unused_symbols` | experimental | exercised | 0-8 | See Verified tools |
| tool | `get_di_registrations` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `get_complexity_metrics` | experimental | exercised | 0-8 | See Verified tools |
| tool | `find_reflection_usages` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `get_namespace_dependencies` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `get_nuget_dependencies` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `semantic_search` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `apply_text_edit` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `apply_multi_file_edit` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `create_file_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `create_file_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `delete_file_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `delete_file_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `move_file_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `move_file_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `add_package_reference_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `remove_package_reference_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `add_project_reference_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `remove_project_reference_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `set_project_property_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `add_target_framework_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `remove_target_framework_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `set_conditional_property_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `add_central_package_version_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `remove_central_package_version_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `apply_project_mutation` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `scaffold_type_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `scaffold_type_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `scaffold_test_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `scaffold_test_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `remove_dead_code_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `remove_dead_code_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `move_type_to_project_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `extract_interface_cross_project_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `dependency_inversion_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `migrate_package_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `split_class_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `extract_and_wire_interface_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `apply_composite_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `test_coverage` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `get_syntax_tree` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `get_code_actions` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `preview_code_action` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `apply_code_action` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `security_diagnostics` | stable | exercised | 0-8 | See Verified tools |
| tool | `security_analyzer_status` | stable | exercised | 0-8 | See Verified tools |
| tool | `nuget_vulnerability_scan` | stable | exercised | 0-8 | See Verified tools |
| tool | `find_consumers` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `get_cohesion_metrics` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `find_shared_members` | stable | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `move_type_to_file_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `move_type_to_file_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `extract_interface_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `extract_interface_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `bulk_replace_type_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `bulk_replace_type_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `extract_type_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `extract_type_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `revert_last_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `analyze_data_flow` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `analyze_control_flow` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `compile_check` | stable | exercised | 0-8 | See Verified tools |
| tool | `list_analyzers` | stable | exercised | 0-8 | See Verified tools |
| tool | `fix_all_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `fix_all_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `get_operations` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `format_range_preview` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `format_range_apply` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `analyze_snippet` | stable | exercised | 0-8 | See Verified tools |
| tool | `evaluate_csharp` | experimental | exercised | 0-8 | See Verified tools |
| tool | `get_editorconfig_options` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `set_editorconfig_option` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `evaluate_msbuild_property` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `evaluate_msbuild_items` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `get_msbuild_properties` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `set_diagnostic_severity` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| tool | `add_pragma_suppression` | experimental | skipped-safety | - | Partial deep-review; not invoked this session; primary worktree conservative cut |
| resource | `server_catalog` | stable | exercised | 0,15 | fetch_mcp_resource |
| resource | `resource_templates` | stable | exercised | 0,15 | fetch_mcp_resource |
| resource | `workspaces` | stable | exercised | 0,15 | fetch_mcp_resource |
| resource | `workspace_status` | stable | exercised | 0,15 | fetch_mcp_resource |
| resource | `workspace_projects` | stable | skipped-safety | 15 | not fetched this session |
| resource | `workspace_diagnostics` | stable | skipped-safety | 15 | not fetched this session |
| resource | `source_file` | stable | skipped-safety | 15 | not fetched this session |
| prompt | `explain_error` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `suggest_refactoring` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `review_file` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `analyze_dependencies` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `debug_test_failure` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `refactor_and_validate` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `fix_all_diagnostics` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `guided_package_migration` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `guided_extract_interface` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `security_review` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `discover_capabilities` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `dead_code_audit` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `review_test_coverage` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `review_complexity` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `cohesion_analysis` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |
| prompt | `consumer_impact` | experimental | blocked | 16 | MCP prompts not invocable from this Cursor agent turn |

## Verified tools (working)
- `server_info` — surface counts match catalog Summary (56/67 tools, 7 resources, 16 prompts).
- `workspace_load` / `workspace_list` / `workspace_status` / `project_graph` — 34 projects, 750 docs, IsStale false.
- `project_diagnostics` — 0 errors, 143 warnings; paging + project/severity filters coherent (ITChatBot.Configuration + Warning: 2 warnings, hasMore false).
- `compile_check` — 0 CS diagnostics on paginated full-solution pass (~10 s); second pass with `emitValidation=true` ~4.8 s, same empty diagnostic page for returned window.
- `security_diagnostics` / `security_analyzer_status` — 0 security findings; SecurityCodeScan present.
- `nuget_vulnerability_scan` — 0 CVEs, 34 projects, ~10.6 s.
- `list_analyzers` — 34 assemblies, 663 rules; offset/limit paging (hasMore true on first page).
- `diagnostic_details` — CA1002 / CA1032 returned descriptions and help links; SupportedFixes empty (acceptable for those rules).
- `get_complexity_metrics` — minComplexity 15 surfaces known hot spots (Slack webhook, aggregation jobs, ChatEndpoints).
- `symbol_search` — resolves HandleQueryAsync / IChatOrchestrator; broad `Async` query returns large hit set.
- `find_references` — `IChatOrchestrator` yields 12 refs; caret placement matters (method-only cursor gave 1 ref).
- `find_unused_symbols` — 1 high-confidence unused symbol (EF snapshot) in ~1.1 s.
- `analyze_snippet` — expression valid; invalid assignment reports CS0029 (see issues).
- `evaluate_csharp` — `Enumerable.Range(1,10).Sum()` => 55; env read confirms RW lock flag `"true"`; ~142 ms / ~18 ms.
- `test_discover` — large structured test inventory.
- `test_run` — `ITChatBot.Configuration.Tests`: 14 passed, 0 failed, exit 0, ~5.9 s.
- Resources: `roslyn://server/catalog`, `roslyn://server/resource-templates`, `roslyn://workspaces`, `roslyn://workspace/{id}/status` — readable.
- `compile_check` — invalid `workspaceId` returns structured NotFound with actionable message (Phase 17 sample).

## Phase 6 refactor summary
- **N/A** — No apply-capable refactor chains executed; conservative partial audit on primary worktree (no 6a–6i applies).

## Concurrency mode matrix (Phase 8b)

**Single-mode lane (rw-lock).** Parallel fan-out, read/write interleave, lifecycle stress, and writer reclassification runs were **not** executed (partial session). Session B columns: **skipped-pending-second-run**.

### Sequential baseline (Session A, rw-lock)
| Slot | Tool | Inputs | Wall-clock (ms) | Notes |
|------|------|--------|-----------------|-------|
| R1 | `find_references` | IChatOrchestrator.cs L8 C22 | 5 | 12 refs |
| R2 | `project_diagnostics` | offset 0 limit 30 | 15130 | first page |
| R3 | `symbol_search` | query `Async` limit 100 | N/A | response without `_meta.heldMs`; large payload |
| R4 | `find_unused_symbols` | includePublic false | 1126 | |
| R5 | `get_complexity_metrics` | minComplexity 15 limit 15 | 88 | |

### Parallel fan-out / 8b.3 / 8b.4 / 8b.5
**N/A — partial run**

## Debug log capture
client did not surface MCP log notifications

## MCP server issues (bugs)
No hard failures in exercised calls. Observations:

| # | Title | Severity | Detail |
|---|-------|----------|--------|
| 1 | `analyze_snippet` CS0029 span | cosmetic | `int x = "hello";` reports StartLine 1, StartColumn 9, EndColumn 16. Deep-review text references literal-column behavior for v1.7+; server **1.6.1** — verify after upgrade. |
| 2 | `find_references` targeting | UX | Caret on method identifier vs type name changes reference count sharply; expected Roslyn resolution behavior — document for agents. |

**Negative test:** `compile_check` with bogus workspaceId → `KeyNotFoundException` message lists `workspace_list` remediation — **PASS** (actionable).

## Improvement suggestions
- Include `_meta` on array-returning tools (`symbol_search`) for performance auditing.
- Operators: deep-review prompt appendix mentions v1.7+ behaviors; live host 1.6.1 — align docs or server version notes.

## Performance observations
| Tool | Input scale | Lock mode | Wall-clock (ms) | Notes |
|------|-------------|-----------|-----------------|-------|
| `project_diagnostics` | first 30 rows, full sln | rw-lock | 15130 | |
| `compile_check` | limit 40 full sln | rw-lock | 10060 | |
| `nuget_vulnerability_scan` | 34 projects | rw-lock | 10624 | |
| `compile_check` | emitValidation limit 10 | rw-lock | 4811 | |
| `test_run` | Configuration.Tests | rw-lock | 5863 | |

## Known issue regression check
**N/A** — no Roslyn MCP backlog entries in ai_docs/backlog.md to re-test against this server build.

## Known issue cross-check
- Deep-review appendix “Last verified: 2026-04-06 … 123 tools” matches catalog enum; running server **1.6.1** vs narrative that references **v1.7+** features (e.g. `compile_check` pagination notes) — treat as version skew for operators, not a live bug.

---

**Report path note (Phase 8b.6 Case A):** This is the mode-1 partial audit. For a full dual-mode concurrency matrix, run `eng/flip-rw-lock.ps1` (or equivalent), restart the MCP host, and re-invoke deep-review per `ai_docs/prompts/deep-review-and-refactor.md`.
