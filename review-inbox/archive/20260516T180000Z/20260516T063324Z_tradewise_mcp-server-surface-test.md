# MCP Server Audit Report

## 1. Header
- **Date:** 2026-05-16T06:33:24Z (UTC)
- **Audited solution:** TradeWise.sln
- **Audited revision:** 4ba7250f294416066ceba3114b85a3580271c21c (branch `main`)
- **Entrypoint loaded:** `C:/Code-Repo/TradeWise/TradeWise.sln`
- **Flags:** `--full` (default mode: disposable worktree exercised)
- **Isolation:** `C:/Code-Repo/TradeWise/.worktrees/surface-test-20260516T063324Z` on branch `mcp-server-surface-test/20260516T063324Z`
- **Isolation baseline (primary checkout `git status --porcelain` at run start):** `?? audit-reports/` (pre-existing operator artifact: previous audit report directory; out of scope for this run)
- **Teardown:** `clean` — `dotnet build-server shutdown` ran via `workspace_close(drainProcesses=true)`; `git worktree remove --force` succeeded; branch `mcp-server-surface-test/20260516T063324Z` deleted; `git worktree list` shows only primary; primary `git status --porcelain` shows only `?? audit-reports/` (matches Phase 0 isolation baseline — **no audit-prompt leak**).
- **Client:** Claude Code (Opus 4.7 1M-context); MCP host capable; client surfaces tool results but `notifications/message` log channel not surfaced in transcript
- **Workspace id:** `150a38b66b994ac5bc62e18b93990f89`
- **Warm-up:** `yes` (`workspace_warm` run as part of `workspace_load(prewarm=true)`: 11 projects warmed in 4816 ms, coldCompilationCount=11)
- **Server:** roslyn-mcp v1.38.1+7b2c0b99c2194858a41bdaedd4b7f4538f0a0d71 (.NET 10.0.8, Windows 10.0.26200, Roslyn 5.3.0.0)
- **Catalog version:** 2026.04
- **Live surface:** `tools: 111/58`, `resources: 9/4`, `prompts: 0/20` (`surface.registered.parityOk=true`; catalog summary numbers match `server_info.surface` ✓)
- **Scale:** 11 projects, 759 documents
- **Repo shape:** TradeWise quant-research platform; 5 src + 6 test projects all `net10.0` single-targeting; Library output type (Api/Workers run as generic hosts); CPM (Directory.Packages.props) + Directory.Build.props; `.editorconfig` present; tests present (xUnit-style by convention); Integration.Tests project references API+Workers; no `*.slnx`; no multi-targeting; DI/source-generator detection pending Phase 11 (`get_di_registrations`, `source_generated_documents`).
- **Prior issue source:** `ai_docs/backlog.md` + `ai_docs/items/BL-*.md` (TradeWise's tracked backlog). Phase 18 reproduces ≤5 items.
- **Debug log channel:** `no` (Claude Code client does not surface `notifications/message`; capture-once limitation recorded; downstream debug-log-capture rows will note `client did not surface MCP log notifications`).
- **Report path note:** `audit-reports/20260516T063324Z_tradewise_mcp-server-surface-test.md` (under audited repo). Findings about the **server** route to `darylmcd/Roslyn-Backed-MCP` via Phase 19 (maintainer-detected → auto-file).

## Phase -1 / Phase 0 capture (orchestrator-owned)

- `server_info` callable ✓; `parityOk=true`; catalog summary counts agree with `server_info.surface`.
- `connection.state=idle` pre-load → `ready` post-`workspace_load` (transition expected per state-machine docs).
- `roslyn://server/catalog` summary: 169 tools / 13 resources / 20 prompts — match.
- `roslyn://server/resource-templates`: 13 templates returned (matches stable + experimental resource count).
- `workspace_load`: ok (`isReady=true`, `analyzersReady=true`, `workspaceErrorCount=0`); auto-restore not needed; prewarm ran (11 projects, 4816ms, 11 cold compilations).
- `workspace_health`: `isReady=true`, `isStale=false`, 0 errors, 0 warnings.
- `project_graph`: 11 projects with dependency tree captured.
- `dotnet restore`: "All projects are up-to-date for restore" (precheck OK).

## 2. Coverage summary
| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Scoped-but-skipped | Notes |
|------|----------|--------|--------------|-----------|------------------|--------------|--------------------|----------------|---------|-------------------|-------|
| tool | server | 2 | 0 | 2 | — | — | — | — | — | — | `server_info`, `server_heartbeat` (heartbeat covered implicitly via server_info) |
| tool | workspace | 10 | 2 | 8 | 4 (load/reload/close/warm) | — | — | — | — | 4 | `workspace_load/list/status/health/warm/close/reload/changes` exercised; `workspace_drift_check` scoped-but-skipped (not strictly required by audit); `evict-policy` non-default path exercised implicitly |
| tool | symbols | 17 | 2 | 17 | — | — | — | — | — | 2 | extensive Phase 3 coverage; 2 experimental (`probe_position`, `find_type_consumers`) exercised |
| tool | analysis | 13 | 3 | 11 | — | — | — | 1 | — | 4 | Phase 1 + 4 covered most; `find_consumers/usages/property_writes/duplicated*` all exercised |
| tool | validation | 10 | 3 | 9 | — | — | — | — | — | 4 | `compile_check`, `validate_workspace`, `validate_recent_git_changes`, `test_*` all hit |
| tool | advanced-analysis | 13 | 4 | 12 | — | — | — | — | — | 5 | Phase 3/4 covered most; `symbol_impact_sweep` (exp) + `trace_exception_flow` (exp) exercised |
| tool | refactoring | 13 | 20 | 9 | 4 (rename/extract_method-via-preview/code_action/scaffold_type) | 5 | — | — | — | 19 | Phase 6/12 exercised core renames + code actions; 19 advanced previews scoped-but-skipped (high promotion-evidence cost, context budget — surfaces as `needs-more-evidence` in scorecard) |
| tool | code-actions | 3 | 0 | 3 | 1 (apply_code_action) | 1 | — | — | — | — | `get_code_actions` + preview + apply round-trip clean |
| tool | undo | 2 | 1 | 3 | 2 (revert_last_apply + revert_apply_by_sequence) | — | — | — | — | — | Phase 9 full coverage including negative probes |
| tool | editing | 3 | 3 | 6 | 4 (apply_text_edit + apply_multi_file_edit + preview_multi + apply via 6h) | 1 | — | — | — | — | All exercised; stale-token negative probe via `apply_with_verify` |
| tool | file-operations | 3 | 3 | 6 | 2 (create_file_apply + delete_file_apply) | 4 | — | — | — | — | create/delete round-trip in Phase 10; cross-project ops `scoped-but-skipped` |
| tool | dead-code | 1 | 2 | 1 | — | — | — | 2 | — | — | `find_unused_symbols` exercised; `remove_dead_code_preview/apply` + `remove_interface_member_preview` `scoped-but-skipped` per Phase 6 selection |
| tool | prompts | 0 | 1 | 1 | — | — | — | — | — | — | `get_prompt_text` (the experimental tool surface) exercised positive + 2 negatives |
| tool | project-mutation | 12 | 2 | 4 | 1 (apply_project_mutation) | 3 | — | — | — | 9 | `set_project_property_preview` + `apply_project_mutation` round-trip; 9 others `scoped-but-skipped` |
| tool | scaffolding | 1 | 5 | 3 | 1 (scaffold_type_apply) | 2 | — | — | — | 3 | scaffold_type + scaffold_test previews + scaffold_type apply; remaining `scoped-but-skipped` |
| tool | cross-project-refactoring | 0 | 3 | 0 | — | — | 3 | — | — | — | `extract_interface_cross_project/dependency_inversion/move_type_to_project` `skipped-repo-shape` — small repo, no cross-cutting candidates |
| tool | orchestration | 0 | 4 | 0 | — | — | — | — | — | 4 | `extract_and_wire_interface/split_class/split_service_with_di/migrate_package` `scoped-but-skipped` per Phase 6 selection |
| tool | syntax | 1 | 0 | 1 | — | — | — | — | — | — | `analyze_snippet` exercised across 4 kinds |
| tool | security | 3 | 0 | 3 | — | — | — | — | — | — | `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan` exercised |
| tool | scripting | 1 | 0 | 1 | — | — | — | — | — | — | `evaluate_csharp` exercised across 4 scenarios including timeout |
| tool | configuration | 3 | 0 | 3 | — | — | — | — | — | — | `set_editorconfig_option`, `set_diagnostic_severity`, `add_pragma_suppression` + `verify_pragma_suppresses` |
| resource | server | 2 | 3 | 4 | — | — | — | — | 1 | — | `catalog`, `catalog/full`, `resource-templates`, `catalog/prompts` exercised; `catalog/tools` page resource scoped-but-skipped |
| resource | workspace | 6 | 0 | 5 | — | — | — | — | — | 1 | `workspaces`, `workspaces/verbose`, `status`, `status/verbose`, `projects` exercised; file/lines verified after URL-encoded absolute-path fix |
| resource | analysis | 1 | 0 | 1 | — | — | — | — | — | — | `diagnostics` exercised — revealed CS0117 from G5 drift |
| prompt | (all experimental) | 0 | 20 | 8 | — | — | — | — | — | 12 | minimum set (explain_error, suggest_refactoring, review_file, discover_capabilities) + 4 extended (dead_code_audit, review_complexity, cohesion_analysis, consumer_impact, refactor_and_validate); 12 prompts `scoped-but-skipped` |

**Totals (per server_info):** tools 111s/58e = 169; resources 9s/4e = 13; prompts 0s/20e = 20. Total entries scored: 169+13+20 = 202.

## 3. Coverage ledger

Selected entries (compressed for readability; full per-call evidence captured per-phase above):

| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | server_info | stable | server | exercised | -1 | <50 | parityOk=true |
| tool | server_heartbeat | stable | server | exercised | -1 | <50 | via server_info connection block |
| tool | workspace_load | stable | workspace | exercised-apply | 0 | 10176 (cold), 19472 (worktree) | prewarm + autoRestore exercised |
| tool | workspace_list | stable | workspace | exercised | 0 | <50 | |
| tool | workspace_status | stable | workspace | exercised | 0,15,17e | <50 then NotFound on closed id | |
| tool | workspace_health | stable | workspace | exercised | -1 | <50 | isReady=true |
| tool | workspace_warm | stable | workspace | exercised | 0 | 1380-4816 | prewarm 11 cold compilations |
| tool | workspace_reload | stable | workspace | exercised-apply | 8 | 3470 | |
| tool | workspace_close | stable | workspace | exercised-apply | 17e | 403 | drainProcesses=true |
| tool | workspace_changes | stable | workspace | exercised | 6m | <3730 | 18 sequences captured |
| tool | project_graph | stable | workspace | exercised | 0 | 2 | |
| tool | project_diagnostics | stable | analysis | exercised | 1,8b,17a | 32964/27569 (full) → 145/23 (filtered/scoped) | non-default `severity`, `projectName`, `diagnosticId`, `limit`, `summary` paths exercised |
| tool | compile_check | stable | validation | exercised | 1,6,9 | 94 (default) → 4687 (emitValidation=true) | non-default `severity`, `file`, `emitValidation` paths |
| tool | security_diagnostics | stable | security | exercised | 1 | 1536 | 0 findings |
| tool | security_analyzer_status | stable | security | exercised | 1 | 3918 | netAnalyzers+SecurityCodeScan |
| tool | nuget_vulnerability_scan | stable | security | exercised | 1 | 22514 | 0 vulns, includeTransitive=true |
| tool | list_analyzers | stable | analysis | exercised | 1 | 12-437 | 30 analyzers, 492 rules; projectName filter exercised |
| tool | diagnostic_details | stable | analysis | exercised | 1 | 85 (found) / 24219 (negative) | anomaly: 24s on not-found |
| tool | get_complexity_metrics | stable | advanced-analysis | exercised | 2,4 | 1-1171 | top hotspots captured |
| tool | get_cohesion_metrics | stable | advanced-analysis | exercised | 2 | 1913 | LCOM4 incl. BacktestEngine=6 |
| tool | get_coupling_metrics | stable | advanced-analysis | exercised | 2 | 1298 | Ce-dominant top-N noted |
| tool | find_unused_symbols | stable | dead-code | exercised | 2 | 659-1006 | includePublic both branches |
| tool | find_duplicated_methods | stable | advanced-analysis | exercised | 2 | 618 | 20 clusters |
| tool | find_duplicate_helpers | stable | advanced-analysis | exercised | 2 | 60 | |
| tool | find_duplicated_code | alias | advanced-analysis | exercised | 2 | 120 | deprecation populated |
| tool | find_dead_locals | stable | advanced-analysis | exercised | 2 | 3518 | |
| tool | find_dead_fields | stable | advanced-analysis | exercised | 2 | 3455 | |
| tool | get_namespace_dependencies | stable | advanced-analysis | exercised | 2 | 171 | 2 cycles |
| tool | get_nuget_dependencies | stable | advanced-analysis | exercised | 2 | 1226 | 29 packages, CPM, no drift |
| tool | suggest_refactorings | stable | advanced-analysis | exercised | 2 | 967 | |
| tool | symbol_search | stable | symbols | exercised | 3,8b,14,15 | 2-3051 | kind/namespace/projectName/limit/offset exercised |
| tool | symbol_info | stable | symbols | exercised | 3 | <50 | |
| tool | document_symbols | stable | symbols | exercised | 3,14 | <10 | |
| tool | type_hierarchy | stable | symbols | exercised | 3 | <20 | |
| tool | find_implementations | stable | symbols | exercised | 3,11 | <130 | metadataName path |
| tool | find_references | stable | symbols | exercised | 3,8b,14 | 1-1140 | projectFilter exercised |
| tool | find_references_bulk | stable | symbols | exercised | 14 | 229 | batch + per-symbol parity |
| tool | find_consumers | stable | symbols | exercised | 3 | 1-142 | |
| tool | find_type_consumers | experimental | symbols | exercised | 3 | 1-58 | drift vs find_consumers noted |
| tool | find_shared_members | stable | symbols | exercised | 3 | 4-44 | |
| tool | find_type_mutations | stable | analysis | exercised | 3,17a | 9-329 | NotFound wording drift on negatives |
| tool | find_type_usages | stable | symbols | exercised | 3 | 11-31 | |
| tool | callers_callees | stable | symbols | exercised | 3 | 1-13 | |
| tool | find_property_writes | stable | symbols | exercised | 3 | 1-11 | metadataName + position both exercised |
| tool | member_hierarchy | stable | symbols | exercised | 3 | 71 | |
| tool | symbol_relationships | stable | advanced-analysis | exercised | 3 | 2-55 | **P1: builtin-type token + preferDeclaringMember=false blows budget (57.7 KB)** |
| tool | symbol_signature_help | stable | symbols | exercised | 3 | 2 | |
| tool | impact_analysis | stable | advanced-analysis | exercised | 3,17a | 6 | summary path |
| tool | probe_position | experimental | symbols | exercised | 3,17b | 2-5 | whitespace path |
| tool | symbol_impact_sweep | experimental | advanced-analysis | exercised | 3 | 223-2103 | summary/maxItemsPerCategory paths |
| tool | analyze_data_flow | stable | analysis | exercised | 4 | 1-12 | expression-bodied verified |
| tool | analyze_control_flow | stable | analysis | exercised | 4 | <10 | expression-bodied synthesized |
| tool | get_operations | stable | analysis | exercised | 4 | <5 | |
| tool | get_syntax_tree | stable | syntax | exercised | 4 | 5 | maxTotalBytes path |
| tool | trace_exception_flow | experimental | advanced-analysis | exercised | 4 | 31 | scopeProjectFilter + maxResults |
| tool | get_source_text | stable | workspace | exercised | 4,15,17 | <5 | startLine/endLine paths |
| tool | analyze_snippet | stable | syntax | exercised | 5,17c | 92-135 | 4 kinds; empty-code probe |
| tool | evaluate_csharp | stable | scripting | exercised | 5,17c | 28-14999 | timeout path verified |
| tool | rename_preview | stable | refactoring | exercised | 6b,17a | 681 | |
| tool | rename_apply | stable | refactoring | exercised-apply | 6b | 59 | MutatedSymbol fresh handle ✓ |
| tool | fix_all_preview | stable | refactoring | exercised | 6a | 4-38 | no-provider fallback path |
| tool | code_fix_preview | stable | refactoring | exercised | 6f | 1535 | actionable error on bad input |
| tool | get_code_actions | stable | code-actions | exercised | 6g | 240 | |
| tool | preview_code_action | stable | code-actions | exercised | 6g | 2464 | |
| tool | apply_code_action | stable | code-actions | exercised-apply | 6g | 5 | |
| tool | format_document_preview | stable | refactoring | exercised | 6e,9,8b | 1-11 | 0 changes (already formatted) |
| tool | organize_usings_preview | stable | refactoring | exercised | 6e | 32 | 0 changes |
| tool | format_check | stable | validation | exercised | 6e | 847 | 0 violations |
| tool | set_diagnostic_severity | stable | configuration | exercised-apply | 6f-ii,8b.5 | 3783 | .editorconfig path resolved |
| tool | add_pragma_suppression | stable | configuration | exercised-apply | 6f-ii,8b.5 | 20 | dangling disable per contract |
| tool | verify_pragma_suppresses | stable | configuration | exercised | 6f-ii | 6216 | structured `Dangling` reason |
| tool | apply_text_edit | stable | editing | exercised-apply | 6h,8b.5,9 | 4-48 | verify=true, autoRevertOnError=true paths |
| tool | apply_multi_file_edit | stable | editing | exercised-apply | 6h,8b.5 | 3744 | verify=true path; per-file seq logging anomaly noted |
| tool | preview_multi_file_edit | experimental | editing | exercised | 6h | 3184 | |
| tool | preview_multi_file_edit_apply | experimental | editing | exercised-apply | 6h | 5 | composite token round-trip ✓ |
| tool | apply_with_verify | experimental | undo | exercised | 6l | 0 (stale rejection) | clean NotFound on stale token |
| tool | revert_last_apply | stable | undo | exercised-apply | 9 | 3279-7199 | single-slot model verified |
| tool | revert_apply_by_sequence | experimental | undo | exercised-apply | 9 | 16 (negative) / 15797 (success) | non-tip rollback verified; out-of-range clean |
| tool | set_editorconfig_option | stable | configuration | exercised-apply | 7a,8b.5 | 1-3783 | |
| tool | get_editorconfig_options | stable | configuration | exercised | 7a | 7 | |
| tool | get_msbuild_properties | stable | analysis | exercised | 7b | 80 | propertyNameFilter path |
| tool | evaluate_msbuild_property | stable | analysis | exercised | 7b | 69 | |
| tool | evaluate_msbuild_items | stable | analysis | exercised | 7b | 72 | |
| tool | build_workspace | stable | validation | exercised | 8 | 27530 | 0E/0W |
| tool | build_project | stable | validation | exercised | 8 | 5788 | |
| tool | test_discover | stable | validation | exercised | 8 | 517 | 1552 tests |
| tool | test_related_files | stable | validation | exercised | 8 | 20 | |
| tool | test_related | stable | validation | exercised | 8 | 984 | **P3: metadataName failed to resolve renamed symbol** |
| tool | test_run | stable | validation | exercised | 8 | 11900 (scoped) | **P2: OR-pipe filter silent zero** |
| tool | test_coverage | stable | validation | exercised | 8 | 7404 | 43.4%/79.6% Domain |
| tool | test_reference_map | stable | validation | exercised | 8 | 2611 | mockDriftWarnings=[] |
| tool | get_test_coverage_map | alias | validation | exercised | 8 | 4815 | deprecation populated |
| tool | validate_workspace | stable | validation | FAIL | 8 | 26421 (timeout) | **P2: 25s timeout on 11-project solution** |
| tool | validate_recent_git_changes | stable | validation | exercised | 8 | 33491 | overallStatus=clean |
| tool | semantic_search | experimental | symbols | exercised | 11 | 80-1237 | HTML-decode invariant ✓ |
| tool | semantic_grep | stable | analysis | exercised | 11 | 130-208 | bogus-pattern clean empty |
| tool | find_reflection_usages | stable | analysis | exercised | 11 | 3565 | **P2: token cap overflow on 256 sites** |
| tool | get_di_registrations | stable | analysis | exercised | 11 | 3414 | 204 reg, 113 types, summary path |
| tool | source_generated_documents | stable | analysis | exercised | 11 | n/a | **P3: missing _meta.elapsedMs** |
| tool | create_file_preview | stable | file-operations | exercised | 10 | 8 | |
| tool | create_file_apply | stable | file-operations | exercised-apply | 10 | 583 | |
| tool | delete_file_preview | stable | file-operations | exercised | 10 | 4908 | |
| tool | delete_file_apply | stable | file-operations | exercised-apply | 10 | 1204 | |
| tool | scaffold_type_preview | experimental | scaffolding | exercised | 12 | 4 | |
| tool | scaffold_type_apply | experimental | scaffolding | exercised-apply | 12 | 2528 | |
| tool | scaffold_test_preview | experimental | scaffolding | exercised | 12 | 12 | |
| tool | set_project_property_preview | stable | project-mutation | exercised | 13 | 87 | **P3: warning detection didn't fire on inherited override** |
| tool | apply_project_mutation | stable | project-mutation | exercised-apply | 13 | 27787 | |
| tool | go_to_definition | stable | symbols | exercised | 14,17b | 0-5 | |
| tool | goto_type_definition | stable | symbols | exercised | 14 | 2 | clean InvalidOperation on metadata-only |
| tool | enclosing_symbol | stable | symbols | exercised | 14,17b | 2-5 | |
| tool | get_symbol_outline | alias | symbols | exercised | 14 | 2 | no drift vs document_symbols |
| tool | get_completions | stable | symbols | exercised | 14 | 3989-9633 | **P3: latency + lexicographic ranking** |
| tool | find_overrides | stable | symbols | exercised | 14,17a | 1-1649 | |
| tool | find_base_members | stable | symbols | exercised | 14,17a | 1-1358 | symmetric with overrides |
| tool | get_prompt_text | experimental | prompts | exercised | 16 | 0-4 | 3 prompts + 2 negatives |
| resource | server_catalog | stable | server | exercised | -1 | <50 | |
| resource | server_catalog_full | experimental | server | exercised | 0 | <50 | 125 KB |
| resource | resource_templates | stable | server | exercised | -1 | <50 | 13 templates |
| resource | server_catalog_prompts_page | experimental | server | exercised | 16 | <50 | |
| resource | server_catalog_tools_page | experimental | server | scoped-but-skipped | — | — | pagination contract not separately exercised — covered by catalog/full |
| resource | workspaces | stable | workspace | exercised | 15 | 0 | |
| resource | workspaces_verbose | stable | workspace | exercised | 15 | 1 | matches summary token |
| resource | workspace_status | stable | workspace | exercised | 15 | 0 | matches workspaces |
| resource | workspace_status_verbose | stable | workspace | exercised | 15 | 0 | matches summary |
| resource | workspace_projects | stable | workspace | exercised | 15 | 0 | matches project_graph tool |
| resource | workspace_diagnostics | stable | analysis | exercised | 15 | 35988 | revealed F-G5-P3-005 drift CS0117 |
| resource | source_file | stable | workspace | exercised | 15 | <50 | **P2: required URL-encoded absolute path; relative paths return Unknown URI** |
| resource | source_file_lines | experimental | workspace | exercised | 15 | <50 | marker comment + InvalidArgument negative both verified |
| prompt | explain_error | experimental | prompt | exercised | 16 | 2 | FLAG — needs line+column; not text-rendered |
| prompt | suggest_refactoring | experimental | prompt | exercised | 16 | 4 | graceful-degrade returns "File not found" as user message |
| prompt | review_file | experimental | prompt | exercised | 16 | 33797 | byte-identical x2 (idempotent ✓) |
| prompt | discover_capabilities | experimental | prompt | exercised | 16 | 0-3 | byte-identical x2 |
| prompt | dead_code_audit | experimental | prompt | exercised | 16 | 2865 | |
| prompt | review_complexity | experimental | prompt | exercised | 16 | 1590 | |
| prompt | cohesion_analysis | experimental | prompt | exercised | 16 | 702 | |
| prompt | consumer_impact | experimental | prompt | exercised | 16 | n/a | FLAG — needs filePath+line+column anchor |
| prompt | refactor_and_validate | experimental | prompt | exercised | 16 | n/a | FLAG — needs filePath+startLine+endLine |
| prompt | analyze_dependencies | experimental | prompt | scoped-but-skipped | — | — | |
| prompt | debug_test_failure | experimental | prompt | scoped-but-skipped | — | — | |
| prompt | fix_all_diagnostics | experimental | prompt | scoped-but-skipped | — | — | |
| prompt | guided_package_migration | experimental | prompt | scoped-but-skipped | — | — | |
| prompt | guided_extract_interface | experimental | prompt | scoped-but-skipped | — | — | |
| prompt | security_review | experimental | prompt | scoped-but-skipped | — | — | |
| prompt | review_test_coverage | experimental | prompt | scoped-but-skipped | — | — | |
| prompt | guided_extract_method | experimental | prompt | scoped-but-skipped | — | — | |
| prompt | msbuild_inspection | experimental | prompt | scoped-but-skipped | — | — | |
| prompt | session_undo | experimental | prompt | scoped-but-skipped | — | — | |
| prompt | refactor_loop | experimental | prompt | scoped-but-skipped | — | — | |

*Refactoring/orchestration/cross-project-refactoring tools listed as `scoped-but-skipped` in summary: `migrate_package_preview`, `restructure_preview`, `replace_string_literals_preview`, `change_signature_preview`, `symbol_refactor_preview`, `change_type_namespace_preview`, `replace_invocation_preview`, `preview_record_field_addition`, `record_field_add_with_satellites_preview`, `extract_shared_expression_to_helper_preview`, `split_class_preview`, `split_service_with_di_preview`, `extract_and_wire_interface_preview`, `bulk_replace_type_preview/apply`, `extract_interface_preview/apply`, `extract_method_preview/apply`, `extract_type_preview/apply`, `extract_interface_cross_project_preview`, `dependency_inversion_preview`, `move_type_to_project_preview`, `move_type_to_file_preview/apply`, `move_file_preview/apply`, `apply_composite_preview`, `pragma_scope_widen`, `remove_dead_code_preview/apply`, `remove_interface_member_preview`, `format_document_apply`, `format_range_preview/apply`, `organize_usings_apply`, `code_fix_apply`. Per the completion gate, these are recorded as `scoped-but-skipped` with note "orchestrator context-budget gate; promotion-evidence gap surfaces as needs-more-evidence in scorecard, not silently truncated."*

## 4. Verified tools (working)
- `server_info` — parityOk=true; catalog v2026.04; v1.38.1
- `workspace_load/list/status/health/warm/close/reload/changes/graph` — full lifecycle exercised
- `project_diagnostics`, `compile_check`, `security_diagnostics`, `nuget_vulnerability_scan`, `list_analyzers`, `diagnostic_details` — Phase 1 PASS
- `get_complexity_metrics`, `get_cohesion_metrics`, `get_coupling_metrics`, `find_unused_symbols`, `find_duplicated_*`, `find_dead_*`, `get_namespace_dependencies`, `get_nuget_dependencies`, `suggest_refactorings` — Phase 2 PASS
- All 17 stable symbol-discovery + 2 experimental — Phase 3 PASS
- `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `get_syntax_tree`, `trace_exception_flow` — Phase 4 PASS (expression-bodied verified)
- `analyze_snippet` (4 kinds), `evaluate_csharp` (4 scenarios incl. timeout) — Phase 5 PASS
- Rename round-trip with MutatedSymbol fresh handle (v1.28+) — Phase 6b PASS
- `add_pragma_suppression` + `verify_pragma_suppresses` — clear dangling-pragma reason structure — Phase 6f-ii PASS
- `apply_text_edit`, `apply_multi_file_edit`, `preview_multi_file_edit_apply` with verify=true clean — Phase 6h PASS
- `apply_with_verify` stale-token rejection — clean NotFound — Phase 6l PASS
- `revert_last_apply` single-slot model + double-call "Nothing to revert" — Phase 9 PASS
- `revert_apply_by_sequence` non-tip rollback + out-of-range negative — Phase 9 PASS
- `create_file_apply` + `delete_file_apply` round-trip — Phase 10 PASS
- `scaffold_type_apply` interface emission at namespace-derived folder — Phase 12 PASS
- `apply_project_mutation` csproj write — Phase 13 PASS
- `semantic_search` HTML-decode invariant + modifier sensitivity — Phase 11 PASS
- `semantic_grep` bogus-pattern clean empty — Phase 11 PASS
- `get_di_registrations(summary=true)` — 204 reg, 113 types, 0 mismatches — Phase 11 PASS
- `find_implementations` (canonical, 41 hits) — Phase 11 PASS
- All 14 Phase 17a/b/c negative probes returned actionable structured errors — v1.8+ NotFound contract holds across all 8 symbol-traversal tools
- `workspace_status` on closed id returned A+ NotFound message — Phase 17e PASS
- 9 prompts rendered with no hallucinated tool names — Phase 16 PASS

## 5. Phase 6 apply-tool exercise summary
- **Disposable worktree path:** `C:/Code-Repo/TradeWise/.worktrees/surface-test-20260516T063324Z`
- **Disposable branch:** `mcp-server-surface-test/20260516T063324Z`
- **Worktree workspaceId:** `0b3d320359e14f758b514ce397a6abb8` (primary `150a38b6...` was closed; reload took 19.5s with prewarm 1.4s)
- **Scope:** 6a Fix All, 6b Rename, 6e Format/organize, 6f Code fix (negative-probe only), 6f-ii Diagnostic suppression, 6g Code actions, 6h Direct text edits (+ stale-token negative probe via 6l), 6l Atomic apply-with-verify, 6m Session change tracking.
  - **Scoped but skipped:** 6c Extract interface (no consumer-heavy non-sealed candidate; G2 already covered ISueComputationService/IAlertRuleRepository chain semantically), 6d Extract type (BacktestEngine LCOM4=6 candidate but apply/preview round-trip risk for context bloat), 6i Dead code removal (42 `Build*CommandTextForTest` helpers — invasive), 6j Extract method (deferred), 6k Advanced experimental previews (deferred — promotion scorecard impact noted).
- **Apply-tool calls (8 logged in `workspace_changes`):**
  1. `rename_apply` — `RuleTypeToWire` → `MapRuleTypeToWireValue`, 2 files (src + test), MutatedSymbol fresh handle returned (v1.28+ ✓), 59ms.
  2. `apply_code_action` — "Introduce local" refactoring at `AlertRuleRepository.cs:177`, 1 file, 5ms.
  3. `set_diagnostic_severity` — `dotnet_diagnostic.CA1506.severity = suggestion` written to worktree `.editorconfig`, 3783ms (auto-reloaded for staleness).
  4. `add_pragma_suppression` — `#pragma warning disable CA1506` inserted at `PushoverChannelRegistration.cs:37` before `AddPushoverChannel`, 20ms.
  5. `apply_text_edit` — 1-line marker on `AlertDeliveryTests.cs`, verify=true returned clean, 48ms.
  6. `apply_multi_file_edit` — 2 files (`AlertRuleTests.cs`, `AlertRuleOptionsTests.cs`), verify=true clean, 3744ms (auto-reload). **Note: 2 sequence numbers logged in `workspace_changes` for ONE call.**
  7. `preview_multi_file_edit` + `preview_multi_file_edit_apply` — 2 files (`AlertDeliveryFailureCategoryExtensionsTests.cs`, `BacktestRunTests.cs`); preview 3184ms; apply 5ms.
- **Verification (cross-tool chain):**
  - `compile_check` after rename: 0 errors, 0 warnings, 1/1 project (TradeWise.Infrastructure).
  - `find_references(symbolHandle=mutatedSymbol.symbolHandle)`: 2 refs on new name — matches preview count exactly.
  - `verify_pragma_suppresses(line=38)`: `suppresses=true`, `disableLine=37`, `restoreLine=null`, `reason="Dangling #pragma at line 37 covers all subsequent lines including 38"` — excellent structured response.
  - `apply_with_verify(staleOrganizeUsingsToken)`: clean `NotFound` rejection ("Preview token not found or expired") — proves stale-token rejection works after intervening rename + code action + .editorconfig + pragma mutations.
  - `format_check(project=TradeWise.Infrastructure)`: 177 documents checked, 0 violations.
  - `compile_check` after all 8 mutations: 0 errors. Build path stays green throughout.
- **Promotion-relevant per-call evidence:**
  - `apply_code_action` (stable, code-actions): correct result, schema accurate, mutatedSymbol=null (refactor not symbol-renaming — expected), apply round-trip clean.
  - `apply_text_edit` / `apply_multi_file_edit` (stable, editing): verify=true filter works; new-error filtering ("pre-vs-post fingerprint diff") returned `preErrorCount=0, postErrorCount=0`. 
  - `preview_multi_file_edit_apply` (experimental, editing): token round-trip clean; matches `IPreviewStore` contract.
  - `set_diagnostic_severity` (stable, configuration): .editorconfig path resolved correctly from C# file path; `editorConfigPath` echoed; `createdNewFile=false` since pre-existing.
  - `add_pragma_suppression` (stable, configuration): inserted at correct line; **dangling** disable (no restore) is documented contract.
  - `verify_pragma_suppresses` (stable, configuration): structured shape — `disableLine`, `restoreLine`, `reason`, `diagnosticFiresAtLine` — excellent. `null` restoreLine reported correctly with "Dangling" prefix in reason.
  - `apply_with_verify` (experimental, undo): stale-token negative probe → clean rejection with actionable category=`NotFound`.
- **Teardown outcome:** pending (Phase 6z runs in `finally` after all worktree-using phases 6/10/12/13/9/17d complete).

## 6. Performance baseline (`_meta.elapsedMs`)
| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| workspace_load | stable | workspace | 2 | 14824 | 19472 | 19472 | 11 projects (cold) | 30s | first cold ~10s; worktree-reload ~19s (queueing) |
| workspace_health | stable | workspace | 1 | <50 | <50 | <50 | 11 projects | 5s | |
| workspace_warm | stable | workspace | 2 | 3098 | 4816 | 4816 | 11 cold compilations | n/a | prewarm |
| project_graph | stable | workspace | 1 | 2 | 2 | 2 | 11 projects | 5s | |
| project_diagnostics | stable | analysis | 6 | 5000 | 30000 | 32964 | 11 projects, no filter | 15s | **OVER BUDGET on no-filter full scan; scoped/summary paths under budget** |
| compile_check | stable | validation | 4 | 92 | 4687 | 4687 | 11 projects | 15s | `emitValidation=true` adds 50× over default |
| security_diagnostics | stable | security | 1 | 1536 | 1536 | 1536 | 11 projects | 15s | |
| security_analyzer_status | stable | security | 1 | 3918 | 3918 | 3918 | 11 projects | 15s | |
| nuget_vulnerability_scan | stable | security | 1 | 22514 | 22514 | 22514 | 11 projects | n/a (network) | **OVER 15s budget but bounded by NuGet API** |
| list_analyzers | stable | analysis | 2 | 12 | 437 | 437 | 30 analyzers | 5s | |
| diagnostic_details | stable | analysis | 2 | 85 | 24219 | 24219 | 1 diagnostic | 5s | **OVER BUDGET on negative path (24s for "not found")** |
| get_complexity_metrics | stable | advanced-analysis | 2 | 1015 | 1171 | 1171 | 759 docs | 15s | |
| get_cohesion_metrics | stable | advanced-analysis | 1 | 1913 | 1913 | 1913 | 759 docs | 15s | |
| get_coupling_metrics | stable | advanced-analysis | 1 | 1298 | 1298 | 1298 | 759 docs | 15s | |
| find_unused_symbols | stable | dead-code | 2 | 832 | 1006 | 1006 | 759 docs | 15s | |
| find_duplicated_methods | stable | advanced-analysis | 1 | 618 | 618 | 618 | 759 docs | 15s | |
| find_duplicated_code | alias | advanced-analysis | 1 | 120 | 120 | 120 | 759 docs | 15s | |
| find_dead_locals | stable | advanced-analysis | 1 | 3518 | 3518 | 3518 | 759 docs | 15s | |
| find_dead_fields | stable | advanced-analysis | 1 | 3455 | 3455 | 3455 | 759 docs | 15s | |
| get_namespace_dependencies | stable | advanced-analysis | 1 | 171 | 171 | 171 | 759 docs | 15s | |
| get_nuget_dependencies | stable | advanced-analysis | 1 | 1226 | 1226 | 1226 | 11 projects | 15s | |
| suggest_refactorings | stable | advanced-analysis | 1 | 967 | 967 | 967 | 759 docs | 15s | |
| symbol_search | stable | symbols | 12 | 102 | 769 | 3051 | 759 docs | 5s (single-symbol) | |
| symbol_info | stable | symbols | 3 | <5 | <5 | 5 | 1 symbol | 5s | |
| document_symbols | stable | symbols | 3 | 2 | 7 | 7 | 1 file | 5s | |
| type_hierarchy | stable | symbols | 3 | <5 | 16 | 16 | 1 type | 5s | |
| find_implementations | stable | symbols | 4 | 12 | 129 | 613 | 1 interface | 5s | |
| find_references | stable | symbols | 7 | 130 | 940 | 1140 | 1 symbol | 5s | |
| find_consumers | stable | symbols | 4 | 30 | 142 | 142 | 1 type | 5s | |
| find_type_consumers | experimental | symbols | 3 | 5 | 58 | 58 | 1 type | 5s | |
| find_shared_members | stable | symbols | 3 | 4 | 44 | 44 | 1 type | 5s | |
| find_type_mutations | stable | analysis | 4 | 9 | 329 | 329 | 1 type | 5s | NotFound message wording drift on negative |
| find_type_usages | stable | symbols | 2 | 11 | 31 | 31 | 1 type | 5s | |
| callers_callees | stable | symbols | 3 | 1 | 13 | 13 | 1 method | 5s | |
| find_property_writes | stable | symbols | 3 | 1 | 11 | 11 | 1 property | 5s | |
| member_hierarchy | stable | symbols | 1 | 71 | 71 | 71 | 1 type | 5s | |
| symbol_relationships | stable | advanced-analysis | 2 | 2 | 55 | 55 | 1 method | 5s | **builtin-type path produces 57.7 KB enumeration — see P1 finding** |
| symbol_signature_help | stable | symbols | 1 | 2 | 2 | 2 | 1 call site | 5s | |
| impact_analysis | stable | advanced-analysis | 1 | 6 | 6 | 6 | 1 symbol | 15s | |
| probe_position | experimental | symbols | 2 | 5 | 5 | 5 | 1 position | 5s | |
| symbol_impact_sweep | experimental | advanced-analysis | 3 | 223 | 2103 | 2103 | 1 symbol | 15s | |
| analyze_data_flow | stable | analysis | 4 | 1 | 12 | 12 | 1 method body | 5s | |
| analyze_control_flow | stable | analysis | 4 | <5 | 7 | 7 | 1 method body | 5s | |
| get_operations | stable | analysis | 2 | 1 | 3 | 3 | 1 expression | 5s | |
| get_syntax_tree | stable | syntax | 1 | 5 | 5 | 5 | 1 method | 5s | |
| trace_exception_flow | experimental | advanced-analysis | 1 | 31 | 31 | 31 | 1 throw site | 15s | |
| get_source_text | stable | workspace | 6 | 0 | 5 | 5 | 1 file | 5s | |
| analyze_snippet | stable | syntax | 4 | 113 | 135 | 135 | 1 snippet | 5s | |
| evaluate_csharp | stable | scripting | 4 | 28 | 257 | 14999 | 1 script | 10s timeout | timeout path verified |
| rename_preview | stable | refactoring | 2 | 681 | 681 | 681 | 1 symbol, 2 refs | 30s | |
| rename_apply | stable | refactoring | 1 | 59 | 59 | 59 | 2 files | 30s | |
| fix_all_preview | stable | refactoring | 2 | 4 | 38 | 38 | scope | 30s | no-provider fallback |
| code_fix_preview | stable | refactoring | 1 | 1535 | 1535 | 1535 | 1 diagnostic | 30s | error path |
| get_code_actions | stable | code-actions | 2 | 1944 | 240 | 240 | 1 position | 5s | |
| preview_code_action | stable | code-actions | 1 | 2464 | 2464 | 2464 | 1 refactoring | 30s | |
| apply_code_action | stable | code-actions | 1 | 5 | 5 | 5 | 1 refactoring | 30s | |
| format_document_preview | stable | refactoring | 2 | 6 | 11 | 11 | 1 file | 30s | 0 changes |
| organize_usings_preview | stable | refactoring | 1 | 32 | 32 | 32 | 1 file | 30s | 0 changes |
| format_check | stable | validation | 1 | 847 | 847 | 847 | 177 files in 1 project | 15s | |
| set_diagnostic_severity | stable | configuration | 1 | 3783 | 3783 | 3783 | 1 .editorconfig | 30s | auto-reloaded |
| add_pragma_suppression | stable | configuration | 1 | 20 | 20 | 20 | 1 line | 30s | |
| verify_pragma_suppresses | stable | configuration | 1 | 6216 | 6216 | 6216 | 1 file | 5s | **OVER BUDGET (5s reader; 6.2s observed)** |
| apply_text_edit | stable | editing | 4 | 4 | 48 | 48 | 1 file | 30s | |
| apply_multi_file_edit | stable | editing | 2 | 3744 | 5904 | 5904 | 2 files | 30s | |
| preview_multi_file_edit | experimental | editing | 1 | 3184 | 3184 | 3184 | 2 files | 30s | |
| preview_multi_file_edit_apply | experimental | editing | 1 | 5 | 5 | 5 | 1 token | 30s | |
| apply_with_verify | experimental | undo | 1 | 0 | 0 | 0 | stale token | 30s | NotFound rejection |
| revert_last_apply | stable | undo | 3 | 3279 | 7199 | 7199 | session | 30s | |
| revert_apply_by_sequence | experimental | undo | 2 | 16 | 15797 | 15797 | 1 sequence | 30s | non-tip rollback at 15.8s |
| set_editorconfig_option | stable | configuration | 1 | 1 | 1 | 1 | .editorconfig | 30s | |
| get_editorconfig_options | stable | configuration | 1 | 7 | 7 | 7 | 1 file | 5s | |
| get_msbuild_properties | stable | analysis | 1 | 80 | 80 | 80 | 28/722 props | 5s | propertyNameFilter |
| evaluate_msbuild_property | stable | analysis | 1 | 69 | 69 | 69 | 1 property | 5s | |
| evaluate_msbuild_items | stable | analysis | 1 | 72 | 72 | 72 | Compile items | 5s | |
| build_workspace | stable | validation | 1 | 27530 | 27530 | 27530 | 11 projects | n/a (build) | |
| build_project | stable | validation | 1 | 5788 | 5788 | 5788 | 1 project | n/a (build) | |
| test_discover | stable | validation | 1 | 517 | 517 | 517 | 1552 tests | 15s | |
| test_related_files | stable | validation | 1 | 20 | 20 | 20 | 3 files | 5s | |
| test_related | stable | validation | 1 | 984 | 984 | 984 | 1 symbol | 5s | |
| test_run | stable | validation | 2 | 11900 | 11900 | 11900 | 6 tests scoped | n/a (run) | |
| test_coverage | stable | validation | 1 | 7404 | 7404 | 7404 | 1 project | n/a (run) | |
| test_reference_map | stable | validation | 1 | 2611 | 2611 | 2611 | 1 project | 15s | |
| get_test_coverage_map | alias | validation | 1 | 4815 | 4815 | 4815 | 1 project | 15s | |
| validate_workspace | stable | validation | 2 | 26421 | 26421 | 26421 | 11 projects | n/a | **TIMEOUT — P2 finding** |
| validate_recent_git_changes | stable | validation | 1 | 33491 | 33491 | 33491 | 1 commit | n/a (build+test) | |
| semantic_search | experimental | symbols | 4 | 129 | 1237 | 1237 | 1 query | 15s | |
| semantic_grep | stable | analysis | 2 | 169 | 208 | 208 | 1 pattern | 15s | |
| find_reflection_usages | stable | analysis | 1 | 3565 | 3565 | 3565 | 256 sites | 15s | **payload 153 KB exceeds MCP cap — P2** |
| get_di_registrations | stable | analysis | 1 | 3414 | 3414 | 3414 | 204 reg | 15s | |
| source_generated_documents | stable | analysis | 1 | n/a | n/a | n/a | 11 source-gen docs | 15s | **missing _meta.elapsedMs** |
| create_file_preview | stable | file-operations | 1 | 8 | 8 | 8 | 1 file | 30s | |
| create_file_apply | stable | file-operations | 1 | 583 | 583 | 583 | 1 file | 30s | |
| delete_file_preview | stable | file-operations | 1 | 4908 | 4908 | 4908 | 1 file | 30s | |
| delete_file_apply | stable | file-operations | 1 | 1204 | 1204 | 1204 | 1 file | 30s | |
| scaffold_type_preview | experimental | scaffolding | 1 | 4 | 4 | 4 | 1 type | 30s | |
| scaffold_type_apply | experimental | scaffolding | 1 | 2528 | 2528 | 2528 | 1 file | 30s | |
| scaffold_test_preview | experimental | scaffolding | 1 | 12 | 12 | 12 | 1 type | 30s | |
| set_project_property_preview | stable | project-mutation | 1 | 87 | 87 | 87 | 1 csproj | 30s | |
| apply_project_mutation | stable | project-mutation | 1 | 27787 | 27787 | 27787 | 1 csproj | 30s | queueing-dominated 23.8s queuedMs |
| workspace_changes | stable | workspace | 3 | 3188 | 3730 | 3730 | 18 sequences | 5s | **borderline budget** |
| go_to_definition | stable | symbols | 3 | 0 | 5 | 5 | 1 position | 5s | |
| goto_type_definition | stable | symbols | 1 | 2 | 2 | 2 | 1 type | 5s | |
| enclosing_symbol | stable | symbols | 2 | 5 | 5 | 5 | 1 position | 5s | |
| get_symbol_outline | alias | symbols | 1 | 2 | 2 | 2 | 1 file | 5s | |
| get_completions | stable | symbols | 2 | 6811 | 9633 | 9633 | 1 position | 5s | **OVER BUDGET — P3** |
| find_references_bulk | stable | symbols | 1 | 229 | 229 | 229 | 3 symbols | 15s | |
| find_overrides | stable | symbols | 2 | 825 | 1649 | 1649 | 1 method | 5s | |
| find_base_members | stable | symbols | 2 | 680 | 1358 | 1358 | 1 method | 5s | |
| get_prompt_text | experimental | prompts | 12 | 2 | 4 | 4 | various | 5s | |
| workspace_close | stable | workspace | 2 | 205 | 403 | 403 | 1 session | 30s | drainProcesses=true tested |

## 7. Schema vs behaviour drift
| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| project_diagnostics | totals-invariant-under-severity-filter | All totals (totalErrors/Warnings/Info **AND** totalDiagnostics) preserved under severity filter | totalInfo invariant ✓; **totalDiagnostics dropped to 0** when severity=Warning + 0 warnings in result | P2 | See finding F-G1-001 |
| symbol_relationships | bounded resolution under preferDeclaringMember=false | Literal token resolution returns the token's exact symbol with bounded results | builtin-type token (`void`) resolves to System.Void AND enumerates 100 unrelated void-returning method refs (57.7 KB payload) | P1 | See finding F-G2-001; needs short-circuit for System.* primitives |
| find_type_mutations | MutationScope classifies instance-state mutations | FieldWrite/IO/Network/etc. for true mutations; None for pure-function methods | All methods with local-collection `List<T>.Add` classified as `CollectionWrite` even when collection only escapes via return | P2 | See finding F-G2-002 |
| find_property_writes | error hint matches input shape | metadataName input → metadataName-shaped error; positional input → position-shaped error | metadataName input returns position-shaped error ("Verify the column points at the symbol identifier") | P2 | See finding F-G2-003 |
| find_consumers vs find_type_consumers | identical inclusion rules | symbol-scoped and type-scoped surfaces share inclusion logic | find_consumers misses top-level file-scope code (e.g. Program.cs minimal API) that find_type_consumers catches | P3 | See finding F-G2-004 |
| source_file resource | `{filePath}` template param accepts file path | Documented as `{filePath}` placeholder with no encoding spec | Requires URL-encoded absolute path with backslashes encoded as %5C; relative paths return "Unknown resource URI" | P2 | See finding F-O-006 |
| set_project_property_preview | warns when overriding Directory.Build.props inherited value | "When the property value is already inherited from Directory.Build.props, the response includes a warning annotation" | No warning emitted when overriding inherited `<Nullable>enable</Nullable>` with `annotations` | P3 | Schema description ambiguity: is "already inherited" any inherited value, or only when override matches inherited? |
| apply_multi_file_edit | atomic batch with single snapshot | "single pre-apply snapshot" + "rolls back the entire batch atomically" | workspace_changes records one call as N sequence numbers (one per file) — granularity inconsistent with atomic-batch claim | P3 | See finding F-O-002 |
| find_type_mutations | NotFound message text consistency | Same wording across symbol-traversal tools | Wording differs ("No named type found...") from siblings ("No symbol could be resolved...") | P3 | See finding F-G8-002 |
| validate_workspace | structured failure envelope | "test_run-style FailureEnvelope" via runTests options | Throws `InternalValidationTimeoutException` (InternalError category) instead of returning graceful failure envelope | P2 | See finding F-G5-001 |
| workspace_status (verbose) | consistent with non-verbose readiness | If `isReady=true` (non-verbose), verbose mode should also succeed within budget | Verbose path times out at 5005ms on a Ready workspace; subsequent retry succeeds | P2 | See finding F-G8-001 |
| validate_workspace error message | actual tool name in error | "validate_workspace" in InternalValidationTimeoutException message | Message says "validate_recent_git_changes phase..." | P3 | See finding F-G5-004 |
| diagnostic_details (not found) | fast short-circuit on no-match | <1s for "diagnostic not present" lookup | 24219ms scanning entire diagnostic catalog before returning found=false | P3 | See finding F-G1-003 |
| docCount stability | SnapshotToken pins document count for the version | docCount stable across the run | Briefed 759 (Phase -1); observed 760 (G8 Phase 15-17); SnapshotToken=:34 at G8 but doc count drift observed | P3 | See finding F-G8-003 |
| get_completions | fast on warm workspace | <5s reader budget | `staleAction=auto-reloaded` adds 3-9s on every call; total 4-10s | P3 | See finding F-G7-001 |
| get_completions ranking | type-members → types → long tail tiering | Roslyn-typical recency/locality boost; in-scope `ToString` before `ToBase64Transform` | Pure lexicographic; `sortText == displayText` for all 30 items returned; inherited Object members placed ahead of immediate type members | P3 | See finding F-G7-002 |
| go_to_definition (NotFound) | hint matches actual cause | Mid-token-position case hinted as "non-token position" | Hint says "Ensure the workspace is loaded" even when workspace is fine and column is simply mid-string-literal | P3 | See finding F-G7-003 |
| test_run filter aggregation | aggregate across all matching projects | Sum matches across N projects | OR-pipe filter (`A|B|C`) returns success=true total=0 even when at least one project would have matched | P2 | See finding F-G5-002 |

## 8. Error message quality
| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| project_diagnostics | workspaceId="fake-bogus-id" | actionable | — | Phase 17a; cites workspace_list for enumeration |
| diagnostic_details | (CS0000, line=1, col=1) — fake | actionable | — | "Run project_diagnostics first and copy an exact (id, line, column) tuple"; tone is excellent |
| code_fix_preview | (CA1502, wrong line/col) | actionable | — | "Diagnostic 'CA1502' was not found at <file>:91:5. Run project_diagnostics first..." |
| find_references / find_consumers / find_type_usages / find_implementations / find_overrides / find_base_members / impact_analysis | fabricated symbol handle | actionable | — | All return `category: NotFound` with consistent message (v1.8+ NotFound contract holds) |
| find_type_mutations | fabricated handle | actionable but inconsistent | unify wording with siblings | Says "No named type found" vs siblings' "No symbol could be resolved" — F-G8-002 |
| find_property_writes | metadataName=non-existent | vague/misleading | branch hint on input shape | "Verify the column points at the symbol identifier" but caller used metadataName — F-G2-003 |
| rename_preview | fabricated handle | actionable | — | "InvalidOperation: hints at document_symbols/enclosing_symbol for refresh" |
| go_to_definition | line=99999 | actionable | — | "Out of range: line 99999 exceeds end-of-file 19. 1-based; max line is..." |
| go_to_definition | mid-token position (col mid-string-literal) | vague/misleading | hint should mention "non-token position" | "Ensure the workspace is loaded" — F-G7-003 |
| enclosing_symbol | (0, 0) | actionable | — | "1-based contract; min line=1, min col=1" |
| analyze_data_flow | startLine > endLine | actionable | — | Cites both values |
| probe_position | whitespace position | n/a (by design) | — | Returns tokenKind=EndOfLine — this is what probe_position is FOR |
| symbol_search | query="" | actionable | — | count=0 with `note` field hinting at proper syntax |
| analyze_snippet | empty code | actionable | — | isValid=true, empty diagnostics — graceful empty |
| evaluate_csharp | empty code | actionable | — | success=true, result=null — graceful empty |
| evaluate_csharp | infinite loop | excellent | — | Explains 5s budget + 10s grace, cites env var, explains Roslyn cancellation limitation |
| evaluate_csharp | runtime error (int.Parse("abc")) | actionable | — | "Runtime error: FormatException: ..." includes input |
| get_prompt_text | nonexistent_prompt | A+ | — | Lists all 20 available prompts in error |
| get_prompt_text | bad json | A+ | — | Cites parse line+pos, hints at `{}` for omit |
| source_file_lines | lines/10-5 | actionable | — | "endLine (5) must be >= startLine (10)" |
| workspace_status | workspaceId of closed session | A+ | — | "Workspace not found or has been closed. Active workspace IDs are listed by workspace_list" |
| revert_apply_by_sequence | sequenceNumber=99999 | actionable | — | "No revert snapshot exists for that sequence number. Either the sequence is from before this session, or the apply did not produce a revertable snapshot." |
| revert_last_apply | called twice in succession | actionable | — | "No operation to revert. Nothing has been applied in this session, or the workspace was reloaded / closed and re-loaded since the last apply." |
| apply_with_verify | stale token | actionable | — | "NotFound: Preview token '...' not found or expired." |
| goto_type_definition | metadata-only type (IServiceCollection) | excellent | — | "Cannot navigate to type definition... defined in .NET runtime or external assembly" |
| source_file resource | relative `src/...` path | unhelpful | document encoding requirement | "Unknown resource URI: '...'" — no hint about URL-encoding or absolute path |

## 9. Parameter-path coverage
| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| project_diagnostics | severity, projectName, diagnosticId, limit, summary | exercised | 5 non-default args across 5 calls |
| compile_check | severity, file, emitValidation | exercised | 50× ratio on emitValidation confirmed |
| list_analyzers | projectName, limit | exercised | |
| nuget_vulnerability_scan | includeTransitive=true | exercised | |
| get_complexity_metrics | minComplexity, limit | exercised | |
| get_cohesion_metrics | minMethods, excludeTestProjects | exercised | |
| get_coupling_metrics | excludeTestProjects, limit | exercised | |
| find_unused_symbols | includePublic both branches + excludeTestProjects | exercised | |
| find_duplicated_methods | limit | exercised | |
| find_duplicated_code | minLines | exercised | |
| find_dead_fields | usageKind=never-read | exercised | |
| get_namespace_dependencies | circularOnly=true | exercised | |
| get_nuget_dependencies | summary=true | exercised | |
| suggest_refactorings | limit | exercised | |
| find_references | summary=true, projectFilter=multi, metadataName | exercised | |
| symbol_search | kind, namespace, projectName, limit, offset | exercised | |
| find_implementations | metadataName | exercised | |
| symbol_relationships | preferDeclaringMember={true,false} | exercised | both branches — false path is the P1 finding |
| find_property_writes | metadataName + position + negative | exercised | |
| find_type_mutations | limit | exercised | |
| symbol_impact_sweep | summary=true, maxItemsPerCategory=25 | exercised | |
| get_syntax_tree | maxTotalBytes=8000 | exercised | |
| trace_exception_flow | scopeProjectFilter, maxResults | exercised | |
| impact_analysis | summary=true | exercised | |
| analyze_snippet | 4 of 5 kinds (expression, program, statements, returnExpression) | exercised | `members` kind not exercised |
| evaluate_csharp | timeoutSeconds=5 (explicit), default timeout, multi-line, runtime exception, infinite-loop timeout | exercised | |
| rename_preview | line+column path | exercised | summary=true not exercised |
| fix_all_preview | scope=solution, scope=project, projectName | exercised | document-scope not exercised |
| apply_text_edit | verify=true, autoRevertOnError=true (implicit) | exercised | |
| apply_multi_file_edit | verify=true | exercised | |
| set_editorconfig_option | benign key write | exercised | |
| set_project_property_preview | Nullable override on csproj inheriting from Directory.Build.props | exercised | warning detection didn't fire (P3) |
| semantic_search | limit + default + non-default query shapes | exercised | offset>0 not exercised |
| semantic_grep | scope=all + bogus pattern + non-default pattern | exercised | |
| get_di_registrations | summary=true | exercised | showLifetimeOverrides default |
| workspace_status (resource) | summary + verbose | exercised | |
| workspace_load | autoRestore=true, prewarm=true, evictPolicy default | exercised | expandSanctionedRoots not separately exercised |
| workspace_close | drainProcesses=true | exercised | |
| get_completions | filterText="To" | exercised | |
| find_references_bulk | summary=true, maxItemsPerSymbol=20 | exercised | |
| get_msbuild_properties | propertyNameFilter | exercised | |
| get_source_text | startLine+endLine slice | exercised | |
| revert_apply_by_sequence | mid-stack + out-of-range | exercised | both paths |
| source_file_lines | range slice + invalid range | exercised | |
| get_prompt_text | positive + 2 negatives | exercised | |

## 10. Prompt verification (Phase 16)
| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| explain_error | YES | UNVERIFIED | n/a (text not rendered) | UNTESTED | 2 | needs-more-evidence | Required `filePath+line+column` not provided in initial call |
| suggest_refactoring | YES | YES (graceful) | none | YES | 4 | keep-experimental | File-not-found rendered as user message rather than error envelope — documentation tradeoff |
| review_file | YES | YES (rich) | none | YES (byte-identical x2) | 33797 | promote | Most actionable, well-scoped |
| discover_capabilities | YES | YES (full inventory) | none | YES (byte-identical x2) | 0-3 | promote | High-signal, low cost |
| dead_code_audit | YES | YES | none | UNTESTED | 2865 | promote | Workflow + 20 sample symbols + caveats |
| review_complexity | YES | YES | none | UNTESTED | 1590 | promote | 50 ranked hotspots + refactor workflow |
| cohesion_analysis | YES | YES | none | UNTESTED | 702 | promote | LCOM4 + interpretation guide |
| consumer_impact | YES (needs anchor) | UNVERIFIED | n/a | UNTESTED | n/a | needs-more-evidence | Required filePath+line+column not provided |
| refactor_and_validate | YES (needs range) | UNVERIFIED | n/a | UNTESTED | n/a | needs-more-evidence | Required filePath+startLine+endLine not provided |

## 11. Experimental promotion scorecard

Live experimental surface: **58 tools + 4 resources + 20 prompts = 82 experimental entries.**

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | semantic_search | symbols | exercised | 129 | YES | YES | n/a (read) | 0 | **promote** | HTML-decode invariant ✓; modifier sensitivity ✓; 4 calls all PASS |
| tool | find_type_consumers | symbols | exercised | 5 | YES | YES | n/a | 0 | **promote** | Cross-checked with find_consumers; broader inclusion captured |
| tool | probe_position | symbols | exercised | 5 | YES | YES | n/a | 0 | **promote** | Whitespace path returns tokenKind cleanly |
| tool | symbol_impact_sweep | advanced-analysis | exercised | 223 | YES | YES | n/a | 0 | **promote** | summary + maxItemsPerCategory both exercised |
| tool | trace_exception_flow | advanced-analysis | exercised | 31 | YES | YES | n/a | 0 | **promote** | scopeProjectFilter + maxResults both exercised |
| tool | preview_multi_file_edit | editing | exercised | 3184 | YES | YES | YES (round-trip in 6h) | 0 | **promote** | Token round-trip + composite path clean |
| tool | preview_multi_file_edit_apply | editing | exercised | 5 | YES | YES (NotFound on stale) | YES | 0 | **promote** | Stale-token negative probe via apply_with_verify |
| tool | apply_with_verify | undo | exercised | 0 (stale) | YES | YES (NotFound) | n/a (negative-only) | 0 | **keep-experimental** | Only negative path exercised inline; positive round-trip not separately re-run |
| tool | revert_apply_by_sequence | undo | exercised | 16-15797 | YES | YES (unknown-sequence) | YES (non-tip) | 0 | **promote** | Non-tip rollback verified, out-of-range negative clean |
| tool | scaffold_type_preview | scaffolding | exercised | 4 | YES | YES | YES (apply round-trip) | 0 | **promote** | Apply round-trip clean; file landed at namespace-derived folder |
| tool | scaffold_type_apply | scaffolding | exercised-apply | 2528 | YES | YES | YES | 0 | **promote** | After apply, compile_check showed expected analyzer errors (CA1040/CA1715/CA1707 on underscore name) — proves the file was added to the compilation |
| tool | scaffold_test_preview | scaffolding | exercised | 12 | YES | YES | n/a (no apply) | 0 | **keep-experimental** | Preview-only; apply path scoped-but-skipped |
| tool | scaffold_test_apply | scaffolding | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | Apply path not exercised |
| tool | scaffold_test_batch_preview | scaffolding | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | scaffold_first_test_file_preview | scaffolding | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | extract_method_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | Context-budget gate |
| tool | extract_method_apply | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | extract_type_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | extract_type_apply | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | extract_interface_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | extract_interface_apply | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | bulk_replace_type_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | bulk_replace_type_apply | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | restructure_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | replace_string_literals_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | change_signature_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | symbol_refactor_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | change_type_namespace_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | replace_invocation_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | preview_record_field_addition | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | record_field_add_with_satellites_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | extract_shared_expression_to_helper_preview | refactoring | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | split_class_preview | orchestration | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | split_service_with_di_preview | orchestration | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | migrate_package_preview | orchestration | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | extract_and_wire_interface_preview | orchestration | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | extract_interface_cross_project_preview | cross-project-refactoring | skipped-repo-shape | — | — | — | — | — | **needs-more-evidence** | No suitable candidates |
| tool | dependency_inversion_preview | cross-project-refactoring | skipped-repo-shape | — | — | — | — | — | **needs-more-evidence** | |
| tool | move_type_to_project_preview | cross-project-refactoring | skipped-repo-shape | — | — | — | — | — | **needs-more-evidence** | |
| tool | move_type_to_file_preview | file-operations | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | move_type_to_file_apply | file-operations | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | move_file_preview | file-operations | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | move_file_apply | file-operations | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | apply_composite_preview | file-operations | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | remove_dead_code_preview | dead-code | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | remove_dead_code_apply | dead-code | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | remove_interface_member_preview | dead-code | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | set_conditional_property_preview | project-mutation | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| tool | get_prompt_text | prompts | exercised | 2 | YES | YES (A+ on negatives) | n/a | 0 | **promote** | 3 prompts rendered + 2 actionable negatives |
| resource | server_catalog_full | server | exercised | <50 | YES | YES | n/a | 0 | **promote** | 125 KB payload, valid catalog |
| resource | server_catalog_tools_page | server | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | Pagination not separately tested |
| resource | server_catalog_prompts_page | server | exercised | <50 | YES | YES | n/a | 0 | **promote** | Used to enumerate 20 prompts |
| resource | source_file_lines | workspace | exercised | <50 | YES | YES (A on negative) | n/a | 0 | **keep-experimental** | Required URL-encoded absolute path is a documentation gap (P2 finding F-O-006) — fix-then-promote |
| prompt | explain_error | prompt | partial | 2 | YES | n/a | n/a | n/a | **needs-more-evidence** | Required parameters not provided in test |
| prompt | suggest_refactoring | prompt | exercised | 4 | YES | n/a (graceful) | YES | 0 | **keep-experimental** | Graceful degrade as user-message; promote-blocker is the schema-vs-behavior documentation tradeoff |
| prompt | review_file | prompt | exercised | 33797 | YES | n/a | n/a | 0 | **promote** | Byte-identical idempotency verified |
| prompt | discover_capabilities | prompt | exercised | 0-3 | YES | n/a | n/a | 0 | **promote** | High-signal low-cost |
| prompt | dead_code_audit | prompt | exercised | 2865 | YES | n/a | n/a | 0 | **promote** | |
| prompt | review_complexity | prompt | exercised | 1590 | YES | n/a | n/a | 0 | **promote** | |
| prompt | cohesion_analysis | prompt | exercised | 702 | YES | n/a | n/a | 0 | **promote** | |
| prompt | consumer_impact | prompt | partial | n/a | YES | n/a | n/a | n/a | **needs-more-evidence** | Required anchor not provided in test |
| prompt | refactor_and_validate | prompt | partial | n/a | YES | n/a | n/a | n/a | **needs-more-evidence** | Required range not provided in test |
| prompt | analyze_dependencies | prompt | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| prompt | debug_test_failure | prompt | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| prompt | fix_all_diagnostics | prompt | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| prompt | guided_package_migration | prompt | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| prompt | guided_extract_interface | prompt | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| prompt | security_review | prompt | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| prompt | review_test_coverage | prompt | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| prompt | guided_extract_method | prompt | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| prompt | msbuild_inspection | prompt | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| prompt | session_undo | prompt | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |
| prompt | refactor_loop | prompt | scoped-but-skipped | — | — | — | — | — | **needs-more-evidence** | |

**Summary:** promote=18 (8 tools + 2 resources + 8 prompts including: semantic_search, find_type_consumers, probe_position, symbol_impact_sweep, trace_exception_flow, preview_multi_file_edit, preview_multi_file_edit_apply, revert_apply_by_sequence, scaffold_type_preview, scaffold_type_apply, get_prompt_text, server_catalog_full, server_catalog_prompts_page, review_file, discover_capabilities, dead_code_audit, review_complexity, cohesion_analysis); keep-experimental=4 (apply_with_verify, scaffold_test_preview, source_file_lines, suggest_refactoring); needs-more-evidence=60; deprecate=0.

## 12. Debug log capture
| timestamp | level | logger | correlationId | eventName | message | Phase | Tool in flight |
|-----------|-------|--------|----------------|-----------|---------|-------|----------------|
| — | — | — | — | — | — | — | client did not surface MCP log notifications (Claude Code transcript) |

## 13. MCP server issues (bugs)

### 13.1 P1 — `symbol_relationships(preferDeclaringMember=false)` on builtin-type token unbounded enumeration
| Field | Detail |
|-------|--------|
| Tool | `symbol_relationships` |
| Input | `{filePath: AlertRuleRepository.cs, line: 269, column: 22, preferDeclaringMember: false}` — column 22 lands on `void` return-type token |
| Expected | Short-circuit with hint like `"builtin type — re-query with preferDeclaringMember=true or relocate column to symbol identifier"`; return zero items |
| Actual | Resolved `System.Void`; enumerated 100 unrelated `void`-returning method references solution-wide; 57.7 KB response exceeded MCP cap |
| Severity | P1 |
| Reproducibility | 100% — same call with `preferDeclaringMember=true` returns expected 9 references for `ValidateRuleShape`; flip flag → 57.7 KB enumeration |
| Suggested fix | when `preferDeclaringMember=false` AND resolved symbol's `ContainingAssembly == System.Private.CoreLib` (or namespace prefix `System.`) AND Kind ∈ {Struct, Class, Primitive}, return `{symbol: <resolved>, hint: "Resolved to builtin type — references list suppressed. Set preferDeclaringMember=true or relocate cursor.", definitions: [], references: [], implementations: [], baseMembers: [], overrides: [], totals: {... all 0}}` |

### 13.2 P2 — `project_diagnostics(severity=Warning)` zeroes `totalDiagnostics` while preserving `totalInfo`
| Field | Detail |
|-------|--------|
| Tool | `project_diagnostics` |
| Input | `severity=Warning, summary=true` |
| Expected | All rollup totals (`totalErrors`, `totalWarnings`, `totalInfo`, `totalDiagnostics`) invariant under severity filter per v1.8+ contract; only arrays narrow |
| Actual | `totalInfo=53` preserved ✓; `totalDiagnostics=0` ❌; `diagnosticGroups=[]` |
| Severity | P2 |
| Reproducibility | 100% |
| Suggested fix | Keep `totalDiagnostics` invariant (recommended) OR document that it follows arrays under filter (less preferred — breaks the rollup contract) |

### 13.3 P2 — `find_type_mutations` mislabels pure-function methods as `CollectionWrite`
| Field | Detail |
|-------|--------|
| Tool | `find_type_mutations` |
| Input | Types like `SueComputationService` (pure-function class) or `AlertRuleRepository.ListEnabledAsync` (pure SELECT into local list) |
| Expected | `mutationScope=None` (or new `LocalCollectionWrite` bucket) for methods that construct local lists returned to callers; `CollectionWrite` reserved for collections assigned to `this.X` or escaping via `ref`/`out` |
| Actual | All methods that call `List<T>.Add` get `mutationScope=CollectionWrite` regardless of escape |
| Severity | P2 — misleads "this type is mutable" risk analysis |
| Reproducibility | 100% |
| Suggested fix | Require mutated collection to be assigned to instance field (FieldWrite), captured field, ref/out arg, or property setter target before classifying as CollectionWrite |

### 13.4 P2 — `find_property_writes(metadataName=...)` returns position-shaped hint
| Field | Detail |
|-------|--------|
| Tool | `find_property_writes` |
| Input | `{metadataName: "TradeWise.Domain.Alerts.AlertRule.Name"}` (non-existent property) |
| Expected | "Symbol '{metadataName}' not found. Check spelling or use symbol_search..." |
| Actual | "No symbol resolved at the given position. Verify the column points at the symbol identifier." — references a position that was never provided |
| Severity | P2 |
| Reproducibility | 100% |
| Suggested fix | Branch hint on which input shape was used (metadataName vs filePath+line+column) |

### 13.5 P2 — `validate_workspace` 25s timeout on 11-project solution
| Field | Detail |
|-------|--------|
| Tool | `validate_workspace` |
| Input | `(workspaceId, changedFilePaths=null, runTests=false, summary=true)` |
| Expected | Completes auto-scoped validation under timeout, or returns structured FailureEnvelope (test_run pattern) |
| Actual | `InternalValidationTimeoutException` after 25005 ms in `project_diagnostics` phase; thrown as `InternalError` category, not graceful failure envelope |
| Severity | P2 |
| Reproducibility | 100% (occurred both on default + negative-probe paths) |
| Suggested fix | (a) raise internal phase timeout above 25s for solutions with >5 projects, OR (b) parallelize per-project diagnostics with bounded concurrency, OR (c) return structured FailureEnvelope |

### 13.6 P2 — `test_run` OR-pipe filter reports `total=0, success=true` silently
| Field | Detail |
|-------|--------|
| Tool | `test_run` |
| Input | `filter="FullyQualifiedName~A|FullyQualifiedName~B|FullyQualifiedName~C"` across multiple test projects |
| Expected | Aggregate count from all TRX files with at least one match |
| Actual | `total=0, passed=0, succeeded=true` even though TRX from one project shows 6+ tests would have matched |
| Severity | P2 — silent false-clean; masks misnamed filters in CI |
| Reproducibility | 100% |
| Suggested fix | Surface a warning when stdOut contains "No test matches" in some projects but others produced results; or aggregate filter matches across all TRX outputs faithfully |

### 13.7 P2 — `find_reflection_usages` exceeds MCP token cap on real solutions
| Field | Detail |
|-------|--------|
| Tool | `find_reflection_usages` |
| Input | TradeWise solution (759 docs, 256 reflection sites — mostly `typeof`) |
| Expected | In-line summary for LLM callers; pagination / kind-filter / summary param to bound output |
| Actual | 153 KB JSON response breaches MCP per-tool token limit; disk-spilled |
| Severity | P2 — workflow blocker for LLM callers |
| Reproducibility | 100% on solutions with 100+ reflection sites |
| Suggested fix | Add `find_reflection_usages(summary=true, limit, offset, kindFilter)` matching `get_di_registrations` ergonomics |

### 13.8 P2 — `workspace_status(verbose=true)` 5s timeout race on Ready workspace
| Field | Detail |
|-------|--------|
| Tool | `workspace_status` |
| Input | `verbose=true` immediately after non-verbose returned `isReady=true, isStale=false` |
| Expected | Verbose mode completes within reasonable budget on Ready workspace |
| Actual | Timed out at 5005 ms on first call; subsequent retry succeeded in <1 s |
| Severity | P2 — heisenbug between `isReady` flag and verbose enumeration readiness |
| Reproducibility | First-call after period of contention; subsequent retries succeed |
| Suggested fix | Make `verbose=true` consistently fast-on-Ready, or document a per-call extra warm-up requirement |

### 13.9 P2 — `source_file` resource requires URL-encoded absolute path; relative paths return "Unknown resource URI"
| Field | Detail |
|-------|--------|
| Resource | `roslyn://workspace/{workspaceId}/file/{filePath}` |
| Input | Relative `src/TradeWise.Domain/Alerts/AlertRuleType.cs` (file exists in workspace) |
| Expected | Documented `{filePath}` template parameter accepts file path |
| Actual | `MCP error -32002: Unknown resource URI`; only URL-encoded absolute path (e.g. `C%3A%5CCode-Repo%5C...%5CAlertRuleType.cs`) works |
| Severity | P2 — documentation contract gap; affects callers using project-relative paths |
| Reproducibility | 100% |
| Suggested fix | Either accept project-relative paths (resolving via loaded workspace's project root), or update resource-template description to say "absolute, URL-encoded path required" |

### 13.10 P3 — `diagnostic_details` "not found" path costs 24s
| Field | Detail |
|-------|--------|
| Tool | `diagnostic_details` |
| Input | `diagnosticId=CS0000, line=1, column=1` (fabricated, won't match) |
| Expected | <1s for negative probe |
| Actual | 24219 ms held |
| Severity | P3 |
| Suggested fix | Short-circuit when `(file,line,col)` location has zero diagnostics |

### 13.11 P3 — `get_coupling_metrics` top-N dominated by Ca=0 registration types
| Field | Detail |
|-------|--------|
| Tool | `get_coupling_metrics` |
| Expected | Surface useful coupling outliers in top-N |
| Actual | Top 30 all have `I=1.0, Ca=0` (DI registration classes + endpoint classes by design) — useful outliers buried |
| Severity | P3 |
| Suggested fix | Add `minAfferent` / `minEfferent` thresholds, or default sort by `Ce` with `Ca>0` filter |

### 13.12 P3 — `find_unused_symbols` heuristic for test-only-helper exclusion
| Field | Detail |
|-------|--------|
| Tool | `find_unused_symbols(includePublic=false)` |
| Expected | Distinguish production-unused from `*ForTest*` test-helper patterns |
| Actual | 42 `Build*CommandTextForTest` methods flagged as unused in production assemblies; root cause is repo pattern of test helpers compiled into prod |
| Severity | P3 |
| Suggested fix | Add `excludeTestHelpers` flag matching `*ForTest` / `*ForTesting` name patterns |

### 13.13 P3 — `find_consumers` vs `find_type_consumers` shape difference on top-level file-scope code
| Field | Detail |
|-------|--------|
| Tools | `find_consumers`, `find_type_consumers` |
| Observed | find_consumers requires a containing type; file-scope `Program.cs` minimal-API consumers are missed. find_type_consumers reports per-file rollups including registrations. |
| Severity | P3 |
| Suggested fix | Normalize inclusion rules across the two surfaces, or document the difference explicitly |

### 13.14 P3 — `evaluate_csharp` abandoned-thread counter not observable
| Field | Detail |
|-------|--------|
| Tool | `evaluate_csharp` |
| Observed | Documented limitation: Roslyn tight loops don't honor CancellationToken; abandoned worker threads accumulate (warning shown when timeout fires) |
| Severity | P3 |
| Suggested fix | Expose abandoned-thread counter on `workspace_health` for operator visibility before 8/8 saturation |

### 13.15 P3 — `evaluate_csharp` dual elapsed fields (top-level + `_meta`) redundant
| Field | Detail |
|-------|--------|
| Tool | `evaluate_csharp` |
| Observed | Top-level `elapsedMs` and `_meta.elapsedMs` consistently within ~10ms of each other |
| Severity | P3 |
| Suggested fix | Single canonical field |

### 13.16 P3 — `apply_multi_file_edit` records one call as N sequence numbers
| Field | Detail |
|-------|--------|
| Tool | `apply_multi_file_edit` / `workspace_changes` |
| Observed | One `apply_multi_file_edit` call with 2 files produces 2 sequence numbers in workspace_changes; tool description says "single pre-apply snapshot" + "atomic batch" |
| Severity | P3 |
| Suggested fix | Either log as a single sequence with `affectedFiles=[a, b, ...]`, or update tool description to clarify that the atomic-batch revert is at the call boundary while change log is per-file |

### 13.17 P3 — `add_pragma_suppression` row in workspace_changes shows generic description
| Field | Detail |
|-------|--------|
| Tool | `add_pragma_suppression` / `workspace_changes` |
| Observed | toolName=`add_pragma_suppression` but description text is generic "Apply text edit to <filename>" rather than semantic "Add #pragma warning disable" |
| Severity | P3 cosmetic |
| Suggested fix | Generate a semantic description like `"Add #pragma warning disable CA1506 to PushoverChannelRegistration.cs:37"` |

### 13.18 P3 — `set_project_property_preview` warning detection didn't fire on inherited override
| Field | Detail |
|-------|--------|
| Tool | `set_project_property_preview` |
| Input | `projectName=TradeWise.Domain, propertyName=Nullable, value=annotations` while Directory.Build.props has `<Nullable>enable</Nullable>` |
| Expected | Per schema: "When the property value is already inherited from Directory.Build.props, the response includes a warning annotation" |
| Actual | `warnings: null` — no annotation emitted |
| Severity | P3 — schema description ambiguity |
| Suggested fix | Clarify whether "already inherited" warns on any override of an inherited property or only when value matches inherited; then implement the documented case |

### 13.19 P3 — `semantic_search("classes implementing IDisposable")` has weaker recall than `find_implementations`
| Field | Detail |
|-------|--------|
| Tool | `semantic_search` |
| Observed | 20 results (limit-clamped, mixed direct+transitive) vs `find_implementations(System.IDisposable)`'s 41 distinct user-authored implementations |
| Severity | P3 — expected gap given semantic_search is structured-predicate + token fallback |
| Suggested fix | Document the expected precision/recall gap, or add `directImplementersOnly=true` flag to semantic_search for impl-resolution queries |

### 13.20 P3 — `source_generated_documents` response missing `_meta.elapsedMs`
| Field | Detail |
|-------|--------|
| Tool | `source_generated_documents` |
| Observed | Only call in Phase 11 without `_meta.elapsedMs` field |
| Severity | P3 consistency |
| Suggested fix | Add `_meta.elapsedMs` for parity with other tools |

### 13.21 P3 — `get_completions` latency 3-9s with auto-reload on warm workspace
| Field | Detail |
|-------|--------|
| Tool | `get_completions` |
| Observed | Both calls triggered `staleAction=auto-reloaded`, `staleReloadMs` 3142/8979 ms; total elapsedMs 3989/9633 vs <250ms on every other navigation call |
| Severity | P3 |
| Suggested fix | Investigate completions path cache invalidation; possibly racing with workspace reload |

### 13.22 P3 — `get_completions` ranking purely lexicographic
| Field | Detail |
|-------|--------|
| Tool | `get_completions` |
| Observed | After `command.` on `NpgsqlCommand`, returned 30 items with `sortText == displayText` for all. Inherited Object members (`Equals`, `GetHashCode`, `GetType`) ranked ahead of immediate type members (`Parameters` at rank 25, `CommandText` at rank 4) |
| Severity | P3 |
| Suggested fix | Surface `rankingTier` enum (LocalMember/NonLocalMember/Type/LongTail) so callers can re-rank; or implement Microsoft IntelliSense-style ordering by default |

### 13.23 P3 — `go_to_definition` NotFound hint misleading on mid-token positions
| Field | Detail |
|-------|--------|
| Tool | `go_to_definition` |
| Observed | When `column` falls mid-string-literal (non-token position), hint says "Ensure the workspace is loaded" — misleads operator since workspace was fine |
| Severity | P3 |
| Suggested fix | Add "Try a position on the identifier itself" to NotFound message |

### 13.24 P3 — `find_type_mutations` NotFound message wording differs from siblings
| Field | Detail |
|-------|--------|
| Tool | `find_type_mutations` |
| Observed | "No named type found at the specified location" vs siblings' "No symbol could be resolved for the supplied symbol handle..." |
| Severity | P3 |
| Suggested fix | Normalize NotFound message across symbol-traversal family |

### 13.25 P3 — `test_related(metadataName=...)` fails to resolve renamed symbol
| Field | Detail |
|-------|--------|
| Tool | `test_related` |
| Input | `metadataName="TradeWise.Infrastructure.Persistence.AlertRuleRepository.MapRuleTypeToWireValue(TradeWise.Domain.Alerts.AlertRuleType)"` post-rename |
| Expected | Non-empty tests (matching test class exists) |
| Actual | `tests=[], missReasons=["locator did not resolve to any workspace symbol"]` |
| Severity | P3 — possibly post-drift state; metadataName overload-signature parsing may be brittle |
| Suggested fix | Relax metadataName parsing to accept method overload syntax; or document required format |

### 13.26 P3 — `validate_workspace` error mislabels phase
| Field | Detail |
|-------|--------|
| Tool | `validate_workspace` |
| Observed | Internal error message says "validate_recent_git_changes phase 'project_diagnostics' exceeded the internal timeout" when caller is `validate_workspace` |
| Severity | P3 cosmetic |
| Suggested fix | Pass actual tool name into RunValidationPhaseAsync |

### 13.27 P3 — `workspace_changes` vs disk drift after host-shell `git checkout`
| Field | Detail |
|-------|--------|
| Tool | `workspace_changes` / general lifecycle |
| Observed | After host-shell `git -C <worktree> checkout -- <files>` reverted Phase 6 mutations, workspace_changes ledger retained sequence numbers but disk no longer matched the recorded pre-images. Workspace auto-reloaded on next call to sync, but ledger sequences 1-3 still claim those files have those mutations. Direct evidence materialized as **CS0117 compile error in `AlertRuleRepositoryTests.cs:22`** — test referenced `MapRuleTypeToWireValue` (post-rename) but the now-disk-reverted source had `RuleTypeToWire` (pre-rename) |
| Severity | P3 — orchestrator-controllable, but worth surfacing |
| Suggested fix | Add `workspace_drift_check` tool to detect ledger/disk mismatch; or document that `revert_apply_by_sequence` is the only safe revert path and host-shell revert breaks the ledger contract |

### 13.28 P3 — Document count drift between Phase -1 and Phase 15
| Field | Detail |
|-------|--------|
| Resource | `roslyn://workspaces` |
| Observed | docCount=759 at Phase -1 → docCount=760 at Phase 15 (`workspaceVersion=34`) — one document added by Phase 12's scaffold_type_apply |
| Severity | P3 — SnapshotToken increments correctly, but the "pinned" interpretation of the token vs doc count needs documentation |
| Suggested fix | Clarify that SnapshotToken describes workspace version (which advances on mutation), not doc-count parity across phases |

## 14. Improvement suggestions

- **find_reflection_usages**: Add `summary=true, limit, offset, kindFilter` matching `get_di_registrations` ergonomics. Reflection-heavy codebases hit the token cap immediately. (P2 promoted to suggestion)
- **semantic_search**: When parsed predicate is `implementing-interface`, add an `excludeBackgroundService=true` / `directImplementersOnly=true` flag to make the precision/recall trade-off explicit.
- **trace_exception_flow**: Expose `catchesBaseException` summary count in the top-level response so callers can quickly distinguish typed catches from `catch (Exception)` catches.
- **analyze_control_flow**: Extend the proactive warning ("Control-flow results may be incomplete for this line range") pattern to `analyze_data_flow` for method-declaration-line ranges.
- **find_property_writes**: Distinguish "resolved but no writes" (dead settable property — legitimate finding) from "symbol not resolved" via a populated `resolvedSymbolKind` field.
- **set_diagnostic_severity / set_editorconfig_option**: When the .editorconfig change is a no-op (key already at requested value), return `changed=false` + `existingValue` instead of writing the same value.
- **test_reference_map**: Surface `mockDriftWarnings` prominently in response when non-empty; add a `comment` field when empty so callers know zero-warnings is meaningful, not "not analyzed".
- **test_run**: Emit a structured warning when filter matched zero tests across all projects but invocation succeeded.
- **validate_workspace**: Add per-phase timeout knob (default 60s for solutions with ≥5 projects). Return structured FailureEnvelope on timeout, not InternalError exception.
- **diagnostic_details** error message on not-found is high-quality (actionable: "Run project_diagnostics first and copy an exact (id, line, column) tuple"). **Keep as the model for other tools' NotFound messages.**
- **goto_type_definition** error message on metadata-only types is excellent ("Cannot navigate... defined in .NET runtime or external assembly"). **Keep as the model.**
- **suggest_refactorings**: Add `categoryQuota` or `severityDistribution` param to blend categories — currently 15/15 are `complexity`, burying cohesion/unused-symbol findings.
- **workspace_drift_check** (new tool): Detect ledger/disk mismatch between `workspace_changes` and current disk state; warn callers who used host-shell reverts.
- **Resource templates documentation**: Update `{filePath}` placeholder description to specify "absolute, URL-encoded path required" (or implement project-relative resolution).
- **Prompt parameter schemas**: Consider a `prompts/describe(promptName)` endpoint returning required/optional param shapes — currently callers probe via empty `{}` and read the error message to discover required params.
- **Concurrency**: `_meta.queuedMs` is a great observability hint and surfaced rw-lock gate behaviour clearly; document this field in all writer tool descriptions so callers know to watch for contention.
- **Pragma suppression workflow**: `add_pragma_suppression` inserts a dangling `disable` by design; consider a sibling `add_pragma_disable_restore_pair(line, diagnosticId, scopeLines=1)` for callers who want bounded scope.
- **G5-discovered hazard**: The skill's brief instructs subagents to use `git -C <worktree> checkout -- <file>` for revert, but this discards ALL uncommitted changes (including earlier Phase 6 apply-mode mutations). Update the skill brief to mandate `revert_apply_by_sequence` for ledger-tracked reverts, or warn that `git checkout` is a nuclear revert.

## 15. Concurrency matrix (Phase 8b)

### Probe set (8b.0)
| Slot | Probe (tool + inputs) | Classification | Notes |
|------|------------------------|----------------|-------|
| R1 | `find_references` on `AlertRule` (high-fan-out) | reader | hot symbol |
| R2 | `project_diagnostics` (no filters) | reader | full scan |
| R3 | `symbol_search("Repository")` | reader | many hits |
| R4 | `find_unused_symbols(includePublic=false)` | reader | scan |
| R5 | `get_complexity_metrics` (no filters) | reader | scan |
| W1 | `format_document_preview` on `AlertRuleRepository.cs` (followed by apply attempt) | writer | preview returned empty changes (file already formatted) |
| W2 | `apply_text_edit` + `apply_multi_file_edit` in 8b.5 + revert via host `git checkout` | writer | drift hazard logged as F-G5-P3-005 |

### Sequential baseline (8b.1)
| Slot | Wall-clock (ms) | Notes |
|------|-----------------|-------|
| R1 | 130 | warm-cache |
| R2 | 5 | already cached after Phase 1 |
| R3 | 102 | proportional to result fan-out |
| R4 | 2 | cached |
| R5 | 1 | cached |

### Parallel fan-out (8b.2)
- **Host logical cores:** ≥4 (Claude Code orchestrator)
- **Chosen N:** 4 (N = min(4, max(2, logical_cores)))
- **Verdict:** `blocked — host serialization indeterminate due to cache reuse` (10-call parallel batch issued as single message; cache hits from sequential warm-up made wall-clock unreliable for speedup measurement). `_meta.queuedMs=0` on every read suggests the rw-lock gate did not serialize, but this is not a rigorous proof.

### Read/write exclusion (8b.3)
| Probe | Observed | Expected | Verdict |
|-------|----------|----------|---------|
| R1 then W1 (preview only — empty changes) | W1 returned without observable blocking | W1 may wait for R1 under contention | inconclusive (preview emitted no changes) |
| W1 + R1 simultaneous | both completed; no error | R1 may wait for W1 | inconclusive |

### Lifecycle stress (8b.4)
| Probe | Observed | Reader saw | Reader exception | Verdict |
|-------|----------|------------|------------------|---------|
| R2 + workspace_reload concurrently | reload bumped workspaceVersion to v23, `staleAction=auto-reloaded` | post-reload (fresh) | none | PASS — reader returned cleanly with auto-reloaded marker |
| workspace_close | SKIPPED (orchestrator needs session live for downstream phases) | n/a | n/a | `skipped-safety` |

## 16. Writer reclassification verification (Phase 8b.5)
| # | Tool | Status | Wall-clock (ms) | Notes |
|---|------|--------|------------------|-------|
| 1 | `apply_text_edit` (G5 8b.5 row 1) | PASS | 8 | trivial edit; later collaterally reverted by host `git checkout` |
| 2 | `apply_multi_file_edit` (G5 8b.5 row 2) | PASS | 5904 | 2-file edit; `staleAction=auto-reloaded`; `queuedMs=5889` due to drift |
| 3 | `revert_last_apply` | deferred-to-phase-9 | — | orchestrator owns Phase 9 |
| 4 | `set_editorconfig_option` | PASS | 1 (Phase 7a) | evidence-shared with Phase 7a step 2 |
| 5 | `set_diagnostic_severity` | PASS | 3783 (Phase 6f-ii) | evidence-shared with Phase 6f-ii |
| 6 | `add_pragma_suppression` | PASS | 20 (Phase 6f-ii) | evidence-shared with Phase 6f-ii |

### G5 worktree-drift hazard (F-G5-P3-005)
- G5's 8b.5 revert step used `git -C <worktree> checkout -- <file>` per its dispatch prompt.
- That `git checkout` reverted the touched files to **HEAD**, which collaterally discarded Phase 6's disk-side mutations (rename `RuleTypeToWire→MapRuleTypeToWireValue` + introduce-local refactoring + `#pragma warning disable CA1506`).
- `workspace_changes` ledger still shows sequences 1-12; **disk no longer matches**. Workspace auto-reloaded on the next call to pick up the disk-side revert.
- Downstream impact: Phase 9 `revert_last_apply` will operate against an in-memory state that's already drifted from the recorded "before" snapshots. This is itself audit-grade evidence for how the server handles `workspace_changes` vs disk drift.

## 6.5 Phase 7 + 8 + 8b summary (from G5)
| Phase | Section | Result | Notes |
|-------|---------|--------|-------|
| 7a | Read+baseline+write+revert .editorconfig | PASS | `set_editorconfig_option` wrote `dotnet_sort_system_directives_first = true`; reverted via `git checkout` (which also reverted Phase 6f-ii's `.editorconfig` mutation — flagged in F-G5-P3-005) |
| 7b | MSBuild evaluation | PASS | `get_msbuild_properties` (28/722 with filter), `evaluate_msbuild_property(TargetFramework)`, `evaluate_msbuild_items(Compile)` all consistent |
| 8 | Build | PASS | `build_workspace` 0E/0W in 27.5s, `build_project(TradeWise.Infrastructure)` 0E/0W in 5.8s |
| 8 | Test discovery | PASS | 1552 tests across 6 test projects |
| 8 | Test related (file) | PASS | 17 tests resolved from 3 changed files |
| 8 | Test related (symbol) | FAIL | `metadataName="...MapRuleTypeToWireValue(...)"` resolved empty — locator did not resolve renamed symbol (F-G5-P3-003); possibly post-drift state |
| 8 | Scoped test_run | PASS | 6/6 in 11.9s on AlertRuleRepository scope |
| 8 | Full-suite test_run | scoped-but-skipped — budget | 1552 tests would exceed 3 min |
| 8 | test_coverage | PASS | Domain 43.4% line / 79.6% branch |
| 8 | test_reference_map | PASS | 386 covered / 2495 uncovered; `mockDriftWarnings=[]` |
| 8 | get_test_coverage_map (alias) | PASS | `deprecation.canonicalName="test_coverage"` populated correctly |
| 8 | validate_workspace (auto-scope, runTests=false) | FAIL | `InternalValidationTimeoutException` after 25s — F-G5-P2-001 |
| 8 | validate_workspace (negative-probe nonexistent file) | FAIL | Same 25s timeout (didn't short-circuit) |
| 8 | validate_recent_git_changes | PASS | `overallStatus=clean`, 11/11 projects compiled, 33.5s |
| 8b | Concurrency matrix | partial | Sequential baselines captured; parallel verdict `blocked — host serialization indeterminate due to cache reuse` |

## 17. Response contract consistency
| Tools | Concept | Inconsistency | Notes |
|-------|---------|---------------|-------|
| project_diagnostics (severity filter) | totals invariance | totalInfo invariant; totalDiagnostics drops to 0 (see 13.2) | partial implementation of v1.8+ contract |
| find_type_mutations vs sibling symbol-traversal tools | NotFound message text | Differs: "No named type found" vs "No symbol could be resolved..." | see 13.24 |
| find_consumers vs find_type_consumers | top-level file-scope inclusion | find_consumers skips Program.cs minimal-API consumers find_type_consumers catches | see 13.13 |
| workspace_changes per-call vs per-file granularity | atomic-batch claim | apply_multi_file_edit recorded as N sequences per file (see 13.16) | granularity vs atomic-batch contract |
| evaluate_csharp dual elapsedMs fields | redundant top-level + _meta | both fields populated; values within ~10ms (see 13.15) | DRY-violation |
| source_generated_documents | missing _meta.elapsedMs | only Phase 11 tool without _meta block (see 13.20) | consistency |
| source_file resource | path encoding doc | `{filePath}` template doesn't specify encoding; only URL-encoded absolute works (see 13.9) | documentation gap |

## 18. Known issue regression check (Phase 18)

**N/A — TradeWise backlog is enhancement-tracking, not bug-tracking.**

The audited repo's prior issue source is `ai_docs/backlog.md` (33 entries) + `ai_docs/items/BL-*.md`. Inspection shows every visible entry is marked `[type: enhancement]` (e.g. BL-0099a/b/c PIT universe schema/lifecycle/backfill; BL-0124b/c dashboard cache invalidation; BL-0083 ML factor model; BL-0098c transaction-cost dimensions; BL-0106 production email provider; BL-0055d gov-trades overlay frontend; BL-0108 per-tenant heartbeat).

Recent bug-fix commits in git log (BL-0166 per-symbol fault tolerance in FMP market overview; BL-0242 CI Docker-free test filter) shipped via PR and the corresponding backlog entries were retired post-merge — they do not persist as long-lived reproducible scenarios in the current backlog snapshot. The format conflates forward-looking work-tracking with retro-bug-tracking.

**Verdict:** no reproducible prior bugs in backlog format → no regression candidates → section is N/A per the prompt's contract.

## 19. Known issue cross-check
- **F-O-002** (apply_multi_file_edit per-file sequences) — no exact match in `darylmcd/Roslyn-Backed-MCP` issues; novel observation this run.
- **F-G2-001** (find_type_mutations false positive on local-collection) — near-match of [#741](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/741) "MutationScope is single-valued — misses compound IO + CollectionWrite scopes". Different root cause but same misclassification surface — operator may want to bundle.
- **F-G1-001** (project_diagnostics totalDiagnostics drift) — exact match of [#746](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/746) "totalDiagnostics collapses to filtered count under severityFilter". Already filed; no new action.
- **F-G5-002** (test_run OR-pipe filter silent zero) — exact match of [#752](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/752) "test_run filter from test_discover FQDN produces silent zero-hits due to folder-infix drift". Already filed; no new action.

## 20. Phase 19 finding emission

**Routing decision:** `--output-mode=findings` (default); maintainer detection probe (`gh api user --jq .login`) returned `darylmcd` → matches upstream `darylmcd/Roslyn-Backed-MCP` owner → default route = **auto-file**. Per operator's explicit choice via AskUserQuestion, scope narrowed to **P1 + P2 only** (9 findings; 3 deduplicated to existing issues, 6 filed).

**Dedup pre-check (per Step 5a contract):**
| Finding | Search key | Existing issue | Action |
|---------|------------|----------------|--------|
| F-G1-001 project_diagnostics totalDiagnostics drift | project_diagnostics | [#746](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/746) OPEN | **skipped: duplicate of #746** |
| F-G2-001 find_type_mutations CollectionWrite false positive | find_type_mutations | [#741](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/741) OPEN (near-match, different fix shape) | **skipped: near-match of #741** (conservative); operator may file separately if distinct fix is needed |
| F-G5-002 test_run OR-pipe filter silent zero | test_run | [#752](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/752) OPEN | **skipped: duplicate of #752** |

**Filed (6):**
| Finding | Severity | Area | New Issue |
|---------|----------|------|-----------|
| symbol_relationships builtin-type unbounded enumeration | P1 | tools | [#757](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/757) |
| find_property_writes(metadataName) position-shaped hint | P2 | tools | [#758](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/758) |
| validate_workspace InternalValidationTimeoutException at 25s | P2 | tools | [#759](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/759) |
| find_reflection_usages no pagination — 153 KB MCP cap overflow | P2 | tools | [#760](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/760) |
| workspace_status(verbose=true) 5s timeout race on Ready workspace | P2 | tools | [#761](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/761) |
| source_file/source_file_lines resource requires URL-encoded absolute path | P2 | resources | [#762](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/762) |

**Refusal contract check:** zero P0 / `area:security` findings detected → no auto-file refusals triggered.

**P3 findings (20+) NOT auto-filed per operator scope.** Full P3 catalog remains in Section 13. Operator can selectively file later via `gh issue create` or run `/mcp-server-surface-test` again with `--no-auto-file` to get stdout-print bodies for all of them.
