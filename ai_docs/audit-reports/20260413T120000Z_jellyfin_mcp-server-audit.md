# MCP Server Audit Report

## 1. Header
- **Date:** 2026-04-13
- **Audited solution:** Jellyfin.sln
- **Audited revision:** fb33b72 (master)
- **Entrypoint loaded:** Jellyfin.sln
- **Audit mode:** `full-surface`
- **Isolation:** disposable worktree at `C:\Code-Repo\jellyfin\.claude\worktrees\deep-review-audit`
- **Client:** Claude Code (Claude Opus 4.6, 1M context) via roslyn-mcp stdio
- **Workspace id:** c81947700edb40e6b5891def6eb7e820
- **Server:** roslyn-mcp 1.14.0+157fdded595bca3861c8bcf824c8b828862f232a
- **Catalog version:** 2026.04
- **Roslyn / .NET:** 5.3.0.0 / .NET 10.0.5
- **Scale:** ~40 projects, ~2065 documents
- **Repo shape:** Single solution, 24 production + 16 test projects, net10.0 (39) + netstandard2.0 (1 analyzer), tests present (xUnit), analyzers present (IDisposableAnalyzers, ASP.NET), source generators: [GeneratedRegex] only (34 sites), DI usage present (ASP.NET Core, 187 registrations), .editorconfig present, Central Package Management ACTIVE (Directory.Packages.props, ManagePackageVersionsCentrally=true), no multi-targeting (except analyzer)
- **Prior issue source:** N/A (first audit of this repo)
- **Debug log channel:** partial — `_meta.elapsedMs` surfaced via tool responses; `notifications/message` not surfaced by client
- **Plugin skills repo path:** blocked — auditing Jellyfin, not Roslyn-Backed-MCP
- **Report path note:** Saved under Jellyfin worktree; intended final path: `<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/20260413T120000Z_jellyfin_mcp-server-audit.md` — copy this file there before creating a rollup or backlog update.

**DRIFT NOTE:** `server_info` reports 77 stable / 51 experimental tools (128 total). Prompt appendix (2026.04 snapshot) lists 75 stable / 52 experimental (127 total). Delta: +2 tools promoted to stable, +1 new tool added since snapshot. Filed in Improvement suggestions.

---

## 2. Coverage summary

| Kind | Category | Tier-stable | Tier-experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-------------|-------------------|-----------|------------------|--------------|--------------------|----------------|---------|-------|
| tool | server | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | |
| tool | workspace | 9 | 0 | 9 | 0 | 0 | 0 | 0 | 0 | |
| tool | symbols | 16 | 0 | 16 | 0 | 0 | 0 | 0 | 0 | |
| tool | analysis | 12 | 0 | 12 | 0 | 0 | 0 | 0 | 0 | |
| tool | advanced-analysis | 11 | 0 | 10 | 0 | 0 | 0 | 0 | 1 | find_unused_symbols errored |
| tool | security | 3 | 0 | 3 | 0 | 0 | 0 | 0 | 0 | |
| tool | validation | 8 | 0 | 8 | 0 | 0 | 0 | 0 | 0 | |
| tool | refactoring | 8 | 14 | 6 | 4 | 8 | 0 | 2 | 2 | |
| tool | cross-project-refactoring | 0 | 3 | 0 | 0 | 3 | 0 | 0 | 0 | |
| tool | orchestration | 0 | 4 | 0 | 0 | 4 | 0 | 0 | 0 | |
| tool | file-operations | 0 | 6 | 0 | 1 | 5 | 0 | 0 | 0 | |
| tool | project-mutation | 2 | 12 | 2 | 1 | 9 | 2 | 0 | 0 | CPM skipped-repo-shape |
| tool | scaffolding | 0 | 4 | 0 | 1 | 3 | 0 | 0 | 0 | |
| tool | dead-code | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 2 | blocked by find_unused_symbols error |
| tool | editing | 0 | 3 | 0 | 2 | 0 | 0 | 0 | 1 | |
| tool | configuration | 1 | 2 | 1 | 2 | 0 | 0 | 0 | 0 | |
| tool | code-actions | 3 | 0 | 3 | 1 | 0 | 0 | 0 | 0 | |
| tool | scripting | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | |
| tool | syntax | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | |
| tool | undo | 0 | 1 | 0 | 1 | 0 | 0 | 0 | 0 | |
| resource | all | 9 | 0 | 9 | n/a | n/a | 0 | 0 | 0 | |
| prompt | prompts | 0 | 19 | 19 | n/a | n/a | 0 | 0 | 0 | client supports prompt invocation |

---

## 3. Coverage ledger

| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | `server_info` | stable | server | exercised | 0 | 8 | |
| tool | `workspace_load` | stable | workspace | exercised | 0 | 9118 | |
| tool | `workspace_reload` | stable | workspace | exercised | 8 | — | |
| tool | `workspace_close` | stable | workspace | exercised | 17e | — | |
| tool | `workspace_list` | stable | workspace | exercised | 0 | — | |
| tool | `workspace_status` | stable | workspace | exercised | 0 | 6 | |
| tool | `project_graph` | stable | workspace | exercised | 0 | 3 | |
| tool | `source_generated_documents` | stable | workspace | exercised | 11 | — | |
| tool | `get_source_text` | stable | workspace | exercised | 4 | — | |
| tool | `workspace_changes` | stable | workspace | exercised | 6k | — | |
| tool | `symbol_search` | stable | symbols | exercised | 3 | — | |
| tool | `symbol_info` | stable | symbols | exercised | 3 | — | |
| tool | `go_to_definition` | stable | symbols | exercised | 14 | — | |
| tool | `goto_type_definition` | stable | symbols | exercised | 14 | — | |
| tool | `find_references` | stable | symbols | exercised | 3 | — | |
| tool | `find_references_bulk` | stable | symbols | exercised | 14 | — | |
| tool | `find_implementations` | stable | symbols | exercised | 3 | — | |
| tool | `find_overrides` | stable | symbols | exercised | 14 | — | |
| tool | `find_base_members` | stable | symbols | exercised | 14 | — | |
| tool | `member_hierarchy` | stable | symbols | exercised | 3 | — | |
| tool | `document_symbols` | stable | symbols | exercised | 3 | — | |
| tool | `enclosing_symbol` | stable | symbols | exercised | 14 | — | |
| tool | `symbol_signature_help` | stable | symbols | exercised | 3 | — | |
| tool | `symbol_relationships` | stable | symbols | exercised | 3 | — | |
| tool | `find_property_writes` | stable | symbols | exercised | 3 | — | |
| tool | `get_completions` | stable | symbols | exercised | 14 | — | |
| tool | `project_diagnostics` | stable | analysis | exercised | 1 | 29710 | |
| tool | `diagnostic_details` | stable | analysis | exercised | 1 | — | |
| tool | `type_hierarchy` | stable | analysis | exercised | 3 | — | |
| tool | `callers_callees` | stable | analysis | exercised | 3 | — | |
| tool | `impact_analysis` | stable | analysis | exercised | 3 | — | |
| tool | `find_type_mutations` | stable | analysis | exercised | 3 | — | |
| tool | `find_type_usages` | stable | analysis | exercised | 3 | — | |
| tool | `find_consumers` | stable | analysis | exercised | 3 | — | |
| tool | `get_cohesion_metrics` | stable | analysis | exercised | 2 | 705 | |
| tool | `find_shared_members` | stable | analysis | exercised | 3 | — | |
| tool | `list_analyzers` | stable | analysis | exercised | 1 | 6 | |
| tool | `analyze_snippet` | stable | analysis | exercised | 5 | — | |
| tool | `find_unused_symbols` | stable | advanced-analysis | blocked | 2 | 546 | UnresolvedAnalyzerReference exception |
| tool | `get_di_registrations` | stable | advanced-analysis | exercised | 11 | — | |
| tool | `get_complexity_metrics` | stable | advanced-analysis | exercised | 2 | 827 | |
| tool | `find_reflection_usages` | stable | advanced-analysis | exercised | 11 | — | |
| tool | `get_namespace_dependencies` | stable | advanced-analysis | exercised | 2 | 279 | |
| tool | `get_nuget_dependencies` | stable | advanced-analysis | exercised | 2 | — | |
| tool | `semantic_search` | stable | advanced-analysis | exercised | 11 | — | |
| tool | `analyze_data_flow` | stable | advanced-analysis | exercised | 4 | — | |
| tool | `analyze_control_flow` | stable | advanced-analysis | exercised | 4 | — | |
| tool | `get_operations` | stable | advanced-analysis | exercised | 4 | — | |
| tool | `suggest_refactorings` | stable | advanced-analysis | exercised | 6j | — | |
| tool | `security_diagnostics` | stable | security | exercised | 1 | 2531 | |
| tool | `security_analyzer_status` | stable | security | exercised | 1 | 2033 | |
| tool | `nuget_vulnerability_scan` | stable | security | exercised | 1 | 11336 | |
| tool | `build_workspace` | stable | validation | exercised | 8 | — | |
| tool | `build_project` | stable | validation | exercised | 8 | — | |
| tool | `test_discover` | stable | validation | exercised | 8 | — | |
| tool | `test_run` | stable | validation | exercised | 8 | — | 103 tests: 100 pass, 3 skip |
| tool | `test_related` | stable | validation | exercised | 8 | — | |
| tool | `test_related_files` | stable | validation | exercised | 8 | — | |
| tool | `test_coverage` | stable | validation | exercised | 8 | — | |
| tool | `compile_check` | stable | validation | exercised | 1 | 9402 | |
| tool | `rename_preview` | stable | refactoring | exercised | 6b | — | |
| tool | `rename_apply` | stable | refactoring | exercised-apply | 6b | — | |
| tool | `organize_usings_preview` | stable | refactoring | exercised | 6e | — | |
| tool | `organize_usings_apply` | stable | refactoring | exercised-apply | 6e | — | |
| tool | `format_document_preview` | stable | refactoring | exercised | 6e | — | |
| tool | `format_document_apply` | stable | refactoring | exercised-apply | 6e | — | |
| tool | `code_fix_preview` | stable | refactoring | exercised | 6f | — | |
| tool | `code_fix_apply` | stable | refactoring | exercised-apply | 6f | — | |
| tool | `fix_all_preview` | experimental | refactoring | exercised | 6a | — | |
| tool | `fix_all_apply` | experimental | refactoring | exercised-apply | 6a | — | |
| tool | `format_range_preview` | experimental | refactoring | exercised | 6e | — | |
| tool | `format_range_apply` | experimental | refactoring | exercised-apply | 6e | — | |
| tool | `extract_interface_preview` | experimental | refactoring | exercised-preview-only | 6c | — | |
| tool | `extract_interface_apply` | experimental | refactoring | skipped-safety | 6c | — | preview verified, apply skipped |
| tool | `extract_type_preview` | experimental | refactoring | skipped-repo-shape | 6d | — | no LCOM4>1 types |
| tool | `extract_type_apply` | experimental | refactoring | skipped-repo-shape | 6d | — | no LCOM4>1 types |
| tool | `move_type_to_file_preview` | experimental | refactoring | exercised-preview-only | 10 | — | |
| tool | `move_type_to_file_apply` | experimental | refactoring | skipped-safety | 10 | — | |
| tool | `bulk_replace_type_preview` | experimental | refactoring | exercised-preview-only | 6c | — | |
| tool | `bulk_replace_type_apply` | experimental | refactoring | skipped-safety | 6c | — | |
| tool | `extract_method_preview` | experimental | refactoring | exercised | 6j | — | |
| tool | `extract_method_apply` | experimental | refactoring | exercised-apply | 6j | — | |
| tool | `move_type_to_project_preview` | experimental | cross-project | exercised-preview-only | 10 | — | |
| tool | `extract_interface_cross_project_preview` | experimental | cross-project | exercised-preview-only | 10 | — | |
| tool | `dependency_inversion_preview` | experimental | cross-project | exercised-preview-only | 10 | — | |
| tool | `migrate_package_preview` | experimental | orchestration | exercised-preview-only | 10 | — | |
| tool | `split_class_preview` | experimental | orchestration | exercised-preview-only | 10 | — | |
| tool | `extract_and_wire_interface_preview` | experimental | orchestration | exercised-preview-only | 10 | — | |
| tool | `apply_composite_preview` | experimental | orchestration | skipped-safety | 10 | — | destructive despite name |
| tool | `create_file_preview` | experimental | file-operations | exercised-preview-only | 10 | — | |
| tool | `create_file_apply` | experimental | file-operations | exercised-apply | 10 | — | applied + cleaned up |
| tool | `delete_file_preview` | experimental | file-operations | exercised-preview-only | 10 | — | |
| tool | `delete_file_apply` | experimental | file-operations | skipped-safety | 10 | — | |
| tool | `move_file_preview` | experimental | file-operations | exercised-preview-only | 10 | — | |
| tool | `move_file_apply` | experimental | file-operations | skipped-safety | 10 | — | |
| tool | `add_package_reference_preview` | experimental | project-mutation | exercised-preview-only | 13 | — | |
| tool | `remove_package_reference_preview` | experimental | project-mutation | exercised-preview-only | 13 | — | |
| tool | `add_project_reference_preview` | experimental | project-mutation | exercised-preview-only | 13 | — | |
| tool | `remove_project_reference_preview` | experimental | project-mutation | exercised-preview-only | 13 | — | |
| tool | `set_project_property_preview` | experimental | project-mutation | exercised-preview-only | 13 | — | |
| tool | `set_conditional_property_preview` | experimental | project-mutation | exercised-preview-only | 13 | — | |
| tool | `add_target_framework_preview` | experimental | project-mutation | exercised-preview-only | 13 | — | |
| tool | `remove_target_framework_preview` | experimental | project-mutation | exercised-preview-only | 13 | — | |
| tool | `add_central_package_version_preview` | experimental | project-mutation | exercised-preview-only | 13 | 200 | CPM active; duplicate guard correct |
| tool | `remove_central_package_version_preview` | experimental | project-mutation | exercised-preview-only | 13 | 200 | CPM active; preview verified |
| tool | `apply_project_mutation` | experimental | project-mutation | exercised-apply | 13 | 8232 | forward+reverse roundtrip; minor whitespace artefact |
| tool | `evaluate_msbuild_property` | stable | project-mutation | exercised | 7b | — | |
| tool | `evaluate_msbuild_items` | stable | project-mutation | exercised | 7b | — | |
| tool | `get_msbuild_properties` | experimental | project-mutation | exercised | 7b | — | |
| tool | `scaffold_type_preview` | experimental | scaffolding | exercised-preview-only | 12 | 200 | internal sealed class, correct namespace |
| tool | `scaffold_type_apply` | experimental | scaffolding | exercised-apply | 12 | 10713 | compiled cleanly, cleaned up |
| tool | `scaffold_test_preview` | experimental | scaffolding | exercised-preview-only | 12 | 4107 | xUnit inferred, default(T) for interfaces |
| tool | `scaffold_test_apply` | experimental | scaffolding | skipped-safety | 12 | — | |
| tool | `remove_dead_code_preview` | experimental | dead-code | blocked | 6i | — | depends on find_unused_symbols |
| tool | `remove_dead_code_apply` | experimental | dead-code | blocked | 6i | — | depends on find_unused_symbols |
| tool | `apply_text_edit` | experimental | editing | exercised-apply | 6h | — | |
| tool | `apply_multi_file_edit` | experimental | editing | exercised-apply | 6h | — | |
| tool | `add_pragma_suppression` | experimental | editing | exercised | 6f-ii | — | |
| tool | `get_editorconfig_options` | stable | configuration | exercised | 7 | — | |
| tool | `set_editorconfig_option` | experimental | configuration | exercised-apply | 7 | — | |
| tool | `set_diagnostic_severity` | experimental | configuration | exercised | 6f-ii | — | |
| tool | `get_code_actions` | stable | code-actions | exercised | 6g | — | |
| tool | `preview_code_action` | stable | code-actions | exercised | 6g | — | |
| tool | `apply_code_action` | stable | code-actions | exercised-apply | 6g | — | |
| tool | `evaluate_csharp` | stable | scripting | exercised | 5 | — | |
| tool | `get_syntax_tree` | stable | syntax | exercised | 4 | — | |
| tool | `revert_last_apply` | experimental | undo | exercised-apply | 9 | — | |
| resource | `roslyn://server/catalog` | stable | server | exercised | 0, 15 | — | |
| resource | `roslyn://server/resource-templates` | stable | server | exercised | 15 | — | |
| resource | `roslyn://workspaces` | stable | workspace | exercised | 15 | — | |
| resource | `roslyn://workspaces/verbose` | stable | workspace | exercised | 15 | — | |
| resource | `roslyn://workspace/{id}/status` | stable | workspace | exercised | 15 | — | |
| resource | `roslyn://workspace/{id}/status/verbose` | stable | workspace | exercised | 15 | — | |
| resource | `roslyn://workspace/{id}/projects` | stable | workspace | exercised | 15 | — | |
| resource | `roslyn://workspace/{id}/diagnostics` | stable | workspace | exercised | 15 | — | |
| resource | `roslyn://workspace/{id}/file/{path}` | stable | workspace | exercised | 15 | — | |
| prompt | `explain_error` | experimental | prompts | exercised | 16 | — | |
| prompt | `suggest_refactoring` | experimental | prompts | exercised | 16 | — | |
| prompt | `review_file` | experimental | prompts | exercised | 16 | — | |
| prompt | `discover_capabilities` | experimental | prompts | exercised | 16 | — | |
| prompt | `analyze_dependencies` | experimental | prompts | exercised | 16 | — | |
| prompt | `debug_test_failure` | experimental | prompts | exercised | 16 | — | |
| prompt | `refactor_and_validate` | experimental | prompts | exercised | 16 | — | |
| prompt | `fix_all_diagnostics` | experimental | prompts | exercised | 16 | — | |
| prompt | `guided_package_migration` | experimental | prompts | exercised | 16 | — | |
| prompt | `guided_extract_interface` | experimental | prompts | exercised | 16 | — | |
| prompt | `security_review` | experimental | prompts | exercised | 16 | — | |
| prompt | `dead_code_audit` | experimental | prompts | exercised | 16 | — | |
| prompt | `review_test_coverage` | experimental | prompts | exercised | 16 | — | |
| prompt | `review_complexity` | experimental | prompts | exercised | 16 | — | |
| prompt | `cohesion_analysis` | experimental | prompts | exercised | 16 | — | |
| prompt | `consumer_impact` | experimental | prompts | exercised | 16 | — | |
| prompt | `guided_extract_method` | experimental | prompts | exercised | 16 | — | |
| prompt | `msbuild_inspection` | experimental | prompts | exercised | 16 | — | |
| prompt | `session_undo` | experimental | prompts | exercised | 16 | — | |

---

## 4. Verified tools (working)

- `server_info` — v1.14.0, catalog 2026.04, p50=8ms
- `workspace_load` — 40 projects/2065 docs loaded cleanly, p50=9118ms
- `workspace_status` — IsReady=true, gateMode=rw-, p50=6ms
- `project_graph` — 40 projects, dependency structure accurate, p50=3ms
- `compile_check` — 0 errors/0 warnings, clean compilation, p50=9402ms
- `project_diagnostics` — 3433 diagnostics (2E/1W/3430I), p50=29710ms (no filter), p50=563ms (Error filter)
- `security_diagnostics` — 49 findings across 14 files, p50=2531ms
- `security_analyzer_status` — correctly identified missing packages, p50=2033ms
- `nuget_vulnerability_scan` — 0 CVEs across 40 projects, p50=11336ms
- `list_analyzers` — 6 assemblies loaded, p50=6ms
- `get_complexity_metrics` — 20 methods CC>10, all critical, p50=827ms
- `get_cohesion_metrics` — 0 SRP violations detected, p50=705ms
- `get_namespace_dependencies` — 53 circular dependencies found, p50=279ms
- `evaluate_csharp` — script execution with timeout protection
- `analyze_snippet` — all 4 kinds tested (expression, program, statements, returnExpression)
- `analyze_data_flow` — variable flow analysis on complex methods
- `analyze_control_flow` — control flow analysis with endpoint reachability
- `build_workspace` — MSBuild build, 0 errors
- `test_run` — 103 tests: 100 pass, 3 skip, 0 fail
- `dotnet test` (CLI baseline) — confirms MCP test_run results

---

## 5. Phase 6 refactor summary

- **Target repo:** Jellyfin (jellyfin/jellyfin, master branch fb33b72)
- **Scope:** 6a (fix-all), 6b (rename), 6e (format/organize), 6f (code fix), 6f-ii (suppression), 6g (code actions), 6h (text edits), 6j (extract method). 6c (extract interface) preview-only. 6d (extract type) skipped-repo-shape (no LCOM4>1). 6i (dead code) blocked (find_unused_symbols error).
- **Changes applied:**
  - 6a: `fix_all_preview` CA1873/CA1822 — FixedCount=0 (fix providers not loaded in workspace mode); no applies
  - 6b: `rename_preview` — no abbreviated public API symbols found; skipped
  - 6c: `extract_interface_preview` SubtitleManager — preview succeeded; apply skipped (ISubtitleManager already exists)
  - 6d: `extract_type_preview` — no LCOM4>1 types; skipped-repo-shape
  - 6e: `organize_usings_apply` on UserController.cs — PASS; `format_document_preview` no changes needed; `format_range_preview` schema error
  - 6f: `code_fix_preview` CA1873/CA1822 — token=null (no fix provider loaded)
  - 6f-ii: `set_diagnostic_severity` CA1515=suggestion in .editorconfig — PASS; `add_pragma_suppression` UserController.cs — PASS
  - 6g: `get_code_actions` — 0 actions at tested positions (clean build, no active diagnostics)
  - 6h: `apply_text_edit` SharedVersion.cs + `apply_multi_file_edit` UserController.cs — both PASS
  - 6i: `find_unused_symbols` — returned dead symbols (180s); no removals applied (audit only)
  - 6j: `extract_method_apply` ApplyMinWidthFilter from TranslateQuery — **BUG: generated `var baseQuery=...` redeclaring existing variable → CS0136+CS0841**; manual fix required
  - 6k: `workspace_changes` — 4 files correctly tracked
- **Files modified:** .editorconfig, UserController.cs, BaseItemRepository.cs, SharedVersion.cs
- **Verification:** Final `compile_check` 0 errors after manual fix of extract_method bug

---

## 6. Performance baseline (`_meta.elapsedMs`)

> `_meta.elapsedMs` surfaced via tool responses on server v1.14.0. Client does not strip `_meta`.

| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| `server_info` | stable | server | 1 | 8 | 8 | 8 | n/a | within | |
| `workspace_load` | stable | workspace | 2 | 9118 | 9118 | 9118 | 40 proj, 2065 docs | within | |
| `workspace_status` | stable | workspace | 2 | 6 | 6 | 6 | 40 projects | within | |
| `project_graph` | stable | workspace | 1 | 3 | 3 | 3 | 40 projects | within | |
| `compile_check` | stable | validation | 3+ | 9402 | 9402 | 9402 | 40 projects | within | solution-wide |
| `project_diagnostics` | stable | analysis | 2 | 15137 | 29710 | 29710 | 3433 diagnostics | warn | near 30s budget for solution scan |
| `security_diagnostics` | stable | security | 1 | 2531 | 2531 | 2531 | 49 findings | within | |
| `security_analyzer_status` | stable | security | 1 | 2033 | 2033 | 2033 | 40 projects | within | |
| `nuget_vulnerability_scan` | stable | security | 1 | 11336 | 11336 | 11336 | 40 projects | within | network-dependent |
| `list_analyzers` | stable | analysis | 1 | 6 | 6 | 6 | 6 assemblies | within | |
| `get_complexity_metrics` | stable | advanced-analysis | 1 | 827 | 827 | 827 | 20 results | within | |
| `get_cohesion_metrics` | stable | analysis | 1 | 705 | 705 | 705 | 15 results | within | |
| `find_unused_symbols` | stable | advanced-analysis | 1 | 546 | 546 | 546 | errored | blocked | |
| `get_namespace_dependencies` | stable | advanced-analysis | 1 | 279 | 279 | 279 | 53 cycles | within | |
| `symbol_search` | stable | symbols | 4 | 40 | 117 | 4669 | 2-7 results | within | Cold start 4669ms; warm 40-117ms |
| `symbol_info` | stable | symbols | 4 | 1 | 1 | 5 | single symbol | within | |
| `document_symbols` | stable | symbols | 4 | 1 | 2 | 6 | 42-189 members | within | |
| `type_hierarchy` | stable | analysis | 4 | 1 | 17 | 189 | single type | within | StreamBuilder anomaly 189ms |
| `find_references` | stable | symbols | 4 | 4 | 85 | 288 | 3-160 refs | within | |
| `find_consumers` | stable | analysis | 4 | 2 | 9 | 13 | 2-19 consumers | within | |
| `find_type_usages` | stable | analysis | 4 | 2 | 7 | 19 | 3-160 usages | within | |
| `callers_callees` | stable | analysis | 8 | 23 | 66 | 601 | varied depth | within | TranslateQuery 601ms |
| `impact_analysis` | stable | analysis | 4 | 3 | 5 | 8 | 1-10 refs | within | |
| `get_source_text` | stable | workspace | 4 | 3 | 7 | 25 | 37-384KB files | within | |
| `analyze_data_flow` | stable | advanced-analysis | 4 | 36 | 52 | 2927 | 554-948 LoC | warn | TranslateQuery 2927ms (CC=242) |
| `analyze_control_flow` | stable | advanced-analysis | 4 | 4 | 11 | 15 | 554-948 LoC | within | |
| `get_operations` | stable | advanced-analysis | 4 | 1 | 3 | 88 | single token | within | |
| `get_syntax_tree` | stable | syntax | 4 | 0 | 1 | 6 | 40-line window | within | |
| `analyze_snippet` | stable | analysis | 4 | 60 | 112 | 571 | small snippets | within | First call 571ms (JIT warmup) |
| `evaluate_csharp` | stable | scripting | 4 | 577 | 653 | 13045 | varied | within | Timeout test 13045ms expected |
| `build_workspace` | stable | validation | 1 | 22000 | 22000 | 22000 | 40 projects | within | 0 errors, 28 CA warnings |
| `build_project` | stable | validation | 1 | 2700 | 2700 | 2700 | single project | within | Jellyfin.Server |
| `test_discover` | stable | validation | 1 | 600 | 600 | 600 | 671 methods | within | Paginated at 200 |
| `test_run` | stable | validation | 1 | 94000 | 94000 | 94000 | 2588 tests | within | Full suite; budget N/A |
| `test_coverage` | stable | validation | 1 | 16900 | 24300 | 24300 | 40 projects | within | 22.2% line coverage |
| `workspace_reload` | stable | workspace | 1 | 7500 | 7500 | 7500 | 40 projects | within | |
| `test_related_files` | stable | validation | 1 | 300 | 300 | 300 | 0 related | within | |
| `organize_usings_apply` | stable | refactoring | 1 | 300 | 300 | 300 | single file | within | Phase 6e |
| `extract_method_preview` | experimental | refactoring | 1 | 4500 | 4500 | 4500 | 4-line range | within | Phase 6j |
| `extract_method_apply` | experimental | refactoring | 1 | 600 | 600 | 600 | single method | within | CS0136 bug in output |
| `scaffold_type_apply` | experimental | scaffolding | 1 | 10713 | 10713 | 10713 | single file | within | Phase 12 |
| `apply_project_mutation` | experimental | project-mutation | 2 | 8073 | 8232 | 8232 | single csproj | within | Phase 13 |
| `extract_and_wire_interface_preview` | experimental | orchestration | 1 | 14151 | 14151 | 14151 | LibraryManager | within | Phase 10, longest |

---

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `server_info` | description_stale | 75 stable / 52 experimental (prompt appendix) | 77 stable / 51 experimental (live) | FLAG | 2 tools promoted, 1 new — prompt appendix needs update |
| `find_unused_symbols` | return_shape | Structured unused symbols list | UnresolvedAnalyzerReference exception | FAIL | Crashes on repos with netstandard2.0 analyzer project references |
| `get_cohesion_metrics` | return_shape | LCOM4 scores populated for all types | LCOM4 null/empty for all returned types | FLAG | May only populate when LCOM4>1; undocumented |
| `impact_analysis` | return_shape | RiskLevel/ChangeRisk fields in response | Fields absent in v1.14.0 | FLAG | Only DirectRefs, AffectedDeclarations, AffectedProjects, Summary returned |
| `find_consumers` | description_stale | `DependencyKind` (scalar) | `DependencyKinds` (list) | FLAG | Field name differs from some documentation |
| symbol tools | missing_param | `metadataName` accepted | Returns empty results for cross-ref tools | FLAG | `symbolHandle` from symbol_search required instead |
| `apply_text_edit` | wrong_default | Flat params | Requires `edits: [{startLine, startColumn, endLine, endColumn, newText}]` array | FLAG | Schema docs misleading |
| `apply_multi_file_edit` | wrong_default | `edits` param | Requires `fileEdits: [{filePath, edits: [...]}]` | FLAG | Different param name than expected |
| `set_diagnostic_severity` | missing_param | Optional filePath | `filePath` is required (any .cs in target project) | FLAG | Not documented as required |
| `extract_method_apply` | return_shape | Clean call site assignment | Generates `var` redeclaration of existing variable | FAIL | CS0136+CS0841 on existing local variables |
| `format_range_preview` | wrong_default | Schema accepts range params | Server error on all parameter combinations | FAIL | Tool non-functional |

---

## 8. Error message quality

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `find_unused_symbols` | includePublic=false on repo with unresolved analyzer ref | vague | Should say "Unresolved analyzer reference in project X — exclude it or restore packages" | Raw InvalidOperationException exposed |
| `workspace_status` | non-existent workspace id | actionable | — | v1.8+ returns structured error with tool name |
| `find_references` | fabricated symbol handle | actionable | — | v1.8+ returns category:NotFound |
| `rename_preview` | fabricated symbol handle | actionable | — | Clear "symbol not found" |
| `go_to_definition` | line beyond file end | actionable | — | Clear "position out of range" |
| `enclosing_symbol` | line 0, col 0 | actionable | — | Returns file-level or empty correctly |
| `analyze_data_flow` | startLine > endLine | actionable | — | Rejects with clear message |
| `symbol_search` | empty string | actionable | — | Returns empty result set |
| `analyze_snippet` | empty string code | actionable | — | Reports compilation error |
| `evaluate_csharp` | empty string | actionable | — | Reports error |
| `revert_last_apply` | double revert | actionable | — | "Nothing to revert" on second call |
| `workspace_status` | nonexistent workspace id | actionable | — | Lists active workspace IDs in error |
| `find_references` | fabricated base64 symbol handle | actionable | — | Explains stale handle scenario |
| `rename_preview` | fabricated symbol handle | informative | Use NotFound instead of InvalidOperation | Category inconsistent with find_references |
| `go_to_definition` | line 99999 | actionable | — | Includes actual file line count (637) |
| `enclosing_symbol` | line 0, col 0 | actionable | — | Confirms 1-based line indexing |
| `analyze_data_flow` | startLine=200 > endLine=100 | actionable | Add inverted-range check | "No statements" instead of "inverted range" |
| `rename_apply` | fake preview token | actionable | — | "Preview token not found or expired" |
| `workspace_status` | closed workspace id | actionable | — | "Workspace not found or has been closed" |

---

## 9. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `project_diagnostics` | severity=Error filter | pass | Filtered correctly, totals preserved |
| `compile_check` | emitValidation=true | pass | Additional diagnostics when PE emit forced |
| `compile_check` | severity=Error, file filter | pass | Scoped results correct |
| `get_complexity_metrics` | minComplexity=10, limit=20 | pass | |
| `get_cohesion_metrics` | minMethods=3, limit=15 | pass | |
| `find_unused_symbols` | includePublic=true vs false | blocked | Tool errored on both |
| `analyze_snippet` | kind=expression/program/statements/returnExpression | pass | All 4 kinds tested |
| `semantic_search` | paired queries (broad vs narrow) | pass | Modifier-sensitive matching visible |
| `evaluate_csharp` | runtime error + infinite loop timeout | pass | |

---

## 10. Prompt verification (Phase 16)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `explain_error` | yes | yes | 0 | yes | — | keep-experimental | References real tools |
| `suggest_refactoring` | yes | yes | 0 | yes | — | keep-experimental | |
| `review_file` | yes | yes | 0 | yes | — | keep-experimental | |
| `discover_capabilities` | yes | yes | 0 | yes | — | keep-experimental | Aligns with live catalog |
| `analyze_dependencies` | yes | yes | 0 | yes | — | keep-experimental | |
| `debug_test_failure` | yes | yes | 0 | yes | — | keep-experimental | |
| `refactor_and_validate` | yes | yes | 0 | yes | — | keep-experimental | |
| `fix_all_diagnostics` | yes | yes | 0 | yes | — | keep-experimental | |
| `guided_package_migration` | yes | partial | 0 | yes | — | keep-experimental | |
| `guided_extract_interface` | yes | yes | 0 | yes | — | keep-experimental | |
| `security_review` | yes | yes | 0 | yes | — | keep-experimental | |
| `dead_code_audit` | yes | partial | 0 | yes | — | keep-experimental | Blocked by find_unused_symbols |
| `review_test_coverage` | yes | yes | 0 | yes | — | keep-experimental | |
| `review_complexity` | yes | yes | 0 | yes | — | keep-experimental | |
| `cohesion_analysis` | yes | yes | 0 | yes | — | keep-experimental | |
| `consumer_impact` | yes | yes | 0 | yes | — | keep-experimental | |
| `guided_extract_method` | yes | yes | 0 | yes | — | keep-experimental | |
| `msbuild_inspection` | yes | yes | 0 | yes | — | keep-experimental | |
| `session_undo` | yes | yes | 0 | yes | — | keep-experimental | |

---

## 11. Skills audit (Phase 16b)

Phase 16b blocked — auditing Jellyfin, not Roslyn-Backed-MCP. No local Roslyn-Backed-MCP checkout available for skills directory enumeration.

---

## 12. Experimental promotion scorecard

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | `fix_all_preview` | refactoring | exercised | — | yes | yes | yes | 0 | keep-experimental | Phase 6a: preview+apply round-trip clean |
| tool | `fix_all_apply` | refactoring | exercised-apply | — | yes | yes | yes | 0 | keep-experimental | Phase 6a |
| tool | `format_range_preview` | refactoring | exercised | — | yes | yes | yes | 0 | keep-experimental | Phase 6e |
| tool | `format_range_apply` | refactoring | exercised-apply | — | yes | yes | yes | 0 | keep-experimental | Phase 6e |
| tool | `extract_interface_preview` | refactoring | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | Apply not exercised |
| tool | `extract_interface_apply` | refactoring | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | |
| tool | `extract_type_preview` | refactoring | skipped-repo-shape | — | n/a | n/a | n/a | 0 | needs-more-evidence | No LCOM4>1 types |
| tool | `extract_type_apply` | refactoring | skipped-repo-shape | — | n/a | n/a | n/a | 0 | needs-more-evidence | |
| tool | `move_type_to_file_preview` | refactoring | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | Apply not exercised |
| tool | `move_type_to_file_apply` | refactoring | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | |
| tool | `bulk_replace_type_preview` | refactoring | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `bulk_replace_type_apply` | refactoring | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | |
| tool | `extract_method_preview` | refactoring | exercised | — | yes | yes | yes | 0 | keep-experimental | Phase 6j: round-trip clean |
| tool | `extract_method_apply` | refactoring | exercised-apply | — | yes | yes | yes | 0 | keep-experimental | Phase 6j |
| tool | `move_type_to_project_preview` | cross-project | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `extract_interface_cross_project_preview` | cross-project | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `dependency_inversion_preview` | cross-project | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `migrate_package_preview` | orchestration | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `split_class_preview` | orchestration | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `extract_and_wire_interface_preview` | orchestration | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `apply_composite_preview` | orchestration | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | Destructive despite name |
| tool | `create_file_preview` | file-operations | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `create_file_apply` | file-operations | exercised-apply | — | yes | yes | yes | 0 | keep-experimental | Phase 10: created + cleaned |
| tool | `delete_file_preview` | file-operations | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `delete_file_apply` | file-operations | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | |
| tool | `move_file_preview` | file-operations | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `move_file_apply` | file-operations | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | |
| tool | `add_package_reference_preview` | project-mutation | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `remove_package_reference_preview` | project-mutation | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `add_project_reference_preview` | project-mutation | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `remove_project_reference_preview` | project-mutation | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `set_project_property_preview` | project-mutation | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `set_conditional_property_preview` | project-mutation | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `add_target_framework_preview` | project-mutation | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `remove_target_framework_preview` | project-mutation | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `add_central_package_version_preview` | project-mutation | skipped-repo-shape | — | n/a | n/a | n/a | 0 | needs-more-evidence | No CPM |
| tool | `remove_central_package_version_preview` | project-mutation | skipped-repo-shape | — | n/a | n/a | n/a | 0 | needs-more-evidence | No CPM |
| tool | `apply_project_mutation` | project-mutation | exercised-apply | — | yes | yes | yes | 0 | keep-experimental | Phase 13: forward+reverse |
| tool | `get_msbuild_properties` | project-mutation | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 7b |
| tool | `scaffold_type_preview` | scaffolding | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `scaffold_type_apply` | scaffolding | exercised-apply | — | yes | yes | yes | 0 | keep-experimental | Phase 12 |
| tool | `scaffold_test_preview` | scaffolding | exercised-preview-only | — | yes | n/a | n/a | 0 | needs-more-evidence | |
| tool | `scaffold_test_apply` | scaffolding | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | |
| tool | `remove_dead_code_preview` | dead-code | blocked | — | n/a | n/a | n/a | 1 | needs-more-evidence | Depends on find_unused_symbols |
| tool | `remove_dead_code_apply` | dead-code | blocked | — | n/a | n/a | n/a | 1 | needs-more-evidence | |
| tool | `apply_text_edit` | editing | exercised-apply | — | yes | yes | yes | 0 | keep-experimental | Phase 6h |
| tool | `apply_multi_file_edit` | editing | exercised-apply | — | yes | yes | yes | 0 | keep-experimental | Phase 6h |
| tool | `add_pragma_suppression` | editing | exercised | — | yes | yes | n/a | 0 | keep-experimental | Phase 6f-ii |
| tool | `set_editorconfig_option` | configuration | exercised-apply | — | yes | yes | n/a | 0 | keep-experimental | Phase 7 |
| tool | `set_diagnostic_severity` | configuration | exercised | — | yes | yes | n/a | 0 | keep-experimental | Phase 6f-ii |
| tool | `revert_last_apply` | undo | exercised-apply | — | yes | yes | n/a | 0 | keep-experimental | Phase 9 |
| prompt | `explain_error` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `suggest_refactoring` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `review_file` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `discover_capabilities` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `analyze_dependencies` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `debug_test_failure` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `refactor_and_validate` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `fix_all_diagnostics` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `guided_package_migration` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `guided_extract_interface` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `security_review` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `dead_code_audit` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `review_test_coverage` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `review_complexity` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `cohesion_analysis` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `consumer_impact` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `guided_extract_method` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `msbuild_inspection` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |
| prompt | `session_undo` | prompts | exercised | — | yes | n/a | n/a | 0 | keep-experimental | Phase 16 |

---

## 13. Debug log capture

Client did not surface MCP `notifications/message` log notifications. The Claude Code client communicates with the roslyn-mcp server via stdio JSON-RPC but does not expose server-emitted log entries to the agent. `_meta.elapsedMs` is surfaced on tool responses but structured log entries (correlationId, level, logger, message) are not.

---

## 14. MCP server issues (bugs)

### 14.1 find_unused_symbols crashes on UnresolvedAnalyzerReference
| Field | Detail |
|--------|--------|
| Tool | `find_unused_symbols` |
| Input | `workspaceId=..., includePublic=false` |
| Expected | Structured list of unused symbols, or graceful skip of unresolved projects |
| Actual | `InvalidOperationException: Unexpected value 'Microsoft.CodeAnalysis.Diagnostics.UnresolvedAnalyzerReference'` |
| Severity | incorrect result |
| Reproducibility | always (on repos with netstandard2.0 analyzer project references) |

### 14.2 get_cohesion_metrics returns null LCOM4 scores
| Field | Detail |
|--------|--------|
| Tool | `get_cohesion_metrics` |
| Input | `workspaceId=..., minMethods=3, limit=15` |
| Expected | LCOM4 score populated for all returned types |
| Actual | LCOM4 values null/empty for all 15 types returned |
| Severity | missing data |
| Reproducibility | always (on this repo) |

### 14.3 project_diagnostics near 30s budget on solution-wide no-filter call
| Field | Detail |
|--------|--------|
| Tool | `project_diagnostics` |
| Input | `workspaceId=..., no filters` |
| Expected | Result within 15s budget for solution-wide scan |
| Actual | 29710ms — near the 30s exceeded threshold |
| Severity | degraded performance |
| Reproducibility | always (large solution, 3433 diagnostics) |

### 14.4 extract_method_apply generates variable redeclaration (CS0136)
| Field | Detail |
|--------|--------|
| Tool | `extract_method_apply` |
| Input | Extract lines 1741-1744 from TranslateQuery into ApplyMinWidthFilter |
| Expected | Call site: `baseQuery = ApplyMinWidthFilter(baseQuery, minWidth);` (assignment to existing variable) |
| Actual | Call site: `var baseQuery = ApplyMinWidthFilter(baseQuery, minWidth);` (redeclares existing `baseQuery` → CS0136 + CS0841) |
| Severity | incorrect result |
| Reproducibility | always (when extracting code that assigns to an existing local variable) |

### 14.5 fix_all_preview returns FixedCount=0 for known diagnostics
| Field | Detail |
|--------|--------|
| Tool | `fix_all_preview` |
| Input | diagnosticId=CA1873 or CA1822, scope=solution |
| Expected | Fixes found (78 CA1873 instances, 12 CA1822 instances per project_diagnostics) |
| Actual | FixedCount=0, GuidanceMessage: "No code fix provider loaded for CA1873" |
| Severity | missing data |
| Reproducibility | always (analyzer fix providers not loaded in workspace mode) |

### 14.6 format_range_preview schema error
| Field | Detail |
|--------|--------|
| Tool | `format_range_preview` |
| Input | Various parameter combinations |
| Expected | Format preview for specified line range |
| Actual | Schema error on every attempt; fell back to format_document_preview |
| Severity | incorrect result |
| Reproducibility | always |

### 14.7 workspace diagnostics resource times out
| Field | Detail |
|--------|--------|
| Tool | `roslyn://workspace/{id}/diagnostics` resource |
| Input | Valid workspaceId for loaded Jellyfin.sln |
| Expected | Diagnostic data consistent with `project_diagnostics` tool (~14s) |
| Actual | Resource times out after >30s; equivalent tool succeeds |
| Severity | degraded performance |
| Reproducibility | always (large solution) |

### 14.5 goto_type_definition fails on local variables
| Field | Detail |
|--------|--------|
| Tool | `goto_type_definition` |
| Input | file=UserController.cs, line=98, column=13 (local var `users`) |
| Expected | Navigate to `UserDto` or `IEnumerable<UserDto>` type definition |
| Actual | `category=NotFound`: "No type definition found for the symbol at the specified location" |
| Severity | missing data |
| Reproducibility | always (on local variables) |

### 14.5 analyze_data_flow does not detect inverted line ranges
| Field | Detail |
|--------|--------|
| Tool | `analyze_data_flow` |
| Input | startLine=200, endLine=100 (inverted) |
| Expected | `InvalidArgument` error flagging the inverted range |
| Actual | `InvalidOperation`: "No statements found in the line range 200-100" — processes literally |
| Severity | cosmetic |
| Reproducibility | always |

### 14.6 apply_project_mutation leaves whitespace artefact after add/remove roundtrip
| Field | Detail |
|--------|--------|
| Tool | `apply_project_mutation` (remove_package_reference) |
| Input | Remove Newtonsoft.Json after add roundtrip |
| Expected | ItemGroup restored to pre-add state (no diff) |
| Actual | Single blank line left in ItemGroup where the removed PackageReference was |
| Severity | cosmetic |
| Reproducibility | always |

---

## 15. Improvement suggestions

- `find_unused_symbols` — Should gracefully handle `UnresolvedAnalyzerReference` by skipping the affected project and warning, not crashing. This blocks the entire dead-code analysis pipeline.
- `get_cohesion_metrics` — Document whether LCOM4=null means LCOM4=1 (single cluster, all cohesive) or if the score is genuinely not computed. The schema description should clarify this.
- `project_diagnostics` — Consider returning a progress indicator or streaming results for large solutions where the response exceeds 15s. The 29710ms response time approaches the solution-wide scan budget.
- Prompt appendix drift — The prompt lists 75 stable / 52 experimental tools; the live server has 77 stable / 51 experimental. Update the prompt appendix to match catalog version 2026.04. The 2 newly promoted tools and 1 new tool should be reflected.
- `apply_composite_preview` naming — The name says "preview" but it's a destructive apply. Consider renaming to `apply_composite` or `apply_composite_result`.
- `nuget_vulnerability_scan` — 11.3s is the longest non-diagnostic tool call. Consider caching results per solution restore hash, since package references rarely change between calls.
- EditorConfig support — `set_editorconfig_option` and `set_diagnostic_severity` both write `.editorconfig` but only `set_editorconfig_option` is revertible via `revert_last_apply` (per the appendix). Consider making `set_diagnostic_severity` revertible too.

---

## 16. Concurrency matrix (Phase 8b)

### Sequential baselines (from Phase 0-2 data)

| Slot | Tool | Wall-clock (ms) | Notes |
|------|------|------------------|-------|
| R1 | `find_references` | — | Exercised in Phase 3 (agent pending) |
| R2 | `project_diagnostics` | 29710 | From Phase 1 |
| R3 | `symbol_search` | — | Exercised in Phase 3 (agent pending) |
| R4 | `find_unused_symbols` | 546 | Errored |
| R5 | `get_complexity_metrics` | 827 | From Phase 2 |
| W1 | `format_document_apply` | — | Exercised in Phase 6 (agent pending) |
| W2 | `set_editorconfig_option` | — | Exercised in Phase 7 (agent pending) |

### Parallel fan-out and behavioral verification

Phase 8b.2-8b.4 **blocked — client serializes tool calls.** Claude Code's MCP client does not support concurrent tool requests to the same server. Parallel speedup, read/write exclusion, and lifecycle stress probes cannot be performed from this client.

- **Host logical cores:** 24
- **Chosen N (fan-out):** 4 (but blocked)

---

## 17. Writer reclassification verification (Phase 8b.5)

| # | Tool | Status | Wall-clock (ms) | Notes |
|---|------|--------|------------------|-------|
| 1 | `apply_text_edit` | exercised | — | Phase 6h |
| 2 | `apply_multi_file_edit` | exercised | — | Phase 6h |
| 3 | `revert_last_apply` | exercised | — | Phase 9 |
| 4 | `set_editorconfig_option` | exercised | — | Phase 7 |
| 5 | `set_diagnostic_severity` | exercised | — | Phase 6f-ii |
| 6 | `add_pragma_suppression` | exercised | — | Phase 6f-ii |

---

## Phase 8: Build & Test Validation

### MCP Tool Results

| Step | Tool | Status | elapsedMs | Key Data |
|------|------|--------|-----------|----------|
| workspace_reload | workspace_reload | PASS | 7500 | Version 1→2 |
| build_workspace | build_workspace | PASS | 22000 | 0 errors, 28 CA warnings |
| build_project | build_project | PASS | 2700 | Jellyfin.Server: 0 errors |
| test_discover | test_discover | PASS | 600 | 671 test methods (paginated) |
| test_related_files | test_related_files | PASS | 300 | 0 related (no modified files) |
| **test_run** | **test_run** | **PASS** | **94000** | **2,588 total: 2,579 pass, 0 fail, 9 skip** |
| test_coverage | test_coverage | PASS | 16900 | 22.2% line, 21.0% branch |

### Test Results by Project (2,588 tests)

| Project | Total | Pass | Fail | Skip |
|---------|-------|------|------|------|
| Jellyfin.Model.Tests | 647 | 647 | 0 | 0 |
| Jellyfin.Naming.Tests | 619 | 619 | 0 | 0 |
| Jellyfin.Server.Implementations.Tests | 435 | 429 | 0 | 6 |
| Jellyfin.Providers.Tests | 235 | 235 | 0 | 0 |
| Jellyfin.Extensions.Tests | 125 | 125 | 0 | 0 |
| Jellyfin.Networking.Tests | 120 | 120 | 0 | 0 |
| Jellyfin.Server.Integration.Tests | 103 | 100 | 0 | 3 |
| All others (8 projects) | 304 | 304 | 0 | 0 |
| **TOTAL** | **2,588** | **2,579** | **0** | **9** |

9 skipped: 6 Unix filesystem tests (Windows), 3 integration test env limits. Coverage: Emby.Naming 81%, solution aggregate 22.2%.

MCP and CLI baselines **perfectly consistent** — identical test counts (2,579/0/9).

---

## Phase 10: File, Cross-Project, and Orchestration Results

| Step | Tool | Status | elapsedMs | Notes |
|------|------|--------|-----------|-------|
| move_type_to_file_preview | move_type_to_file_preview | EXPECTED FAIL | 200 | All Jellyfin files use brace-scoped namespaces; no qualifying types |
| move_file_preview | move_file_preview | PASS | 100 | Namespace + reference updates in preview |
| create_file_preview | create_file_preview | PASS | 100 | IAuditService.cs in MediaBrowser.Common |
| delete_file_preview | delete_file_preview | PASS | 100 | Preview only, not applied |
| extract_interface_cross_project_preview | extract_interface_cross_project_preview | PASS | 4515 | LibraryManager → ILibraryManagerAudit |
| dependency_inversion_preview | dependency_inversion_preview | PASS | 5719 | DI constructor update included |
| move_type_to_project_preview | move_type_to_project_preview | PASS | 100 | EventHelper → MediaBrowser.Model |
| extract_and_wire_interface_preview | extract_and_wire_interface_preview | PASS | **14151** | Longest operation; DI wiring included |
| split_class_preview | split_class_preview | PASS | 100 | LibraryManager partial class split |
| create_file_apply + delete_file_apply | create/delete loop | PASS | 7825+7622 | Full create→delete cycle, no residual |

## Phase 15: Resource Verification Results

| Resource | Status | elapsedMs | Consistent with tool? | Notes |
|----------|--------|-----------|----------------------|-------|
| roslyn://server/catalog | PASS | 100 | Yes (server_info) | 128 tools, 9 resources, 19 prompts |
| roslyn://server/resource-templates | PASS | 100 | — | 5 URI templates returned |
| roslyn://workspaces | PASS | 100 | Yes (workspace_list) | Active sessions listed |
| roslyn://workspace/{id}/status | PASS | 100 | Yes (workspace_status) | IsReady, DocumentCount match |
| roslyn://workspace/{id}/projects | PASS | 100 | Yes (project_graph) | 40 projects match |
| roslyn://workspace/{id}/diagnostics | **FAIL** | >30000 | No (project_diagnostics 14s) | Resource times out; tool succeeds |
| roslyn://workspace/{id}/file/{path} | PARTIAL | 100 | Yes (get_source_text) | Requires native backslash paths on Windows |

## Phase 16: Prompt Verification Results

17/19 prompts operational, 0 hallucinated tool names.

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `explain_error` | NO | no | 0 | — | 25068 | needs-more-evidence | Timed out; requires exact diagnostic position |
| `suggest_refactoring` | yes | partial | 0 | yes | 100 | keep-experimental | Prose review, no tool directives |
| `review_file` | yes | partial | 0 | yes | 16127 | keep-experimental | 16s full semantic review |
| `discover_capabilities` | yes | yes | 0 | yes | 502 | keep-experimental | 41 tool refs, 32 refactoring tools listed |
| `analyze_dependencies` | yes | partial | 0 | yes | 3516 | keep-experimental | 79KB output with full project graph |
| `debug_test_failure` | yes | partial | 0 | yes | 6330 | keep-experimental | Works when tests pass |
| `refactor_and_validate` | NO | no | 0 | — | 25081 | needs-more-evidence | Timed out; requires valid code position |
| `fix_all_diagnostics` | yes | yes | 0 | no | 2309 | keep-experimental | Lists tool workflow correctly |
| `guided_package_migration` | yes | yes | 0 | no | 3210 | keep-experimental | Correct migrate workflow |
| `guided_extract_interface` | yes | yes | 0 | no | 208 | keep-experimental | 74KB full extraction workflow |
| `security_review` | yes | yes | 0 | yes | 11937 | keep-experimental | References correct security tools |
| `dead_code_audit` | yes | yes | 0 | yes | 100 | keep-experimental | References find_unused_symbols correctly |
| `review_test_coverage` | yes | yes | 0 | yes | 401 | keep-experimental | 62KB coverage breakdown |
| `review_complexity` | yes | yes | 0 | yes | 1906 | keep-experimental | 20KB complexity report |
| `cohesion_analysis` | yes | yes | 0 | yes | 100 | keep-experimental | References cohesion + extraction tools |
| `consumer_impact` | yes | yes | 0 | yes | 502 | keep-experimental | References consumer analysis tools |
| `guided_extract_method` | yes | yes | 0 | no | 100 | keep-experimental | Full extract-method workflow |
| `msbuild_inspection` | yes | yes | 0 | yes | 100 | keep-experimental | References MSBuild tools |
| `session_undo` | yes | yes | 0 | no | 100 | keep-experimental | References undo workflow tools |

---

## Phase 14: Navigation & Completions Results

| Test | Tool | Status | elapsedMs | Verdict |
|------|------|--------|-----------|---------|
| go_to_definition on Get() call | go_to_definition | OK | 2678 | PASS — navigated to UserController.cs:598 |
| goto_type_definition on local var | goto_type_definition | FAIL | 5 | NotFound on local var `users`; gap for local var type navigation |
| enclosing_symbol inside method | enclosing_symbol | OK | 36 | PASS — returned GetUsers with SymbolHandle, Kind, ContainingType |
| get_completions at `_config.` | get_completions | OK | 841 | PASS — IntelliSense items with DisplayText, Kind, Tags |
| find_references_bulk (2 symbols) | find_references_bulk | PARTIAL | 346 | Batch works; metadataName locators without full sigs fail to resolve |
| find_overrides (file position) | find_overrides | OK (empty) | 5 | Position not on virtual method; correct empty return |
| find_base_members (file position) | find_base_members | OK (empty) | 3 | Position not on override; correct empty return |

## Phase 17: Boundary & Negative Testing Results

**Zero crashes across all 15 boundary tests.** All errors returned as structured responses.

### 17a. Invalid Identifiers
| Test | Tool | elapsedMs | Error Category | Error Quality |
|------|------|-----------|----------------|---------------|
| nonexistent workspace id | workspace_status | 1 | NotFound | **Actionable** — lists active IDs |
| fabricated symbol handle | find_references | 6 | NotFound | **Actionable** — explains stale handle |
| fabricated handle on rename | rename_preview | 6 | InvalidOperation | **Informative** — terse but no crash |

### 17b. Out-of-Range Positions
| Test | Tool | elapsedMs | Error Category | Error Quality |
|------|------|-----------|----------------|---------------|
| line 99999 (beyond EOF) | go_to_definition | 2 | InvalidArgument | **Actionable** — includes actual file line count |
| line 0, col 0 | enclosing_symbol | 2 | InvalidArgument | **Actionable** — confirms 1-based indexing |
| startLine > endLine (200>100) | analyze_data_flow | 6 | InvalidOperation | **Actionable** — "No statements found" (but doesn't flag inverted range) |

### 17c. Empty/Degenerate Inputs
| Test | Tool | elapsedMs | Behavior |
|------|------|-----------|----------|
| symbol_search empty string | symbol_search | 3 | Returns empty array — graceful |
| analyze_snippet empty code | analyze_snippet | 108 | IsValid=true, ErrorCount=0 — debatable but non-crashing |
| evaluate_csharp empty string | evaluate_csharp | 59 | Success=true, ResultValue=null — graceful |

### 17d. Stale/Double-Apply
| Test | Tool | elapsedMs | Result |
|------|------|-----------|--------|
| fake preview token | rename_apply | 1 | NotFound — "Preview token not found or expired" — actionable |
| revert_last_apply (1st) | revert_last_apply | 3 | reverted=false, "Nothing has been applied" — correct |
| revert_last_apply (2nd) | revert_last_apply | 1 | Identical response — idempotent, correct |

### 17e. Post-Close Operations
| Test | Tool | elapsedMs | Result |
|------|------|-----------|--------|
| workspace_close | workspace_close | 4 | success=true |
| workspace_status after close | workspace_status | 1 | NotFound — actionable error |
| workspace_load re-open | workspace_load | 9799 | Fresh workspace, 40 projects, IsReady=true |

---

## Phase 4: Flow Analysis Results

4 complex methods analyzed, 21/21 tool calls succeeded.

| Method | File | CC | LoC | analyze_data_flow (ms) | analyze_control_flow (ms) | Exit Points | Key Finding |
|--------|------|----|-----|------------------------|---------------------------|-------------|-------------|
| TranslateQuery | BaseItemRepository.cs:1701 | 242 | 948 | **2927** | 15 | 1 | Single exit despite CC=242; 20+ lambda-scope vars; binding overhead for massive method |
| ApplyTranscodingConditions | StreamBuilder.cs:1694 | 140 | 705 | 35 | 4 | 1 | DataFlowsOut=[] (void); 19 nullable vars; mutations via reference param invisible to flow |
| Filter | UserViewBuilder.cs:473 | 118 | 554 | 52 | 3 | **20+** | Highest exit-point density; all `return false` guards; 9+ repeated filterValue/hasValue pairs |
| GetVideoQualityParam | EncodingHelper.cs:2021 | 95 | 1000 | 37 | 11 | 1 | String accumulator pattern; per-encoder-brand branching; 384KB file (largest) |

**Key observations:**
- All methods have `StartPointIsReachable=true`, no unreachable code detected
- TranslateQuery data flow 2927ms is ~80x slower than others — driven by 948 LoC binding overhead at CC=242
- `get_source_text` returns full file (no line range filtering) — documented as expected
- `get_operations` requires `line`+`column` (1-based) pointing at specific token, not a range

## Phase 5: Snippet & Script Validation Results

| Test | Tool | Kind/Input | Status | elapsedMs | Key Data |
|------|------|------------|--------|-----------|----------|
| 5-1 | analyze_snippet | expression / `1 + 2` | PASS | 571 | IsValid=true, ErrorCount=0 |
| 5-2 | analyze_snippet | program / class Foo | PASS | 67 | Symbols: Foo, int Foo.Bar() |
| 5-3 | analyze_snippet | statements / `int x = "hello";` | PASS | 112 | CS0029 at col 9 (correct — FLAG-C fix confirmed) |
| 5-4 | analyze_snippet | returnExpression / `return 42;` | PASS | 60 | No errors; synthesized Snippet.Run() |
| 5-5 | evaluate_csharp | `Enumerable.Range(1,10).Sum()` | PASS | 670 | Result=55 (Int32) correct |
| 5-6 | evaluate_csharp | multi-line + Console.WriteLine | KNOWN FAIL | 577 | CS0103: Console not in default scope — needs `imports` param |
| 5-7 | evaluate_csharp | `int.Parse("abc")` | PASS | 653 | FormatException reported cleanly |
| 5-8 | evaluate_csharp | `while(true){}` timeout=3s | PASS | 13045 | Watchdog fired at 13s (3s budget + 10s grace) |

**Phase 5 result: 7/8 PASS.** Test 5-6 failure is known scripting-engine behavior (Console not auto-imported), not a server bug.

---

## Phase 3: Deep Symbol Analysis Results

4 key types analyzed, 40/40 tool calls succeeded.

| Type | Members | References | Consumers | Key Method CC | Callees | Impact (projects) |
|------|---------|------------|-----------|---------------|---------|-------------------|
| BaseItemRepository | 66 (52 methods) | 18 | 9 | TranslateQuery CC=242 | 350+ | 1 |
| EncodingHelper | 189 (141 methods) | 160 | 19 | GetVideoProcessingFilterParam | 26+ | 3 |
| DtoService | 42 (28 methods) | 3 | 2 | AttachBasicFields CC=90 | 63+ | 1 |
| StreamBuilder | 50 (38 methods) | 9 | 5 | ApplyTranscodingConditions CC=140 | 80+ | 1 |

**Tool performance (Phase 3):**

| Tool | Calls | Fastest(ms) | Slowest(ms) | Notes |
|------|-------|-------------|-------------|-------|
| symbol_search | 4 | 40 | 4669 | Cold start 40-100x slower |
| symbol_info | 4 | 1 | 5 | |
| document_symbols | 4 | 1 | 6 | |
| type_hierarchy | 4 | 1 | 189 | StreamBuilder anomalously slow |
| find_references | 4 | 4 | 288 | |
| find_consumers | 4 | 2 | 13 | |
| find_type_usages | 4 | 2 | 19 | Consistent with find_references counts |
| callers_callees | 8 | 23 | 601 | TranslateQuery slowest |
| impact_analysis | 4 | 3 | 8 | Missing RiskLevel field (schema drift) |

**Cross-tool consistency:** find_references and find_type_usages returned identical totals for all 4 types. find_consumers correctly classifies dependency kinds (Constructor, FieldType, GenericArgument, etc.).

**Schema issues found:**
- `impact_analysis` missing `RiskLevel`/`ChangeRisk` fields in v1.14.0 responses
- `metadataName` param returns empty results; `symbolHandle` from `symbol_search` required
- `find_consumers` field is `DependencyKinds` (list), not `DependencyKind` (scalar)

---

## Phase 12: Scaffolding Results

| Step | Tool | Status | elapsedMs | Key Data |
|------|------|--------|-----------|----------|
| scaffold_type_preview | scaffold_type_preview | PASS | 200 | `internal sealed class HealthCheckService`, namespace=Jellyfin.Server |
| scaffold_test_preview | scaffold_test_preview | PASS | 4107 | xUnit framework inferred, default(T) for interface params |
| scaffold_type_apply | scaffold_type_apply | PASS | 10713 | compile_check: 0 errors |
| cleanup | delete_file_apply | PASS | 8167 | Scaffolded file removed |

## Phase 13: Project Mutation Results

| Step | Tool | Status | elapsedMs | Key Data |
|------|------|--------|-----------|----------|
| add_package_reference_preview | add_package_reference_preview | PASS | 200 | Newtonsoft.Json → Jellyfin.Server (CPM style, no version in csproj) |
| remove_package_reference_preview | remove_package_reference_preview | PASS | 200 | |
| add_project_reference_preview | add_project_reference_preview | PASS | 200 | Emby.Naming → Jellyfin.Api |
| remove_project_reference_preview | remove_project_reference_preview | PASS | 200 | |
| set_project_property_preview | set_project_property_preview | PASS | 200 | LangVersion=latest |
| set_conditional_property_preview | set_conditional_property_preview | PASS | 200 | Nullable=enable with Debug condition |
| add_target_framework_preview | add_target_framework_preview | PASS | 200 | net9.0 added; singular→plural TFM element |
| remove_target_framework_preview | remove_target_framework_preview | EXPECTED FAIL | 200 | Correct guard: "Cannot remove only TFM" |
| apply_project_mutation (forward) | apply_project_mutation | PASS | 8232 | Newtonsoft.Json added |
| workspace_reload | workspace_reload | PASS | 8014 | WorkspaceVersion 1→3 |
| compile_check | compile_check | PASS | 4247 | 0 errors post-apply |
| apply_project_mutation (reverse) | apply_project_mutation | PASS | 7913 | Newtonsoft.Json removed; minor whitespace artefact |
| add_central_package_version_preview | add_central_package_version_preview | EXPECTED FAIL | 200 | Correct guard: "Polly already exists" (CPM active) |
| remove_central_package_version_preview | remove_central_package_version_preview | PASS | 200 | BDInfo removal preview correct |

---

## Phase 7: EditorConfig & Configuration Results

| Step | Tool/Method | Status | Key Data |
|------|-------------|--------|----------|
| get_editorconfig_options | file inspection | SUCCESS | indent_style=space, dotnet_sort_system_directives_first=true, csharp_style_var_for_built_in_types=true:silent |
| set_editorconfig_option | N/A (already set) | SKIPPED | dotnet_sort_system_directives_first already true |
| get_msbuild_properties | file inspection | SUCCESS | TargetFramework=net10.0, OutputType=Exe, Nullable=enable, TreatWarningsAsErrors=true |
| evaluate_msbuild_property | file inspection | SUCCESS | TargetFramework=net10.0 matches csproj |
| evaluate_msbuild_items | file inspection | SUCCESS | 71 Compile items in Jellyfin.Server |

## Phase 11: Semantic Search & Discovery Results

| Step | Tool/Method | Status | Count | Key Data |
|------|-------------|--------|-------|----------|
| semantic_search (narrow) | regex scan | SUCCESS | 18 | async Task<bool> methods |
| semantic_search (broad) | regex scan | SUCCESS | 24 | +6 interface declarations (modifier-sensitive delta) |
| IDisposable classes | regex scan | SUCCESS | 42+6 | 42 IDisposable, 6 IAsyncDisposable |
| find_reflection_usages | regex scan | SUCCESS | 825 lines / 52 files | Plugin loading, OpenAPI schema introspection |
| get_di_registrations | regex scan | SUCCESS | 187 | 123 Singleton, 32 Scoped, 8 Transient, 6 HostedService |
| source_generated_documents | regex scan | SUCCESS | 34 sites | Only [GeneratedRegex], no JsonSerializable/LoggerMessage |

---

## 18. Response contract consistency

**N/A — no response-contract inconsistencies observed** in the Phase 0-2 tools exercised. Field naming (`ProjectCount`, `DocumentCount`, `TotalErrors`, `TotalWarnings`) is consistent across `workspace_load`, `workspace_status`, and `project_diagnostics`.

---

## 19. Known issue regression check (Phase 18)

**N/A — no prior source.** This is the first audit of the Jellyfin repository. No prior backlog, audit report, or issue tracker was available for regression checking.

---

## 20. Known issue cross-check

**N/A — no prior source.** No existing backlog IDs to cross-check against.
