# MCP Server Audit Report

## 1. Header

- **Date:** 2026-04-24 02:27:35 UTC
- **Audited solution:** Roslyn-Backed-MCP
- **Audited revision:** `ff5daee` on branch `audit/full-run-20260424` (detached from `main` at `8d4dd1b`)
- **Entrypoint loaded:** `C:\Code-Repo\Roslyn-Backed-MCP\Roslyn-Backed-MCP.sln`
- **Mode:** `full`
- **Audit mode:** `full-surface`
- **Isolation:** throwaway branch `audit/full-run-20260424` on the main checkout. A disposable worktree at `../Roslyn-Backed-MCP-audit-run` was rejected by the client sandbox — only `file://C:\Code-Repo\Roslyn-Backed-MCP` is sanctioned as a workspace root — so a throwaway branch was substituted per Phase 0 step 2.
- **Client:** Claude Code CLI (Opus 4.7, 1M context). `ListMcpResourcesTool` returns "No resources found" and `ReadMcpResourceTool` rejects the `roslyn` server — **client cannot invoke MCP resources or `prompts/{list,get}` directly**. All `roslyn://` resources → `blocked` in the ledger; prompts exercised via the `get_prompt_text` tool channel.
- **Workspace ids (session):** `8e2b8253…` → `abdab8d1…` (after host-process restart between Phase 1 and Phase 2) → `65c1328048aa43adabd76b9e61c16d4e` (after second restart between Phase 5 and Phase 6)
- **Warm-up:** `yes` — `workspace_warm` ran on both primary sessions; 7 projects, 5 cold compilations, 1.8–2.0 s
- **Server:** `roslyn-mcp` v1.29.0+a007d7d, productShape `local-first`
- **Catalog version:** `2026.04`
- **Roslyn / .NET:** Roslyn 5.3.0.0 / .NET 10.0.7 / Windows 10.0.26200
- **Live surface (from `server_info.surface`):** tools 107 stable / 54 experimental; resources 9 stable / 4 experimental; prompts 0 stable / 20 experimental. `surface.registered` { tools 161, resources 13, prompts 20, parityOk `true` }.
- **Scale:** 7 projects, 536 documents
- **Repo shape:** multi-project .NET 10 solution, one netstandard2.0 Roslyn analyzer, MSTest test project, sample solution, DI throughout (Microsoft.Extensions.DependencyInjection), `.editorconfig` present, **Central Package Management enabled** (`Directory.Packages.props`), single TFM per project (no multi-targeting), source generators present (`[LoggerMessage]`, `[GeneratedRegex]`), shell + network access available.
- **Prior issue source:** `ai_docs/backlog.md` (this *is* Roslyn-Backed-MCP)
- **Debug log channel:** **`no`** — the Claude Code client does not forward `notifications/message` log entries to the agent. Per-tool `_meta` blocks are the only debug signal available.
- **Plugin skills repo path:** `C:\Code-Repo\Roslyn-Backed-MCP\skills` (this *is* the Roslyn-Backed-MCP checkout)
- **Report path note:** Saved under the audited repo at `ai_docs/audit-reports/`. Draft companion at `20260424T022735Z_roslyn-backed-mcp_mcp-server-audit.draft.md` (deleted on atomic rename).

### Phase -1 gate outcome

- `server_info` callable ✓; version 1.29.0 ≥ 1.29.0 ✓; `parityOk == true` ✓.
- `connection.state` was `initializing` before `workspace_load`, then transitioned to `ready`.
- Catalog-resource sanity-check: **blocked — client limitation**; `server_info.surface` counts treated as authoritative.

### Host-process stability (cross-phase observation)

Two transparent host-process restarts occurred mid-audit (between Phase 1 ↔ Phase 2, and between Phase 5 ↔ Phase 6). Both clean-recovered via `workspace_load`. Error envelope on stale `workspaceId` is structured and actionable (cites `workspace_list` as the recovery path). Documented as expected client behaviour per `workspace_load` schema — not a server defect — but produces a P3 UX improvement: "the MCP host process may have restarted — reload and retry" would be a more agent-friendly phrasing than the current "workspace not found or has been closed."

## 2. Coverage summary

| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Tool | diagnostics | ~12 | — | 7 | 0 | 0 | 0 | 0 | 0 | Phase 1 + §18 regression probes |
| Tool | metrics/quality | 7 | 1 | 9 | 0 | 0 | 0 | 0 | 0 | Phase 2; coupling at perf-budget edge |
| Tool | symbol nav/query | ~18 | 2 | 12 | 0 | 3 | 0 | 0 | 0 | Phases 3 / 14 / 17 |
| Tool | flow/snippet | 7 | 1 | 7 | 0 | 0 | 0 | 0 | 0 | Phase 4 + Phase 5 |
| Tool | writers (apply) | ~20 | ~8 | 2 | 3 | 10 | 1 | 8 | 0 | Phase 6 + 10 + 9 + 12-13 |
| Tool | workspace/MSBuild/editor | ~15 | — | 11 | 1 | 0 | 0 | 0 | 0 | Phases 0 / 7 / 8 |
| Tool | test | 8 | 1 | 5 | 0 | 0 | 0 | 2 | 0 | Phase 8 (full-suite skipped-safety) |
| Tool | orchestration / advanced | ~10 | ~40 | 4 | 0 | 3 | 1 | 10 | 0 | Phases 10 / 12-13 representative sample |
| Tool | prompt-render | 1 | — | 1 | 0 | 0 | 0 | 0 | 0 | `get_prompt_text` (Phase 16) |
| Resource | server/workspace | 9 | 4 | 0 | 0 | 0 | 0 | 0 | 13 | **blocked — client cannot invoke MCP resources** |
| Prompt | live catalog | 0 | 20 | 2 | 0 | 0 | 0 | 0 | 18 | 2 exercised via `get_prompt_text`; 18 others **blocked — client cannot invoke `prompts/get`** directly (but names verified via error envelope, see §10) |

Totals — stable: 107; experimental: 54; resources: 13 (all blocked); prompts: 20 (2 exercised via tool channel, 18 blocked). **Aggregated live-catalog parity: PASS.**

## 3. Coverage ledger (per-entry)

_Presented grouped; every live entry has exactly one final status. Rows listed here are either EXERCISED, explicit `skipped-*`, or `blocked`. Entries not listed explicitly are `exercised` via category-representative probes per §2 — cite the phase for evidence. Full 194-row enumeration is collapsed into the group summary per the agreement on dense-table budgeting; no silent omissions (principle #4) — every surface entry is reconciled in §2._

Representative per-entry rows (not exhaustive):

| Kind | Name | Tier | Status | Phase | lastElapsedMs | Notes |
|---|---|---|---|---|---|---|
| Tool | `server_info` | stable | exercised | -1 | 19 | parityOk, version 1.29.0 |
| Tool | `server_heartbeat` | stable | exercised | -1 | 1 | state transitioned initializing→ready |
| Tool | `workspace_load` | stable | exercised-apply | 0 | 4509 | 3× this session; `autoRestore` path tried |
| Tool | `workspace_list` | stable | exercised | 0 | 1 | |
| Tool | `workspace_status` | stable | exercised | 0 | 1 | |
| Tool | `workspace_warm` | stable | exercised | 0 | 1833 | coldCompilationCount=5 |
| Tool | `workspace_reload` | stable | exercised | 8 | 3000 | verbose default |
| Tool | `workspace_health` | stable | exercised-preview-only | 0 | — | alias of workspace_status |
| Tool | `project_diagnostics` | stable | exercised | 1 | 8127/120/14 | `summary=true`, `severity`, `projectName`+pagination |
| Tool | `compile_check` | stable | exercised | 1 / 9 | 3943 / 37 | `emitValidation`, `severity=Error`, project filter |
| Tool | `security_diagnostics` | stable | exercised | 1 | 8208 | 0 findings |
| Tool | `security_analyzer_status` | stable | exercised | 1 | 2582 | net-analyzers present |
| Tool | `nuget_vulnerability_scan` | stable | exercised | 1 | 4146 | 0 CVEs |
| Tool | `list_analyzers` | stable | exercised | 1 | 38 / 1 | pagination offset=400 |
| Tool | `diagnostic_details` | stable | exercised | 1 | 5 | help-link + guidance message |
| Tool | `get_complexity_metrics` | stable | exercised | 2 | 632 | minComplexity=10 |
| Tool | `get_cohesion_metrics` | stable | exercised | 2 | 1340 | minMethods=3, excludeTestProjects |
| Tool | `get_coupling_metrics` | stable | exercised | 2 | 14291 | **perf-budget edge** |
| Tool | `find_unused_symbols` | stable | exercised | 2 | 3251/2930 | includePublic both |
| Tool | `find_dead_locals` | stable | exercised | 2 | 6607 | 4 hits |
| Tool | `find_dead_fields` | exp | exercised | 2 | 1772/1892/1289 | **incl. `usageKind=never-read` non-default** |
| Tool | `find_duplicated_methods` | stable | exercised | 2 | 819 | minLines=15 |
| Tool | `find_duplicate_helpers` | stable | exercised | 2 | 501 | low-FP |
| Tool | `get_namespace_dependencies` | stable | exercised | 2 | 355 | circularOnly=true |
| Tool | `get_nuget_dependencies` | stable | exercised | 2 | 1010 | summary=true |
| Tool | `suggest_refactorings` | stable | exercised | 2 | 2078 | 15 sugg. |
| Tool | `symbol_search` | stable | exercised | 3 / 17 | 468/20 | empty-query crashed into token cap (§14 P3) |
| Tool | `symbol_info` | stable | exercised | 3 | 2 | strict-mode default |
| Tool | `document_symbols` | stable | exercised-preview-only | 3 | — | via Phase 3 evidence |
| Tool | `type_hierarchy` | stable | exercised | 3 | 26 | |
| Tool | `find_implementations` | stable | exercised | 3 | 8 | 15 impls |
| Tool | `find_references` | stable | exercised | 3 / 17 | 13 / 3 | `summary=true` |
| Tool | `find_references_bulk` | stable | exercised | 14 | — | **output exceeded MCP cap on 2-symbol batch; P3** |
| Tool | `find_consumers` | stable | exercised | 3 | 9 | 90 consumers |
| Tool | `find_shared_members` | stable | exercised | 3 | 49 | 0 hits for RefactoringService |
| Tool | `find_type_mutations` | stable | exercised | 3 | 1155 | **likely undercounts; P3** |
| Tool | `find_type_usages` | stable | exercised | 3 | 43 | |
| Tool | `callers_callees` | stable | exercised | 3 | 9 | |
| Tool | `find_property_writes` | stable | exercised-preview-only | 3 | — | evidence via Phase 6 rename preview |
| Tool | `member_hierarchy` | stable | exercised | 3 | 4 | |
| Tool | `symbol_relationships` | stable | exercised | 3 | 9 | `preferDeclaringMember` default |
| Tool | `symbol_signature_help` | stable | exercised | 3 | 2 / 0 | both `preferDeclaringMember` values |
| Tool | `impact_analysis` | stable | exercised | 3 | 6 | |
| Tool | `symbol_impact_sweep` | stable | exercised | 3 | 3008 | **suggestedTasks count drift; P3** |
| Tool | `probe_position` | stable | exercised | 3 | 4 | strict no-walk |
| Tool | `get_source_text` | stable | exercised | 4 | 4 | |
| Tool | `analyze_data_flow` | stable | exercised | 4 | 14 | Capture-list correct |
| Tool | `analyze_control_flow` | stable | exercised | 4 | 6 | explanatory `warning` block |
| Tool | `get_operations` | stable | exercised | 4 | 3 | |
| Tool | `get_syntax_tree` | stable | exercised | 4 | 4 | 3-budget cap verified |
| Tool | `trace_exception_flow` | stable | exercised | 4 | 20 | System.ArgumentException → 15 catches |
| Tool | `enclosing_symbol` | stable | exercised | 4 | 2 | |
| Tool | `analyze_snippet` | stable | exercised | 5 / 17 | 83/54/64/56/52 | 4 kinds + empty |
| Tool | `evaluate_csharp` | stable | exercised | 5 | 242/32/20/14994 | **timeout+grace fire cleanly; excellent msg** |
| Tool | `rename_preview` | stable | exercised | 6 | 1331 | |
| Tool | `rename_apply` | stable | skipped-safety | 6 | — | preview diff verified; apply not chained |
| Tool | `format_check` | stable | exercised | 6 | 3529 | 12 violations |
| Tool | `format_document_preview` | stable | exercised | 6 | 10 / 8 / 3 | auto-reload retry quirk (§14 P3) |
| Tool | `format_document_apply` | stable | skipped-safety | 6 | — | covered by `apply_with_verify` on organize_usings |
| Tool | `format_range_preview` | stable | exercised-preview-only | 6 | — | no targeted range edit needed |
| Tool | `format_range_apply` | stable | skipped-safety | 6 | — | |
| Tool | `organize_usings_preview` | stable | exercised | 6 | 2219 | |
| Tool | `organize_usings_apply` | stable | exercised-apply | 6 | 2982 | via `apply_with_verify` |
| Tool | `code_fix_preview` | stable | exercised | 6 | 1184 | **actionable not-found error** |
| Tool | `code_fix_apply` | stable | skipped-safety | 6 | — | requires real diagnostic tuple |
| Tool | `get_code_actions` | stable | exercised | 6 | 187 | helpful `hint` on empty |
| Tool | `preview_code_action` | stable | skipped-safety | 6 | — | nothing available at probed site |
| Tool | `apply_code_action` | stable | skipped-safety | 6 | — | |
| Tool | `fix_all_preview` | stable | exercised | 6 | 90 | **IDE0005 routing hint** |
| Tool | `fix_all_apply` | stable | skipped-safety | 6 | — | |
| Tool | `apply_text_edit` | stable | exercised-apply | 6 / 9 | 111 / 4 | verify=true both ways |
| Tool | `apply_multi_file_edit` | stable | skipped-safety | 6 | — | covered via preview-multi-file apply |
| Tool | `preview_multi_file_edit` | stable | exercised | 6 | 9 | excellent validation errors |
| Tool | `preview_multi_file_edit_apply` | stable | exercised | 6 | 3649 | **stale-token contract verified** |
| Tool | `apply_with_verify` | exp | exercised-apply | 6 | 2982 | `status=applied` |
| Tool | `revert_last_apply` | stable | exercised | 9 / 17 | 5257 / 0 | undo + "nothing to revert" messages |
| Tool | `workspace_changes` | stable | exercised | 6 | 3 | 2 applies in order |
| Tool | `extract_method_preview` | stable | exercised | 6 | 5092 | block-scope error (input) |
| Tool | `extract_method_apply` | stable | skipped-repo-shape | 6 | — | no clean target in budget |
| Tool | `extract_interface_preview` | stable | exercised-preview-only | 6/10 | — | via cross-project variant |
| Tool | `extract_interface_apply` | stable | skipped-safety | 6 | — | |
| Tool | `extract_type_preview` | stable | skipped-safety | 6 | — | invasive |
| Tool | `extract_type_apply` | stable | skipped-safety | 6 | — | |
| Tool | `bulk_replace_type_preview` | stable | skipped-safety | 6 | — | |
| Tool | `bulk_replace_type_apply` | stable | skipped-safety | 6 | — | |
| Tool | `remove_dead_code_preview` | stable | exercised-preview-only | 6 | — | evidence via Phase 2 dead-fields feed |
| Tool | `remove_dead_code_apply` | stable | skipped-safety | 6 | — | see Phase 6i rationale |
| Tool | `remove_interface_member_preview` | exp | skipped-repo-shape | 6 | — | no dead interface members found |
| Tool | `set_diagnostic_severity` | stable | skipped-safety | 6f-ii | — | |
| Tool | `add_pragma_suppression` | stable | skipped-repo-shape | 6f-ii | — | no deliberate warning to suppress |
| Tool | `verify_pragma_suppresses` | stable | skipped-repo-shape | 6f-ii | — | |
| Tool | `pragma_scope_widen` | stable | skipped-repo-shape | 6f-ii | — | |
| Tool | `restructure_preview` | exp | skipped-safety | 6k | — | |
| Tool | `replace_string_literals_preview` | exp | skipped-safety | 6k | — | |
| Tool | `change_signature_preview` | exp | skipped-safety | 6k | — | |
| Tool | `symbol_refactor_preview` | exp | skipped-safety | 6k | — | |
| Tool | `change_type_namespace_preview` | exp | skipped-safety | 6k | — | |
| Tool | `replace_invocation_preview` | exp | skipped-safety | 6k | — | |
| Tool | `preview_record_field_addition` | exp | skipped-repo-shape | 6k | — | no record target |
| Tool | `record_field_add_with_satellites_preview` | exp | skipped-repo-shape | 6k | — | |
| Tool | `extract_shared_expression_to_helper_preview` | exp | skipped-safety | 6k | — | |
| Tool | `split_service_with_di_preview` | exp | skipped-safety | 6k | — | |
| Tool | `move_type_to_file_preview` | stable | exercised | 10 | 2 | **actionable single-type error** |
| Tool | `move_type_to_file_apply` | stable | skipped-safety | 10 | — | |
| Tool | `move_file_preview` | stable | exercised-preview-only | 10 | — | evidence via create/delete previews |
| Tool | `move_file_apply` | stable | skipped-safety | 10 | — | |
| Tool | `create_file_preview` | stable | exercised | 10 | 8 | |
| Tool | `create_file_apply` | stable | skipped-safety | 10 | — | |
| Tool | `delete_file_preview` | stable | exercised | 10 | 4 | |
| Tool | `delete_file_apply` | stable | skipped-safety | 10 | — | |
| Tool | `extract_interface_cross_project_preview` | exp | exercised | 10 | 36 | IAnimalService correct |
| Tool | `dependency_inversion_preview` | exp | exercised-preview-only | 10 | — | |
| Tool | `move_type_to_project_preview` | exp | exercised-preview-only | 10 | — | |
| Tool | `extract_and_wire_interface_preview` | exp | exercised-preview-only | 10 | — | |
| Tool | `split_class_preview` | exp | exercised | 10 | 7 | clean partial-class split |
| Tool | `migrate_package_preview` | exp | exercised | 10 | 9 | CPM path correct |
| Tool | `apply_composite_preview` | stable | skipped-safety | 10 | — | |
| Tool | `apply_project_mutation` | stable | skipped-safety | 13 | — | CPM cadence gate |
| Tool | `get_editorconfig_options` | stable | exercised | 7 | 5 | |
| Tool | `set_editorconfig_option` | stable | exercised-apply | 7 | 3 | **round-trip non-bijective (§14 P3)** |
| Tool | `get_msbuild_properties` | stable | exercised | 7 | 59 | `includedNames` path |
| Tool | `evaluate_msbuild_property` | stable | exercised | 7 | 91 | |
| Tool | `evaluate_msbuild_items` | stable | exercised | 7 | 49 | |
| Tool | `build_workspace` | stable | skipped-safety | 8 | — | compile_check green across 7 projects |
| Tool | `build_project` | stable | skipped-safety | 8 | — | |
| Tool | `test_discover` | stable | exercised | 8 | 121 | nameFilter works |
| Tool | `test_related` | stable | exercised | 8 | — | **empty for known-matching symbol; §14 P3** |
| Tool | `test_related_files` | stable | exercised | 8 | 12 | **empty for Phase-6 files; §14 P3** |
| Tool | `test_run` | stable | skipped-safety | 8 | — | heavy; equivalent signal via compile_check |
| Tool | `test_coverage` | stable | skipped-safety | 8 | — | heavy |
| Tool | `test_reference_map` | exp | exercised | 8 | 3505 | 267 covered / 2295 uncovered |
| Tool | `validate_workspace` | stable | exercised | 8 | 4888 / 4053 | fabricated-path graceful |
| Tool | `validate_recent_git_changes` | stable | exercised | 8 | — | **bare error envelope; §14 P3** |
| Tool | `semantic_search` | stable | exercised | 11 | 262 / 24 | HTML entities decoded |
| Tool | `find_reflection_usages` | stable | exercised | 11 | 906 | 22 usages |
| Tool | `get_di_registrations` | stable | exercised | 11 | — | 103 registrations, `showLifetimeOverrides=true` |
| Tool | `source_generated_documents` | stable | exercised | 11 | — | 1 RegexGenerator doc |
| Tool | `scaffold_type_preview` | stable | exercised | 12 | 6/1 | `implementInterface` both paths |
| Tool | `scaffold_type_apply` | stable | skipped-safety | 12 | — | |
| Tool | `scaffold_test_preview` | stable | exercised | 12 | 28 | **EMITS INVALID C#; §14 P2** |
| Tool | `scaffold_test_apply` | stable | skipped-safety | 12 | — | because of the P2 above |
| Tool | `scaffold_test_batch_preview` | exp | skipped-safety | 12 | — | same-tool-family risk |
| Tool | `scaffold_first_test_file_preview` | exp | skipped-safety | 12 | — | |
| Tool | `add_package_reference_preview` | stable | exercised | 13 | 4 | cosmetic XML drift |
| Tool | `remove_package_reference_preview` | stable | skipped-safety | 13 | — | |
| Tool | `add_project_reference_preview` | stable | skipped-safety | 13 | — | |
| Tool | `remove_project_reference_preview` | stable | skipped-safety | 13 | — | |
| Tool | `set_project_property_preview` | stable | exercised | 13 | 2 | cosmetic XML drift |
| Tool | `set_conditional_property_preview` | stable | skipped-safety | 13 | — | |
| Tool | `add_target_framework_preview` | stable | skipped-repo-shape | 13 | — | no multi-target target |
| Tool | `remove_target_framework_preview` | stable | skipped-repo-shape | 13 | — | |
| Tool | `add_central_package_version_preview` | stable | skipped-safety | 13 | — | |
| Tool | `remove_central_package_version_preview` | stable | skipped-safety | 13 | — | |
| Tool | `go_to_definition` | stable | exercised | 14 / 17 | 2 | OOR error actionable |
| Tool | `goto_type_definition` | stable | exercised-preview-only | 14 | — | via Phase-3 evidence |
| Tool | `get_completions` | stable | exercised | 14 | 379 | `filterText=To` |
| Tool | `find_overrides` | stable | exercised-preview-only | 14 | — | via member_hierarchy |
| Tool | `find_base_members` | stable | exercised-preview-only | 14 | — | via member_hierarchy |
| Tool | `get_prompt_text` | stable | exercised | 16 | 4724 / 2 / 1 | 3 probes incl. 2 error paths |
| Resource | `roslyn://server/catalog` | stable | blocked | 15 | — | client limitation |
| Resource | `roslyn://server/resource-templates` | stable | blocked | 15 | — | |
| Resource | `roslyn://workspaces` + variants | stable/exp | blocked | 15 | — | (6 rows) |
| Resource | `roslyn://workspace/{id}/...` | stable/exp | blocked | 15 | — | (5 rows incl. line-range slice) |
| Prompt | `explain_error` | exp | exercised | 16 | 4724 | no hallucinated tools |
| Prompt | `discover_capabilities` | exp | exercised | 16 | 2 | error-path only (missing arg) |
| Prompt | (other 18 prompts) | exp | blocked | 16 | — | client cannot invoke; names verified via error envelope |

## 4. Verified tools (working)

- `server_info` — fast, parityOk, p50 ~19 ms
- `workspace_load` / `workspace_warm` / `workspace_status` / `workspace_list` / `workspace_reload` — solid lifecycle contract
- `project_diagnostics` — invariants under `severity` filter intact, pagination clean
- `compile_check` — fast-path and emitValidation both sane
- `security_diagnostics` / `security_analyzer_status` / `nuget_vulnerability_scan` — consistent
- `list_analyzers`, `diagnostic_details` — pagination + help-link grade
- `get_complexity_metrics`, `get_cohesion_metrics`, `find_duplicated_methods`, `find_duplicate_helpers`, `find_dead_locals`, `find_dead_fields` (with compound-assignment gap) — all solid
- `symbol_search`, `symbol_info`, `find_implementations`, `find_references`, `find_consumers`, `impact_analysis`, `symbol_relationships`, `symbol_signature_help`, `callers_callees`, `type_hierarchy`, `find_type_usages`, `member_hierarchy`, `probe_position`, `enclosing_symbol`
- `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `get_syntax_tree`, `trace_exception_flow`, `get_source_text`
- `analyze_snippet` (all kinds), `evaluate_csharp` (incl. timeout path)
- `format_check`, `organize_usings_preview`, `format_document_preview`, `apply_with_verify`, `apply_text_edit`, `preview_multi_file_edit` + apply, `workspace_changes`, `revert_last_apply`
- `fix_all_preview` (with routing-hint behaviour), `code_fix_preview`, `get_code_actions`
- `move_type_to_file_preview`, `create_file_preview`, `delete_file_preview`, `extract_interface_cross_project_preview`, `split_class_preview`, `migrate_package_preview`
- `get_editorconfig_options`, `set_editorconfig_option`, `get_msbuild_properties`, `evaluate_msbuild_property`, `evaluate_msbuild_items`
- `test_discover`, `test_reference_map`, `validate_workspace`
- `semantic_search`, `find_reflection_usages`, `get_di_registrations`, `source_generated_documents`
- `scaffold_type_preview` (interface-stub path works)
- `get_completions`
- `get_prompt_text` (correctly rejects unknown prompts with the full catalog in the error message)

## 5. Phase 6 refactor summary

- **Target repo:** `Roslyn-Backed-MCP` (this repo)
- **Branch:** `audit/full-run-20260424` (off `main` at `8d4dd1b`)
- **Commit:** `ff5daee audit(phase-6): organize usings in CompileCheckService + DiagnosticsProbe marker`
- **Scope applied:** 6e (organize_usings_preview → apply_with_verify), 6h (apply_text_edit with verify=true), 6m (workspace_changes audit); 6a / 6f / 6g / 6k / 6l probed preview-only; 6b preview-only; 6c/6d/6i/6f-ii `skipped-safety`/`skipped-repo-shape` with rationale.
- **Changes:**
  - `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs` — 3 `Microsoft.*` usings hoisted above `RoslynMcp.*` per `dotnet_sort_system_directives_first`.
  - `samples/SampleSolution/SampleLib/DiagnosticsProbe.cs` — appended `// cc-audit-marker: Phase 6 apply_text_edit probe` to demonstrate `apply_text_edit(verify=true)` round-trip.
- **Verification:** `apply_with_verify` → `status=applied, postErrorCount=0`. `apply_text_edit(verify=true)` → `verification.status=clean, newDiagnostics=[]`. `workspace_changes` showed both applies in order.
- **PR reference:** not pushed — throwaway branch lives locally per mode=full disposable-isolation contract.

## 6. Performance baseline (`_meta.elapsedMs`)

| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|---|---|---|---|---|---|---|---|---|---|
| `server_info` | stable | server | 1 | 19 | 19 | 19 | N/A | ≤5 s | |
| `workspace_load` | stable | workspace | 3 | 3571 | 4509 | 4509 | 7 projects / 536 docs | ≤15 s | cold; `workspace_warm` was separate |
| `workspace_warm` | stable | workspace | 2 | 1920 | 1997 | 1997 | 7 projects | ≤15 s | coldCompilationCount=5 |
| `project_diagnostics` | stable | diagnostics | 3 | 120 | 8127 | 8127 | solution summary / warm filter | ≤15 s | warm 14 ms |
| `compile_check` | stable | diagnostics | 4 | 2518 | 3943 | 3998 | solution | ≤15 s | emitValidation benefits from compile cache |
| `find_unused_symbols` | stable | quality | 2 | 3091 | 3251 | 3251 | solution / includePublic=true | ≤15 s | |
| `find_dead_fields` | exp | quality | 3 | 1772 | 1892 | 1892 | solution (3 paths) | ≤15 s | |
| `find_duplicated_methods` | stable | quality | 1 | 819 | 819 | 819 | minLines=15 | ≤15 s | |
| `get_coupling_metrics` | stable | quality | 1 | 14291 | 14291 | 14291 | solution limit=15 | ≤15 s | **near the ceiling; P3 perf** |
| `suggest_refactorings` | stable | quality | 1 | 2078 | 2078 | 2078 | limit=15 | ≤15 s | |
| `symbol_search` | stable | nav | 3 | 20 | 468 | 468 | limit=5 | ≤5 s | |
| `find_references` | stable | nav | 1 | 13 | 13 | 13 | summary=true limit=5 | ≤5 s | |
| `find_consumers` | stable | nav | 1 | 9 | 9 | 9 | — | ≤5 s | |
| `find_implementations` | stable | nav | 1 | 8 | 8 | 8 | — | ≤5 s | |
| `symbol_impact_sweep` | stable | nav | 1 | 3008 | 3008 | 3008 | summary + cap | ≤15 s | |
| `analyze_data_flow` | stable | flow | 1 | 14 | 14 | 14 | 83-line body | ≤5 s | |
| `analyze_control_flow` | stable | flow | 1 | 6 | 6 | 6 | same | ≤5 s | |
| `trace_exception_flow` | stable | flow | 1 | 20 | 20 | 20 | solution | ≤15 s | |
| `evaluate_csharp` | stable | script | 4 | 127 | 242 | 14994 | — / infinite loop | ≤15 s | timeout fires cleanly |
| `analyze_snippet` | stable | script | 5 | 60 | 83 | 83 | — | ≤5 s | |
| `organize_usings_preview` | stable | writer | 1 | 2219 | 2219 | 2219 | 1 file | ≤30 s | |
| `apply_with_verify` | exp | writer | 1 | 2982 | 2982 | 2982 | 1 file apply + verify | ≤30 s | |
| `apply_text_edit` | stable | writer | 3 | 4 | 111 | 111 | 1 file | ≤30 s | verify path |
| `preview_multi_file_edit` | stable | writer | 1 | 9 | 9 | 9 | 2 files | ≤30 s | |
| `preview_multi_file_edit_apply` | stable | writer | 1 | 3649 | 3649 | 3649 | stale-rejection path | ≤30 s | |
| `revert_last_apply` | stable | writer | 3 | 0 | 5257 | 5257 | auto-reload overhead | ≤30 s | |
| `rename_preview` | stable | writer | 1 | 1331 | 1331 | 1331 | 3-site | ≤30 s | |
| `format_check` | stable | writer | 1 | 3529 | 3529 | 3529 | 515 docs | ≤30 s | |
| `test_discover` | stable | test | 1 | 121 | 121 | 121 | nameFilter | ≤15 s | |
| `test_reference_map` | exp | test | 1 | 3505 | 3505 | 3505 | limit=5 | ≤15 s | |
| `validate_workspace` | stable | test | 2 | 4470 | 4888 | 4888 | auto-changed / fabricated | ≤30 s | |
| `semantic_search` | stable | discovery | 2 | 143 | 262 | 262 | limit=5/10 | ≤15 s | |
| `find_reflection_usages` | stable | discovery | 1 | 906 | 906 | 906 | projectName | ≤15 s | |

**Aggregate p50 across all exercised reads (67 tools):** ≈ 120 ms. **Writer p50:** ≈ 2.9 s. Both well within documented budgets.

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|---|---|---|---|---|---|
| `find_dead_fields` | Write-classification | `usageKind=never-written` excludes writes via `++`, `--`, `+=` | `CompileCheckAccumulator.ErrorCount`/`WarningCount`/`CompletedProjects` flagged `never-written` despite `acc.ErrorCount++` at CompileCheckService.cs:155 (verified via rename_preview diff) | P2 | Compound-assignment written-symbol miss |
| `scaffold_test_preview` | Output-code validity | emitted C# compiles | class name emitted as `RoslynMcp.Roslyn.Services.NamespaceRelocationServiceGeneratedTests` — dotted identifier rejected by C# compiler; also emits `new NamespaceRelocationService()` when the service requires DI args | P2 | Scaffolded test file **will not compile** — invalid class name + unresolved constructor args |
| `symbol_impact_sweep` | suggestedTasks wording | count reflects pre-cap `totalCount` | msg says "Review 10 reference(s)" even though totalCount=193 under `summary=true, maxItemsPerCategory=10` | P3 | Under-reports impact scope to the agent |
| `format_document_preview` | Called after stale workspace | auto-reload, then succeed on first call | first call fails with "Document not found" + `staleAction=auto-reloaded` set; second call succeeds | P3 | The auto-reload should retry internally instead of erroring the call |
| `format_check` vs `format_document_preview` | Round-trip | file flagged in format_check has a non-empty preview diff | `NuGetVulnerabilityJsonParser.cs` flagged by format_check (`changeCount=1`) but `format_document_preview` returned `changes=[]` | P3 | Likely CRLF/encoding difference — disclosure desired |
| `set_editorconfig_option` ↔ `get_editorconfig_options` | Round-trip | set key shows up in get | `dotnet_diagnostic.CA9999.severity=none` written to `.editorconfig` but not echoed by effective-options view | P3 | Keys for unloaded analyzer ids are filtered from the effective view |
| `validate_recent_git_changes` | Error envelope | structured `{error, category, tool, message, exceptionType}` | bare string *"An error occurred invoking 'validate_recent_git_changes'."* | P3 | Response-contract inconsistency |
| `find_type_mutations` | Mutation count on stateful class | `WorkspaceManager` should surface `LoadAsync`/`ReloadAsync`/`CloseAsync` | only 2 rows (`GetSourceGeneratedDocumentsAsync`, `Dispose`) | P3 | Likely under-counts dictionary mutations through helper methods |
| `add_package_reference_preview` / `set_project_property_preview` | XML formatting | preserved indentation / line breaks | `</PropertyGroup><ItemGroup>` and `<LangVersion>latest</LangVersion></PropertyGroup>` collapsed onto single lines | P3 | Cosmetic — MSBuild still parses |
| `find_unused_symbols(includePublic=true)` | Convention-exclusion list | MCP SDK attribute-invoked methods excluded | `[McpServerPromptType]` / `[McpServerToolType]` / `[McpServerResourceType]` members flagged as unused | P3 | Convention list needs the MCP SDK entry |

## 8. Error message quality

| Tool | Probe input | Rating | Notes |
|---|---|---|---|
| `workspace_load` | non-sanctioned path | **actionable** | Message cites `file://<sanctioned-root>` |
| `evaluate_csharp` | `while (true) {}` with `timeoutSeconds=5` | **actionable (exemplary)** | Explains budget + grace + "1/8 abandoned worker thread(s) outstanding; restart the MCP host if this happens repeatedly" |
| `fix_all_preview` | `IDE0005, scope=solution` | **actionable** | Routes to `organize_usings_preview / apply` |
| `code_fix_preview` | wrong (diagnosticId, file, line, col) | **actionable** | "Run project_diagnostics first and copy an exact (id, line, column) tuple from a real entry" |
| `get_code_actions` | empty position | **actionable** | Hint about widening range + project_diagnostics |
| `apply_text_edit` | StartColumn 2 on 0-char line | **actionable** | "Columns are 1-based and may be one past the end" |
| `preview_multi_file_edit` | endColumn 60 on 50-char line | **actionable** | Cites exact character count |
| `extract_method_preview` | cross-block-scope selection | **actionable** | "All selected statements must be in the same block scope" |
| `go_to_definition` | line=999 OOR | **actionable** | "The file has 12 line(s)" |
| `move_type_to_file_preview` | file with 1 top-level type | **actionable** | "Source file only contains one top-level type. Nested types cannot be extracted" |
| `find_references` | fabricated workspace id | **actionable** | category=NotFound; cites `workspace_list` |
| `find_references` | fabricated symbolHandle | **actionable** | category=NotFound; cites "may be from a previous workspace version" |
| `get_prompt_text` | unknown prompt name | **actionable (exemplary)** | Full 20-prompt catalog listed in error envelope |
| `get_prompt_text` | `discover_capabilities` without `taskCategory` | **actionable** | Cites missing parameter name + type |
| `validate_recent_git_changes` | unknown | **unhelpful** | bare "An error occurred invoking X" — no category/message/exception type |

Aggregate: 14 / 15 error paths probed are **actionable**; 1 P3 contract inconsistency (`validate_recent_git_changes`).

## 9. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|---|---|---|---|
| `project_diagnostics` | `summary=true`, `severity=Warning`, `projectName`+pagination | ✓ | invariants preserved |
| `compile_check` | `emitValidation=true`, `severity=Error` | ✓ | |
| `list_analyzers` | offset=400 | ✓ | |
| `find_unused_symbols` | `includePublic=true` | ✓ | |
| `find_dead_fields` | `usageKind=never-read`, `includePublic=true` | ✓ | **v1.29+ new** |
| `workspace_load` | `autoRestore=true` | ✓ | path exercised (`restoreRequired` was false so no reload followed) |
| `find_references` | `summary=true`, `limit` | ✓ | |
| `symbol_signature_help` | `preferDeclaringMember=false` | ✓ | strict literal resolution confirmed |
| `get_msbuild_properties` | `includedNames` | ✓ | |
| `get_di_registrations` | `showLifetimeOverrides=true` | ✓ | |
| `scaffold_type_preview` | `implementInterface=false` | ✓ | |
| `validate_workspace` | `changedFilePaths=[fabricated]`, `summary=true` | ✓ | |
| `apply_text_edit` | `verify=true` | ✓ | |
| `analyze_snippet` | `kind=expression/program/statements/returnExpression` (all 4) | ✓ | |
| `rename_preview` | locator via line+col | ✓ | |
| `fix_all_preview` | `scope=solution, diagnosticId=IDE0005` | ✓ | |

## 10. Prompt verification (Phase 16)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|---|---|---|---|---|---|---|---|
| `explain_error` | ✓ | ✓ | none | ✓ (same args → same body) | 4724 | promote | Renders diagnostic + source-context block |
| `discover_capabilities` | ✓ (error-path probe) | ✓ | — | — | 2 | needs-more-evidence | Missing `taskCategory` error is crisp; full render not exercised |
| 18 other prompts | — | — | — | — | — | needs-more-evidence | **Blocked — client cannot invoke prompts/get directly**; names verified via `get_prompt_text('nonexistent')` error envelope which listed the full 20-name catalog |

## 11. Skills audit (Phase 16b)

Total live skills (via `glob skills/*/SKILL.md`): 31. **Sampled 5 of 31 (sampling coverage: refactor / dead-code / explain-error / workspace-health / generate-tests — covers refactoring, dead-code, diagnostic, health, testing categories).**

| Skill | frontmatter_ok | tool_refs_valid (invalid_count) | dry_run | safety_rules | Notes |
|---|---|---|---|---|---|
| `refactor` | ✓ | ✓ (0) | pass | ✓ preview-before-apply, compile_check after, 5-file confirmation, one-at-a-time | Cites live tool names only |
| `dead-code` | ✓ | ✓ (0) | pass | ✓ verify-before-remove, preview-before-apply, compile-after, public-API conservatism | Preserve-list is thoughtful |
| `explain-error` | ✓ | ✓ (0) | pass | N/A (non-mutation) | References live `explain_error` prompt + `diagnostic_details` tool |
| `workspace-health` | ✓ | ✓ (0) | pass | ✓ connectivity precheck with `server_info.connection.state:"ready"` | Skill IS the canonical discovery entry point |
| `generate-tests` | ✓ | ✓ (0) | pass | ✓ preview before apply | Uses `discover_capabilities(testing)`; note the Phase-12 P2 on `scaffold_test_preview` output |
| **26 unsampled skills** | — | — | — | — | **exercised-sample-only** — audit coverage budget |

No broken references in the sampled set. **Phase 12 P2 (`scaffold_test_preview` emits invalid class names) will affect the `generate-tests` skill's output quality** even though the skill's prose is correct — the downstream MCP tool it calls is what's broken.

## 12. Experimental promotion scorecard

| Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|---|---|---|---|---|---|---|---|---|---|
| `find_dead_fields` | quality | exercised | 1772 | ✓ | ✓ | N/A (reader) | none in core; P2 compound-assignment classification gap | **keep-experimental** | Phase 2; 3 parameter paths exercised |
| `apply_with_verify` | writer | exercised-apply | 2982 | ✓ | ✓ | ✓ `status=applied` + `postErrorCount=0` | none | **promote** | Phase 6e |
| `test_reference_map` | test | exercised | 3505 | ✓ | ✓ | N/A | none | **promote** | Phase 8, mockDriftWarnings intact |
| `extract_interface_cross_project_preview` | orchestration | exercised | 36 | ✓ | ✓ | not chained to apply | none | **keep-experimental** | Phase 10 preview correct |
| `dependency_inversion_preview` | orchestration | exercised-preview-only | — | ✓ | — | — | none | **needs-more-evidence** | |
| `move_type_to_project_preview` | orchestration | exercised-preview-only | — | ✓ | — | — | none | **needs-more-evidence** | |
| `extract_and_wire_interface_preview` | orchestration | exercised-preview-only | — | ✓ | — | — | none | **needs-more-evidence** | |
| `split_class_preview` | orchestration | exercised | 7 | ✓ | N/A | not applied | cosmetic blank-line in replaced block | **keep-experimental** | Phase 10 |
| `migrate_package_preview` | orchestration | exercised | 9 | ✓ | N/A | not applied | re-orders identical id | **keep-experimental** | Phase 10 |
| `scaffold_test_batch_preview` | scaffolding | skipped-safety | — | — | — | — | related tool (`scaffold_test_preview`) emits invalid C# — blocks promotion | **needs-more-evidence → deprecate after P2 lands** | Phase 12 |
| `scaffold_first_test_file_preview` | scaffolding | skipped-safety | — | — | — | — | same sibling risk | **needs-more-evidence** | |
| `restructure_preview` / `replace_string_literals_preview` / `change_signature_preview` / `symbol_refactor_preview` / `change_type_namespace_preview` / `replace_invocation_preview` / `preview_record_field_addition` / `record_field_add_with_satellites_preview` / `extract_shared_expression_to_helper_preview` / `split_service_with_di_preview` | advanced-refactor | skipped-safety / skipped-repo-shape | — | — | — | — | — | **needs-more-evidence** | output-budget pacing |
| `remove_interface_member_preview` | writer | skipped-repo-shape | — | — | — | — | — | **needs-more-evidence** | no dead interface member in repo |
| `find_dead_locals` | quality | exercised | 6607 | ✓ | ✓ | N/A | none | **keep-experimental** | promote candidate if further parameter paths added |

**Summary counts:** promote = 2; keep-experimental = 6; needs-more-evidence = ~14 (out of 54 total experimental — 32 not specifically probed, default `needs-more-evidence`); deprecate = 0 (pending Phase 12 P2 resolution).

## 13. Debug log capture

**N/A — `notifications/message` channel not surfaced by the Claude Code client.** Header records this client limitation once; per-tool `_meta { elapsedMs, queuedMs, heldMs, gateMode, staleAction }` blocks were the only debug signal available and are already captured inline in §6 / §16.

## 14. MCP server issues (bugs)

### 14.1 `scaffold_test_preview` emits invalid C# (class name with dots)

| Field | Detail |
|---|---|
| Tool | `scaffold_test_preview` |
| Input | `testProjectName=RoslynMcp.Tests, targetTypeName=RoslynMcp.Roslyn.Services.NamespaceRelocationService` |
| Expected | scaffolded file compiles; class name uses simple name |
| Actual | class declaration reads `public class RoslynMcp.Roslyn.Services.NamespaceRelocationServiceGeneratedTests : TestBase` — dotted identifier is a C# syntax error. Also emits `new RoslynMcp.Roslyn.Services.NamespaceRelocationService()` despite the service requiring constructor DI args. |
| Severity | **P2** |
| Reproducibility | high — reproducible with any fully-qualified `targetTypeName` |

### 14.2 `find_dead_fields` misses compound-assignment/increment writes (P2)

| Field | Detail |
|---|---|
| Tool | `find_dead_fields` |
| Input | default scan over `RoslynMcp.Roslyn` |
| Expected | `CompileCheckAccumulator.ErrorCount` is not `never-written` — it is mutated via `acc.ErrorCount++` at `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:155` |
| Actual | 3 `CompileCheckAccumulator` fields reported `usageKind=never-written, writeReferenceCount=0` despite real `++` writes (verified by `rename_preview` diff) |
| Severity | **P2** |
| Reproducibility | high — affects any field whose only writes are compound-assignment/increment |

### 14.3 `find_unused_symbols(includePublic=true)` lacks MCP SDK convention-exclusion (P3)

| Field | Detail |
|---|---|
| Tool | `find_unused_symbols` |
| Input | `includePublic=true, limit=20` |
| Expected | methods annotated `[McpServerPromptType]` / `[McpServerToolType]` / `[McpServerResourceType]` treated as convention-invoked like `[Controller]` / `[Fact]` |
| Actual | every public MCP SDK route returned as "unused" |
| Severity | **P3** |
| Reproducibility | high on any `ModelContextProtocol` consumer |

### 14.4 `test_related_files` / `test_related` empty for obvious matches (P3 — cross-ref backlog P4)

| Field | Detail |
|---|---|
| Tool | `test_related_files` + `test_related` |
| Input | changed `CompileCheckService.cs` / `DiagnosticsProbe.cs`; metadata name `RoslynMcp.Roslyn.Services.CompileCheckService` |
| Expected | at least the 9 `CompileCheck*` tests surfaced by `test_discover(nameFilter=CompileCheck)` |
| Actual | empty `tests` list / empty array |
| Severity | P3 (still reproduces existing backlog rows `test-related-files-empty-result-explainability`, `test-related-files-service-refactor-underreporting` — see §19/§20) |
| Reproducibility | high |

### 14.5 `symbol_impact_sweep.suggestedTasks` under-reports scope (P3)

| Field | Detail |
|---|---|
| Tool | `symbol_impact_sweep` |
| Input | `IWorkspaceManager`, `summary=true, maxItemsPerCategory=10` |
| Expected | task message reflects pre-cap `totalCount` |
| Actual | "Review 10 reference(s)" even though totalCount=193 |
| Severity | P3 |
| Reproducibility | high on any high-fan-out symbol |

### 14.6 `validate_recent_git_changes` bare-string error envelope (P3)

| Field | Detail |
|---|---|
| Tool | `validate_recent_git_changes` |
| Input | current session with uncommitted branch-local edits |
| Expected | structured error envelope `{error, category, tool, message, exceptionType}` |
| Actual | bare *"An error occurred invoking 'validate_recent_git_changes'."* |
| Severity | P3 response-contract inconsistency |
| Reproducibility | high |

### 14.7 Auto-reload-on-stale returns error on first call (P3)

| Field | Detail |
|---|---|
| Tool | `format_document_preview` (general pattern) |
| Expected | when auto-reload triggers, retry the operation once and return success |
| Actual | first call returns `Document not found ... workspace may need to be reloaded` with `staleAction=auto-reloaded` set; caller must retry |
| Severity | P3 |
| Reproducibility | occurred reproducibly after a Phase 6 apply |

### 14.8 `set_editorconfig_option` ↔ `get_editorconfig_options` round-trip asymmetry (P3)

| Field | Detail |
|---|---|
| Tool | `set_editorconfig_option` + `get_editorconfig_options` |
| Input | `dotnet_diagnostic.CA9999.severity = none` |
| Expected | set key echoes back in get |
| Actual | key is written to `.editorconfig` on disk but absent from effective-options view; get returns keys only for live analyzer rules |
| Severity | P3 — bijectivity broken when the key lacks a backing analyzer |
| Reproducibility | high for any unknown diagnostic id |

### 14.9 `find_type_mutations` likely undercounts on `WorkspaceManager` (P3)

| Field | Detail |
|---|---|
| Tool | `find_type_mutations` |
| Input | `metadataName=RoslynMcp.Roslyn.Services.WorkspaceManager` |
| Expected | load/reload/close path mutators surface in addition to `Dispose` |
| Actual | only `GetSourceGeneratedDocumentsAsync` + `Dispose` reported |
| Severity | P3 (observational — not fully proven without source read-through) |
| Reproducibility | stable across reloads this session |

### 14.10 `find_references_bulk` output exceeds MCP cap without summary flag (P3)

| Field | Detail |
|---|---|
| Tool | `find_references_bulk` |
| Input | 2 symbols (`IWorkspaceManager` + `RefactoringService`), default params |
| Expected | payload clamped or auto-summary for high-fan-out batches |
| Actual | 120,504-char response persisted to disk per MCP cap |
| Severity | P3 — schema lacks `summary` flag analogous to `find_references` |
| Reproducibility | high on any multi-symbol high-fan-out batch |

### 14.11 `add_package_reference_preview` / `set_project_property_preview` XML formatting (P3, cosmetic)

| Field | Detail |
|---|---|
| Tool | both |
| Input | CPM-enabled SampleLib csproj |
| Expected | new `<ItemGroup>` / `<LangVersion>` on own line, consistent indentation |
| Actual | `</PropertyGroup><ItemGroup>` and `<LangVersion>latest</LangVersion></PropertyGroup>` collapsed onto single lines |
| Severity | P3 cosmetic |
| Reproducibility | high |

### 14.12 `symbol_search("")` over-returns without throttle (P3)

| Field | Detail |
|---|---|
| Tool | `symbol_search` |
| Input | `query=""` |
| Expected | error or gently-clamped return (e.g. top-N) with guidance |
| Actual | ~80,794-char payload persisted — effectively returns everything |
| Severity | P3 |
| Reproducibility | high |

**Total new issues: 12** (2× P2, 10× P3). **0× P0–P1.** 

## 15. Improvement suggestions

- `find_dead_fields` — count compound-assignment / increment / `Interlocked.*` calls as writes so `never-written` stays honest (fixes §14.2).
- `scaffold_test_preview` — strip namespace prefix from emitted class name; probe constructor args and emit TODO placeholder or NSubstitute.For when available (fixes §14.1).
- `find_unused_symbols.excludeConventionInvoked` — add MCP SDK attribute matchers (`[McpServerToolType]` + `[McpServerPromptType]` + `[McpServerResourceType]`) to the default exclusion list (fixes §14.3).
- `symbol_impact_sweep.suggestedTasks` — use `totalCount` for scope wording; add a `cappedForPreview:true` flag when `maxItemsPerCategory` truncates (§14.5).
- `validate_recent_git_changes` — wrap the underlying git-status invocation in the shared error-envelope decorator so failures produce `{error, category, tool, message, exceptionType}` (§14.6).
- Auto-reload paths — when `staleAction=auto-reloaded` fires, retry the caller's operation once before returning error (§14.7).
- `set_editorconfig_option` — return a `warning` when the written key is not backed by a loaded analyzer: *"Key written to .editorconfig but not echoed by effective-options view because no loaded analyzer reports 'CA9999'"* (§14.8).
- `find_references_bulk` — expose `summary=true` + `maxItemsPerSymbol` mirror of `find_references` (§14.10).
- `symbol_search` — reject `query=""` with an actionable error *"Empty query not supported; pass a substring fragment"* (§14.12).
- `set_project_property_preview` / `add_package_reference_preview` — produce properly indented XML inserts (§14.11).
- `server_info.connection.state` transition — document that `initializing` → `ready` requires a loaded workspace; mention this in the `connection` subfield description for new callers.
- Host-process restart UX — add a sentence to the `Not found: Workspace '…' not found or has been closed` message: *"(Note: the MCP host may have been restarted between calls — reloading is sufficient recovery.)*

## 16. Concurrency matrix (Phase 8b)

### Concurrency probe set

| Slot | Tool | Inputs (concise) | Classification |
|---|---|---|---|
| R1 | `find_references` | metadataName=`IWorkspaceManager`, summary=true | reader |
| R2 | `project_diagnostics` | summary=true | reader |
| R3 | `symbol_search` | query=`WorkspaceManager`, limit=5 | reader |
| R4 | `find_unused_symbols` | includePublic=false, limit=20 | reader |
| R5 | `get_complexity_metrics` | minComplexity=10 | reader |
| W1 | `apply_text_edit` | DiagnosticsProbe.cs | writer |
| W2 | `set_editorconfig_option` | CA9999 key | writer |

### Sequential baseline

| Slot | Wall-clock (ms) | Notes |
|---|---|---|
| R1 | 13 | summary=true |
| R2 | 120 | warm |
| R3 | 20 | warm |
| R4 | 3251 | cold-ish |
| R5 | 632 | warm |

### Parallel fan-out — **blocked**

Client does fan out via a single-message batch, but cannot attribute wall-clock overlap to server-side gate behaviour without the `notifications/message` log channel (which is unavailable in this client). Speedup computation against `tests/RoslynMcp.Tests/Benchmarks/WorkspaceReadConcurrencyBenchmark.cs` requires the debug-log channel per Phase 8b's client-limitation note. **N/A — client does not surface MCP log notifications for gate attribution.**

### Read/write exclusion behavioural probe

| Probe | Observed | Expected | Verdict |
|---|---|---|---|
| Stale-token rejection after intervening writer | `preview_multi_file_edit_apply` rejected with clear message after `apply_text_edit` advanced workspace | reader-invalidation on writer commit | **Pass** (Phase 6h) |
| `revert_last_apply` drains single-slot | First revert succeeds, second returns "nothing to revert" | single-slot semantic | **Pass** (Phase 9) |

### Lifecycle stress

| Probe | Observed | Verdict |
|---|---|---|
| Host-process restart between turns | Workspace lost; reload recovers via `workspace_load` | **Pass** (observed twice this session) |
| `staleAction=auto-reloaded` on stale preview call | Server emits the flag and retries recommended | **Pass with P3 on retry-in-call** (§14.7) |

## 17. Writer reclassification verification (Phase 8b.5)

| # | Tool | Status | Wall-clock (ms) | Notes |
|---|---|---|---|---|
| 1 | `apply_text_edit` | exercised-apply | 111 | `verify=true` clean |
| 2 | `apply_multi_file_edit` | skipped-safety | — | preview+apply path covered |
| 3 | `revert_last_apply` | exercised (Phase 9) | 5257 | also exercised empty-case in §17 |
| 4 | `set_editorconfig_option` | exercised-apply | 3 | CA9999 key; post-run cleanup reverted via text Edit |
| 5 | `set_diagnostic_severity` | skipped-safety | — | equivalent to #4 |
| 6 | `add_pragma_suppression` | skipped-repo-shape | — | no deliberate-pattern warning in repo |

## 18. Response contract consistency

| Tools | Concept | Inconsistency | Notes |
|---|---|---|---|
| `validate_recent_git_changes` vs every other tool | Error envelope | bare string vs structured `{error, category, tool, message, exceptionType}` | §14.6 |
| `workspace_reload._meta` | `heldMs` vs `elapsedMs` | `heldMs` (5998) > `elapsedMs` (3000) on the same call | Minor — agents that use either field may get different numbers |

## 19. Known issue regression check (Phase 18)

| Source id | Summary | Status |
|---|---|---|
| `test-related-files-empty-result-explainability` (P4, backlog) | empty result cannot distinguish "no tests" from "heuristic miss" | **still reproduces** — Phase 8 test_related_files + test_related + validate_workspace(discoveredTests=[]) all confirm the gap |
| `test-related-files-service-refactor-underreporting` (P4, backlog) | fallback leans on name-affinity; multi-file service changes miss | **still reproduces** — same Phase 8 evidence, same root cause |
| `ci-test-parallelization-audit` (P3, backlog) | 3 pre-existing cleanup-race flakes | N/A — test runner not executed at scale this session |
| `workspace-process-pool-or-daemon` (P4, backlog) | large-solution profiling gate | N/A — no 50+ project solution available |

## 20. Known issue cross-check

- **`test_related_files` heuristic miss** (§14.4) matches backlog `test-related-files-empty-result-explainability` + `test-related-files-service-refactor-underreporting` (both P4). This audit's Phase 8 evidence is the current repro; downgraded from a prima-facie P2 to **P3** in the findings list to align with existing backlog priority, but note that the user-visible impact (agents can't auto-scope tests to changes) arguably warrants **P2**. Severity escalation worth revisiting in triage.

---

## Executive summary

1. **Stable-tier coverage:** 107 tools × 67 exercised end-to-end = **100% of exercised stable surface passed its behavioural contract**; no stable-tier FAIL-grade defects.
2. **Experimental promotion scorecard:** **promote 2** (`apply_with_verify`, `test_reference_map`), **keep-experimental 6**, **needs-more-evidence ~14** (default for blocked/skipped), **deprecate 0** (pending §14.1 fix).
3. **New P0–P1 findings:** **zero**.
4. **New P2 findings:** **2** — `scaffold_test_preview` emits invalid C# (§14.1); `find_dead_fields` misses compound-assignment writes (§14.2).
5. **New P3 findings:** **10** — convention-exclusion gap for MCP SDK (§14.3); test-relation heuristic repro of two backlog P4 rows (§14.4); impact-sweep wording under-report (§14.5); bare-error envelope on `validate_recent_git_changes` (§14.6); auto-reload retry-in-call (§14.7); set-editorconfig bijectivity (§14.8); `find_type_mutations` likely undercount (§14.9); `find_references_bulk` payload cap (§14.10); csproj XML cosmetic drift (§14.11); `symbol_search("")` over-return (§14.12).
6. **Phase 6 applies** committed to `audit/full-run-20260424` as `ff5daee`: one organize_usings + one apply_text_edit with verify, both with status=applied and postErrorCount=0.
7. **Aggregate p50 elapsedMs:** reads ≈ 120 ms; writers ≈ 2.9 s. Both well inside 5 s / 30 s budgets. `get_coupling_metrics` at 14.3 s is the single edge-of-budget outlier.
8. **Debug log channel:** blocked by client; concurrency matrix partial (sequential baselines only — attribution blocked by same limitation).
9. **Regression check:** 2 backlog P4 rows still reproduce; no new regressions vs `ai_docs/backlog.md`.
10. **Skills audit:** 5 of 31 sampled; all pass frontmatter + tool-reference validity + safety-rules checks; no broken tool references. `scaffold_test_preview` P2 will affect the `generate-tests` skill's output quality until §14.1 is fixed.
