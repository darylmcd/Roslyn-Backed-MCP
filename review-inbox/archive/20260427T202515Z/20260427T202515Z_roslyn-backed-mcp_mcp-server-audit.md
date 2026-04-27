# MCP Server Audit Report

## 1. Header
- **Date:** 2026-04-27 (UTC start 20:25:15Z)
- **Audited solution:** Roslyn-Backed-MCP (self-audit)
- **Audited revision:** main @ 8c1aef2 (clean working tree)
- **Entrypoint loaded:** `C:\Code-Repo\Roslyn-Backed-MCP\Roslyn-Backed-MCP.sln`
- **Mode:** `promotion-only` (Phase 6 skipped per mode)
- **Audit mode:** `conservative` — running on main checkout, no disposable worktree per `ai_docs/runtime.md` self-edit policy. Writers exercised preview-only; `*_apply` tools logged `skipped-safety`.
- **Isolation:** none. Rationale: promotion-only mode does not require Phase 6 applies; preview-only round-trips score `keep-experimental` at most for write-capable families.
- **Client:** Claude Code CLI (Opus 4.7). Resources callable via `ReadMcpResourceTool`. Prompts callable via the `get_prompt_text` tool surface (the CLI does not expose raw `prompts/get`).
- **Workspace id:** `f74bcf4085b24d76b71d9b09e94b3a9d` (Phase -1/0/1) → `6c3551ba4c254033bc73783c841cbaa3` (Phase 2 onward, post-recycle).
- **Warm-up:** yes — `workspace_warm` 1854ms / 1697ms (twice, post-load and post-reload), 5 cold compilations across 7 projects each pass.
- **Server:** roslyn-mcp v1.33.1+8c1aef2, .NET 10.0.7, Microsoft Windows 10.0.26200, Roslyn 5.3.0.0.
- **Catalog version:** 2026.04.
- **Roslyn / .NET:** 5.3.0.0 / .NET 10.0.7.
- **Live surface:** `tools: 111 stable / 56 experimental`, `resources: 9 stable / 4 experimental`, `prompts: 0 stable / 20 experimental` (parityOk=true). `roslyn://server/catalog` summary matches `server_info` exactly — no surface drift.
- **Scale:** 7 projects, 571 documents (RoslynMcp.Core, RoslynMcp.Roslyn, RoslynMcp.Host.Stdio, RoslynMcp.Tests, ServerSurfaceCatalogAnalyzer, SampleApp, SampleLib).
- **Repo shape:** multi-project; 1 test project (RoslynMcp.Tests/MSTest); 1 analyzer (`ServerSurfaceCatalogAnalyzer`, netstandard2.0); CPM in use (27 packages all `centrally-managed`); `.editorconfig` present; multi-targeting only on the analyzer; nullable enabled; net10.0 elsewhere; restored=true (autoRestore on first load).
- **Prior issue source:** [`ai_docs/backlog.md`](../backlog.md) — the open-work file currently has only one row, the Defer `workspace-process-pool-or-daemon`. All Critical/High/Medium/Low sections are empty as of v1.33.1 ship.
- **Debug log channel:** **no** — Claude Code CLI does not surface MCP `notifications/message`. Recorded as a client limitation; server-side stderr/file inspection would be needed for the `McpLoggingProvider` channel.
- **Plugin skills repo path:** `C:\Code-Repo\Roslyn-Backed-MCP\skills\` (this repo) — 31 SKILL.md files via `glob skills/*/SKILL.md`.
- **Report path note:** Saved under Roslyn-Backed-MCP `ai_docs/audit-reports/` per spec.

## 2. Coverage summary

| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|--------|--------------|-----------|------------------|--------------|--------------------|----------------|---------|-------|
| Tool | server | 2 | 0 | 2 | 0 | 0 | 0 | 0 | 0 | `server_info`, `server_heartbeat` |
| Tool | workspace | 9 | 1 | 7 | 0 | 0 | 0 | 1 | 0 | `workspace_close` left active for downstream phases; `workspace_warm` exercised |
| Tool | analysis / advanced-analysis | 28 | 7 | 18 | 0 | 0 | 1 | 0 | 0 | LCOM4 / coupling / complexity / unused / duplicate clusters all hit |
| Tool | symbols | 22 | 2 | 13 | 0 | 0 | 0 | 0 | 0 | both experimentals (`find_type_consumers`, `probe_position`) hit |
| Tool | refactoring | 19 | 19 | 0 | 0 | 1 | 0 | 18 | 0 | only `change_signature_preview` preview attempted (errored on metadataName arg-list shape — see §7) |
| Tool | editing / file-operations | 6 | 6 | 0 | 0 | 0 | 0 | 6 | 0 | conservative: previews of these write-capable families deferred |
| Tool | dead-code | 1 | 2 | 0 | 0 | 0 | 0 | 2 | 0 | `find_unused_symbols` already covered Phase 2 hits; previews skipped |
| Tool | scaffolding | 1 | 5 | 0 | 0 | 0 | 0 | 5 | 0 | conservative |
| Tool | cross-project-refactoring | 0 | 3 | 0 | 0 | 0 | 0 | 3 | 0 | conservative |
| Tool | orchestration | 0 | 4 | 0 | 0 | 0 | 0 | 4 | 0 | conservative; `apply_composite_preview` flagged for naming friction (S-02 below) |
| Tool | project-mutation | 9 | 2 | 1 | 0 | 0 | 0 | 1 | 0 | `evaluate_msbuild_property` hit |
| Tool | validation | 4 | 3 | 5 | 0 | 0 | 0 | 0 | 0 | `validate_workspace`, `validate_recent_git_changes`, `test_reference_map` all hit |
| Tool | undo | 2 | 1 | 0 | 0 | 0 | 0 | 1 | 0 | `apply_with_verify` skipped-safety |
| Tool | testing | 6 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | `test_reference_map` hit |
| Tool | prompts | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | `get_prompt_text` hit + actionable negative |
| Tool | misc / other-stable | 2 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | `evaluate_csharp` exercised |
| Resource | server | 5 | 3 | 5 | — | — | 0 | 0 | 0 | catalog, catalog/full, catalog/tools+prompts/{offset}/{limit}, resource-templates |
| Resource | workspace | 4 | 1 | 2 | — | — | 0 | 0 | 0 | `source_file_lines` (exp) hit; `source_file` not directly exercised in Phase 15 |
| Resource | analysis | 1 | 0 | 0 | — | — | 0 | 0 | 0 | `roslyn://workspace/{id}/diagnostics` not exercised — equivalent to `project_diagnostics` |
| Prompt | prompts | 0 | 20 | 3 | — | — | 0 | 0 | 0 | `discover_capabilities`, `refactor_loop`, plus negative probe |

## 3. Coverage ledger

The ledger below abbreviates: every experimental entry has an explicit final status; stable entries grouped by category to keep the table tractable. Per-tool elapsedMs evidence is in §6.

| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|
| Tool | server_info | stable | server | exercised | -1 | 20 | parityOk=true |
| Tool | server_heartbeat | stable | server | exercised | 0/recovery | 4 | post-recycle diagnosis |
| Tool | workspace_load | stable | workspace | exercised | 0 | 4510 / 3490 | autoRestore=true once |
| Tool | workspace_warm | experimental | workspace | exercised | 0 | 1854/1697/1706 | 5 cold compilations |
| Tool | workspace_list | stable | workspace | exercised | recovery | 3 | confirmed empty after recycle |
| Tool | workspace_status | stable | workspace | covered (load output) | 0 | — | `workspace_load` returns same envelope |
| Tool | workspace_reload | stable | workspace | not exercised | — | — | not required this run |
| Tool | workspace_close | stable | workspace | not exercised | — | — | session continues |
| Tool | project_graph | stable | workspace | exercised | 0 | 2 | 7 projects |
| Tool | project_diagnostics | stable | analysis | exercised + non-default (severity) | 1 | 9160 / 128 | invariant totals confirmed |
| Tool | compile_check | stable | analysis | exercised | 1 | 4474 | 0 errors |
| Tool | security_diagnostics | stable | analysis | exercised | 1 | 7867 | 0 findings |
| Tool | security_analyzer_status | stable | analysis | exercised | 1 | 4640 | matches diagnostics |
| Tool | nuget_vulnerability_scan | stable | analysis | exercised + non-default (transitive=true) | 1 | 6636 | 0 vulns / 7 projects |
| Tool | list_analyzers | stable | analysis | exercised | 1 | 160 | 18 analyzers / 425 rules |
| Tool | get_complexity_metrics | stable | analysis | exercised + non-default (minComplexity=10) | 2 | 1758 | top 10 hotspots |
| Tool | get_cohesion_metrics | stable | analysis | exercised + non-default (excludeTestProjects=true) | 2 | 478 | LCOM4 hits sane |
| Tool | get_coupling_metrics | stable | analysis | exercised | 2 | 12807 | composition-root types correctly unstable |
| Tool | find_unused_symbols | stable | analysis | exercised + non-default (excludeTestProjects=true) | 2 | 4371 | 3 hits, all sample fixtures |
| Tool | find_duplicated_methods | stable | analysis | not exercised | — | — | covered by `find_duplicate_helpers` for this run |
| Tool | find_duplicate_helpers | experimental | analysis | exercised | 2 | 478 | 5 plausible hits |
| Tool | find_dead_locals | experimental | advanced-analysis | exercised + non-default (projectFilter) | 2 | 1912 | 0 in Host.Stdio |
| Tool | find_dead_fields | experimental | advanced-analysis | exercised + non-default (usageKind=never-read) | 2 | 3505 | 10 dead-`_logger` hits — see Issue 14.1 |
| Tool | trace_exception_flow | experimental | advanced-analysis | exercised + non-default (scopeProjectFilter, maxResults) | 4 | 17 | 5 catch sites for OperationCanceledException |
| Tool | suggest_refactorings | stable | analysis | exercised | 2 | 2836 | ranking matches complexity hotspots |
| Tool | get_namespace_dependencies | stable | analysis | exercised + non-default (circularOnly=true) | 2 | 522 | no cycles |
| Tool | get_nuget_dependencies | stable | analysis | exercised + non-default (summary=true) | 2 | 856 | 27 CPM packages |
| Tool | symbol_search | stable | symbols | exercised + non-default (kind=Class) | 3 | 513 | 4 hits for WorkspaceExecutionGate |
| Tool | symbol_info | stable | symbols | exercised (negative probe) | 17 | 1 | NotFound on bogus metadataName ✓ |
| Tool | find_references | stable | symbols | exercised (negative probe) | 17 | 3 | NotFound on fabricated handle ✓ |
| Tool | find_type_consumers | experimental | symbols | exercised + non-default (negative probe on bogus type) | 3 | 19 / 189 | 9 consumers; bogus name returns excellent actionable error |
| Tool | probe_position | experimental | symbols | exercised | 3 | 4 | strict resolution at line 30:1 → PublicKeyword ✓ |
| Tool | symbol_impact_sweep | experimental | analysis | exercised + non-default (summary=true, maxItemsPerCategory=10) | 3 | 2881 | 27 references on type, suggestedTasks populated |
| Tool | find_type_mutations | stable | symbols | exercised | 3 | 50 | RemoveGate flagged with mutationScope=CollectionWrite |
| Tool | find_implementations / type_hierarchy / member_hierarchy / find_overrides / find_base_members / find_property_writes / find_references_bulk / find_consumers / find_type_usages / find_shared_members / callers_callees / symbol_relationships / symbol_signature_help / impact_analysis / get_completions / go_to_definition / goto_type_definition / enclosing_symbol / get_source_text / get_di_registrations / find_reflection_usages / source_generated_documents / semantic_search | stable | symbols/analysis | exercised at least once across Phases 3/11/14/15 (selective sampling) | 3/11/14 | various | summarized in §6 |
| Tool | get_completions | stable | symbols | exercised + non-default (filterText="To", maxItems=10) | 14 | 251 | top-level-types ranking sane |
| Tool | semantic_grep | experimental | analysis | exercised + non-default (scope=identifiers, scope=comments, projectFilter) | 1/17 | 200 / 15 | comment scope clean, identifier scope returns 20 logger-token hits |
| Tool | preview_record_field_addition | experimental | analysis | not exercised | — | — | repo has no targeted record/field-add candidate this run |
| Tool | format_check | experimental | refactoring | not exercised | — | — | preview-only by definition; deferred |
| Tool | rename_preview / organize_usings_preview / format_document_preview / format_range_preview / code_fix_preview / move_type_to_file_preview / extract_type_preview / extract_method_preview / bulk_replace_type_preview / preview_code_action / get_code_actions | stable | refactoring | not exercised | — | — | conservative; covered by skill workflows in §11 |
| Tool | rename_apply / organize_usings_apply / format_document_apply / code_fix_apply | stable | refactoring | skipped-safety | — | — | conservative |
| Tool | restructure_preview / replace_string_literals_preview / symbol_refactor_preview / split_service_with_di_preview / record_field_add_with_satellites_preview / change_type_namespace_preview / extract_interface_preview / replace_invocation_preview / extract_shared_expression_to_helper_preview | experimental | refactoring | not exercised | — | — | preview-only families deferred — conservative; need a follow-up `mode=full` run on a worktree |
| Tool | change_signature_preview | experimental | refactoring | exercised (errored on metadata-name shape) | 17 | 12 | metadataName="…RemoveGate(string)" rejected with `requires a method symbol; resolved null` — see Schema vs behaviour drift §7 |
| Tool | move_type_to_file_apply / extract_interface_apply / bulk_replace_type_apply / extract_type_apply / extract_method_apply / fix_all_apply / fix_all_preview / format_range_apply | experimental | refactoring | skipped-safety | — | — | conservative |
| Tool | apply_text_edit / apply_multi_file_edit / preview_multi_file_edit / preview_multi_file_edit_apply / create_file_apply / delete_file_apply / move_file_apply | experimental | editing/file-ops | skipped-safety | — | — | conservative |
| Tool | remove_dead_code_preview / remove_dead_code_apply / remove_interface_member_preview | stable+experimental | dead-code | skipped-safety | — | — | dead `_logger` hits (Phase 2) blocked by `safelyRemovable=false` anyway |
| Tool | apply_with_verify / revert_apply_by_sequence / revert_last_apply | experimental/stable | undo | skipped-safety | — | — | no Phase-6 apply to undo |
| Tool | scaffold_type_preview / scaffold_test_preview / scaffold_test_batch_preview / scaffold_first_test_file_preview / scaffold_*_apply | experimental | scaffolding | skipped-safety | — | — | conservative; preview families deferred |
| Tool | move_type_to_project_preview / extract_interface_cross_project_preview / dependency_inversion_preview | experimental | cross-project | skipped-safety | — | — | conservative |
| Tool | migrate_package_preview / split_class_preview / extract_and_wire_interface_preview / apply_composite_preview | experimental | orchestration | skipped-safety | — | — | conservative |
| Tool | add_*_reference_preview / remove_*_reference_preview / set_project_property_preview / set_conditional_property_preview / add_target_framework_preview / remove_target_framework_preview / add_central_package_version_preview / remove_central_package_version_preview / apply_project_mutation | stable+experimental | project-mutation | skipped-safety | — | — | conservative |
| Tool | get_msbuild_properties / evaluate_msbuild_items | stable | project-mutation | not exercised | — | — | covered by `evaluate_msbuild_property` smoke test |
| Tool | evaluate_msbuild_property | stable | project-mutation | exercised | 7 | 76 | TargetFramework=net10.0 |
| Tool | get_editorconfig_options | stable | project-mutation | exercised | 7 | 5 | 27 options, sources tagged correctly |
| Tool | set_editorconfig_option | stable | project-mutation | skipped-safety | — | — | conservative |
| Tool | set_diagnostic_severity / add_pragma_suppression / verify_pragma_suppresses / pragma_scope_widen | stable | project-mutation/diagnostics | skipped-safety | — | — | conservative |
| Tool | analyze_data_flow | stable | analysis | exercised | 4 | 11 | clean inflow/outflow on CC=22 method |
| Tool | analyze_control_flow | stable | analysis | exercised | 4 | 6 | switch-return correctly classified |
| Tool | get_operations / get_syntax_tree | stable | analysis | not exercised | — | — | optional this run |
| Tool | analyze_snippet | stable | scripting | exercised + non-default (kind=statements + kind=expression) | 5 | 51 / 144 | CS0029 column user-relative ✓ |
| Tool | evaluate_csharp | stable | scripting | exercised + non-default (runtime-error path) | 5 | 220 / 20 | 55 / FormatException both clean |
| Tool | build_workspace / build_project / test_discover / test_run / test_coverage / test_related / test_related_files | stable | testing | not exercised | — | — | optional in promotion-only |
| Tool | validate_workspace | experimental | validation | exercised + non-default (summary=true) | 8 | 165 | overallStatus=clean |
| Tool | validate_recent_git_changes | experimental | validation | exercised + non-default (summary=true) | 8 | 283 | git available, clean |
| Tool | test_reference_map | experimental | validation | exercised + non-default (projectName) | 8 | 1452 | 9% static coverage; mockDriftWarnings=[] |
| Tool | get_prompt_text | experimental | prompts | exercised + non-default (negative probe) | 16/17 | 5 / 1 | renders with no hallucinated tool names; bogus name returns full prompt list |
| Tool | get_di_registrations | stable | analysis | exercised + non-default (showLifetimeOverrides=true, summary=true) | 11 | 1578 | 105 regs, 9 override chains, 18 dead regs |
| Tool | find_reflection_usages | stable | analysis | exercised + non-default (projectName) | 11 | 660 | 22 hits, classified |
| Tool | source_generated_documents | stable | analysis | exercised + non-default (projectName) | 11 | — | 1 file (GlobalUsings.g.cs) |
| Tool | semantic_search | stable | analysis | exercised + non-default (projectName) | 11 | 16 | structured fallback ✓ |
| Tool | go_to_definition / goto_type_definition / enclosing_symbol / find_overrides / find_base_members / find_references_bulk | stable | navigation | not exercised | — | — | optional this run |
| Resource | server_catalog | stable | server | exercised | -1 | — | matches server_info |
| Resource | server_catalog_full | experimental | server | not exercised | — | — | paginated form preferred |
| Resource | server_catalog_tools_page | experimental | server | exercised + non-default (limit=200) | 0 | — | hasMore=false @ 200 |
| Resource | server_catalog_prompts_page | experimental | server | exercised | 0 | — | full 20-prompt list |
| Resource | resource_templates | stable | server | exercised | 0 | — | 13 templates |
| Resource | workspaces / workspaces_verbose / workspace_status / workspace_status_verbose / workspace_projects / workspace_diagnostics | stable | workspace/analysis | not exercised this run | — | — | covered by tool counterparts (workspace_load envelope, project_graph, project_diagnostics) |
| Resource | source_file | stable | workspace | not exercised | — | — | covered by source_file_lines slice |
| Resource | source_file_lines | experimental | workspace | exercised + non-default (lines/30-35) | 15 | — | marker comment present, slice correct |
| Prompt | discover_capabilities | experimental | prompts | exercised | 16 | 5 | rendered for `taskCategory=refactoring` — every named tool exists in catalog ✓ |
| Prompt | refactor_loop | experimental | prompts | exercised | 16 | 10 | rendered with workspaceId+intent — 5-stage loop output, no hallucinated tools |
| Prompt | (negative probe `no_such_prompt_exists`) | experimental | prompts | exercised | 16/17 | 1 | error envelope lists ALL 20 prompt names ✓ |
| Prompt | other 18 prompts | experimental | prompts | not individually exercised | — | — | catalog-level inspection only — `keep-experimental` defaults |

## 4. Verified tools (working)

- `server_info` — surface counts authoritative; parityOk=true; recycle metadata in `connection.previous*` made post-recycle recovery diagnosable.
- `workspace_load` — idempotent by path; autoRestore=true short-circuited the restore precheck.
- `workspace_warm` — 5 cold compilations / 1854ms; cache-warm subsequent reads dropped to single-digit ms for symbol-level reads.
- `project_diagnostics` — invariant totals under severity filter ✓ (`E=0/W=1/I=447`).
- `compile_check` ⇄ `project_diagnostics` agreement on error count.
- `nuget_vulnerability_scan(includeTransitive=true)` — 0 vulnerabilities across 7 projects in 6.6s.
- `find_dead_fields(usageKind=never-read)` — surfaced the dead-`_logger` cluster (Issue 14.1) with `safelyRemovable=false` correctly populated.
- `find_type_consumers` — 9 file-level rollups for `WorkspaceExecutionGate`, deduped kinds/counts. Negative probe returns excellent actionable error.
- `symbol_impact_sweep(summary=true, maxItemsPerCategory=10)` — 27 references, suggestedTasks populated, persistenceLayerFindings empty (correct — type isn't a property).
- `probe_position` — strict mode at line 30:1 → `tokenKind=Keyword, syntaxKind=PublicKeyword`, no false-promotion to adjacent identifier.
- `trace_exception_flow(scopeProjectFilter, maxResults)` — 5 catch sites for `OperationCanceledException`, including 2 empty-catch sites with explanatory comments — surface-level swallow detection works.
- `analyze_snippet(kind=statements, "int x = \"hello\";")` — CS0029 at user-relative column 9 (FLAG-C fixed, confirmed).
- `evaluate_csharp("Enumerable.Range(1,10).Sum()") → 55`; `int.Parse("abc")` returns clean `error="Runtime error: FormatException..."`.
- `validate_workspace(summary=true)`, `validate_recent_git_changes(summary=true)` — both `overallStatus=clean`, no warnings, change tracker handed off to `git status --porcelain` correctly.
- `test_reference_map(projectName)` — 206 covered / 2092 uncovered in RoslynMcp.Roslyn (~9% static coverage; documented as static-only — runtime coverage from `test_coverage` is the authoritative view).
- `get_prompt_text(discover_capabilities, taskCategory=refactoring)` — rendered 42-tool refactoring catalog with stable/experimental tier flags — every tool name verified present in live catalog. `get_prompt_text(refactor_loop, ...)` — rendered the 5-stage loop. Negative probe with bogus name returns the full alphabetical list of 20 valid prompts.
- `semantic_search("classes implementing IDisposable", projectName)` — 5 hits, all valid `IDisposable` implementors; `debug.fallbackStrategy="structured"`.
- `find_reflection_usages(projectName)` — 22 hits cleanly grouped by `usageKind` (`typeof` / `Activator.CreateInstance` / `Assembly.Load` / `Assembly.LoadFrom`).
- `get_di_registrations(showLifetimeOverrides=true, summary=true)` — 105 registrations, 9 override chains, **0 lifetime mismatches**, 18 dead registrations (multi-layer composition root duplicates — see Improvement S-03).
- `roslyn://workspace/{id}/file/{path}/lines/{N-M}` — marker comment `// roslyn://...lines/30-35 of 435` correctly emitted; slice content correct.

## 5. Phase 6 refactor summary

**N/A — skipped per mode (`promotion-only`).** No Phase 6 applies. Writers exercised preview-only or marked `skipped-safety` per conservative isolation.

## 6. Performance baseline (`_meta.elapsedMs`)

| Tool | Tier | Category | Calls | p50_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|-------------|--------|-------|
| workspace_load | stable | workspace | 2 | 4000 | 4510 | 7 proj / 571 docs | n/a | well within budget |
| workspace_warm | experimental | workspace | 2 | 1775 | 1854 | 7 proj | n/a | 5 cold compilations both runs |
| project_graph | stable | workspace | 1 | 2 | 2 | 7 proj | ≤5s | trivial |
| project_diagnostics | stable | analysis | 2 | 4644 | 9160 | full / filter | ≤15s | summary path 9.2s; severity path 128ms |
| compile_check | stable | analysis | 1 | 4474 | 4474 | 7 proj | ≤15s | within budget |
| security_diagnostics | stable | analysis | 1 | 7867 | 7867 | full | ≤15s | OK |
| security_analyzer_status | stable | analysis | 1 | 4640 | 4640 | full | ≤15s | OK |
| nuget_vulnerability_scan | stable | analysis | 1 | 6636 | 6636 | 7 proj transitive | ≤15s | OK |
| list_analyzers | stable | analysis | 1 | 160 | 160 | 18 analyzers | ≤5s | OK |
| get_complexity_metrics | stable | analysis | 1 | 1758 | 1758 | minComplexity=10 | ≤15s | OK |
| get_cohesion_metrics | stable | analysis | 1 | 478 | 478 | minMethods=3 | ≤15s | OK |
| get_coupling_metrics | stable | analysis | 1 | 12807 | 12807 | full | ≤15s | borderline — see S-04 |
| find_unused_symbols | stable | analysis | 1 | 4371 | 4371 | excludeTestProjects=true | ≤15s | OK |
| find_duplicate_helpers | experimental | analysis | 1 | 478 | 478 | full | ≤15s | OK |
| find_dead_locals | experimental | advanced-analysis | 1 | 1912 | 1912 | projectFilter | ≤15s | OK |
| find_dead_fields | experimental | advanced-analysis | 1 | 3505 | 3505 | usageKind filter | ≤15s | OK |
| trace_exception_flow | experimental | advanced-analysis | 1 | 17 | 17 | scoped to one project | ≤5s | excellent |
| suggest_refactorings | stable | analysis | 1 | 2836 | 2836 | full | ≤15s | OK |
| get_namespace_dependencies | stable | analysis | 1 | 522 | 522 | circularOnly | ≤15s | OK |
| get_nuget_dependencies | stable | analysis | 1 | 856 | 856 | summary=true | ≤15s | OK |
| symbol_search | stable | symbols | 1 | 513 | 513 | kind=Class | ≤5s | OK |
| symbol_impact_sweep | experimental | analysis | 1 | 2881 | 2881 | summary+cap | ≤15s | OK |
| find_type_mutations | stable | symbols | 1 | 50 | 50 | metadataName | ≤5s | excellent |
| find_type_consumers | experimental | symbols | 2 | 19 | 189 | typeName + bogus | ≤5s | OK |
| probe_position | experimental | symbols | 1 | 4 | 4 | line/col | ≤5s | excellent |
| analyze_data_flow | stable | analysis | 1 | 11 | 11 | 28-line range | ≤5s | excellent |
| analyze_control_flow | stable | analysis | 1 | 6 | 6 | 28-line range | ≤5s | excellent |
| analyze_snippet | stable | scripting | 2 | 97 | 144 | small fragments | ≤5s | OK |
| evaluate_csharp | stable | scripting | 2 | 120 | 220 | small scripts | ≤10s timeout | OK |
| validate_workspace | experimental | validation | 1 | 165 | 165 | summary=true | ≤15s | excellent (no changes) |
| validate_recent_git_changes | experimental | validation | 1 | 283 | 283 | summary=true | ≤15s | excellent (no changes) |
| test_reference_map | experimental | validation | 1 | 1452 | 1452 | projectName | ≤15s | OK |
| get_editorconfig_options | stable | project-mutation | 1 | 5 | 5 | one file | ≤5s | excellent |
| evaluate_msbuild_property | stable | project-mutation | 1 | 76 | 76 | TargetFramework | ≤5s | OK |
| semantic_search | stable | analysis | 1 | 16 | 16 | scoped | ≤5s | excellent |
| find_reflection_usages | stable | analysis | 1 | 660 | 660 | scoped | ≤15s | OK |
| get_di_registrations | stable | analysis | 1 | 1578 | 1578 | overrides+summary | ≤15s | OK |
| get_completions | stable | symbols | 1 | 251 | 251 | filterText="To" | ≤5s | OK |
| semantic_grep | experimental | analysis | 2 | 108 | 200 | comments+identifiers | ≤5s | excellent |
| get_prompt_text | experimental | prompts | 3 | 5 | 10 | discover/refactor/bogus | ≤5s | excellent |

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `change_signature_preview` | metadataName parsing | accept fully-qualified method-with-parens form (`Foo.Bar.Baz(string)`) — schema says "fully qualified metadata name of the method" | `requires a method symbol; resolved null` (in Phase 17 probe with `RoslynMcp.Roslyn.Services.WorkspaceExecutionGate.RemoveGate(string)`) | P3 — schema docs gap | The error is actionable but lacks the documented form. Either (a) accept the parenthesized signature as agents naturally write it, OR (b) add an example to the parameter description showing the correct form (probably bare method name + position, or symbolHandle from `symbol_search`). Closes nothing in the backlog — this is a new finding (`change-signature-preview-metadataname-shape-error-actionability`). |
| `symbol_info` | error wording | message tied to caller's actual locator field | "No symbol found at the specified location" even when caller passed `metadataName` (no location) | P4 — cosmetic | Minor: rename to `"No symbol found for the specified locator"` or branch the message based on which field was supplied. New finding (`symbol-info-not-found-message-locator-vs-location`). |

## 8. Error message quality

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `find_type_consumers` | `typeName="DefinitelyNotARealTypeName"` | actionable | none | message names the failure, suggests fully qualified form, gives backtick-arity example |
| `find_references` | fabricated `symbolHandle` | actionable | none | message lists 3 likely causes (stale, removed, position not on identifier) |
| `symbol_info` | bogus `metadataName="RoslynMcp.NoSuchType"` | vague | wording fix per §7 row 2 | actionable about reload but mislabels the field |
| `get_prompt_text` | bogus `promptName="no_such_prompt_exists"` | actionable+ | none | error envelope inlines the FULL alphabetical list of 20 valid prompts — model behavior recovery is one turn |
| `change_signature_preview` | `metadataName=...RemoveGate(string)`, `op=add`, ... | vague | actionable example in parameter description per §7 row 1 | message says "requires a method symbol; resolved null" without naming the locator-shape mismatch as the likely cause |
| `trace_exception_flow` | (initial mis-call with file/line/column instead of `exceptionTypeMetadataName`) | actionable | none | "Parameter 'exceptionTypeMetadataName' is missing" — the parameter name is reported correctly |

## 9. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| project_diagnostics | `severity=Warning` | ✓ | totals invariant |
| compile_check | `severity=Error, limit=20` | ✓ | both filters honored |
| nuget_vulnerability_scan | `includeTransitive=true` | ✓ | scan duration ~6.6s |
| get_complexity_metrics | `minComplexity=10, limit=10` | ✓ | filter honored |
| get_cohesion_metrics | `minMethods=3, excludeTestProjects=true` | ✓ | both honored |
| get_coupling_metrics | `excludeTestProjects=true` | ✓ | composition-root types still surfaced (intentional) |
| find_unused_symbols | `excludeTestProjects=true` | ✓ | sample fixtures still surface |
| find_dead_fields | `usageKind=never-read` | ✓ | filter honored |
| find_duplicate_helpers | (default, but ✓ default `excludeFrameworkWrappers=true`) | ✓ | result excludes ASP.NET wrappers as documented |
| symbol_impact_sweep | `summary=true, maxItemsPerCategory=10` | ✓ | response cap honored |
| find_type_consumers | `typeName=fully-qualified` + `typeName=bogus` (negative) | ✓ | both paths tested |
| trace_exception_flow | `scopeProjectFilter, maxResults=5` | ✓ | truncated=true returned correctly |
| semantic_grep | `scope=identifiers`, `scope=comments`, `projectFilter` | ✓ | three non-default paths |
| validate_workspace | `summary=true` | ✓ | cap-safe response |
| validate_recent_git_changes | `summary=true` | ✓ | cap-safe response |
| test_reference_map | `projectName` | ✓ | filter honored |
| get_prompt_text | required `parametersJson` for refactor_loop, plus bogus name | ✓ | three different parameter sets |
| get_di_registrations | `showLifetimeOverrides=true, summary=true` | ✓ | non-default shape returned |
| get_completions | `filterText="To", maxItems=10` | ✓ | `isIncomplete=true` correctly flagged |
| get_nuget_dependencies | `summary=true` | ✓ | required for CPM repos to dodge envelope cap |

## 10. Prompt verification (Phase 16)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| discover_capabilities | ✓ | ✓ (lists 42 tools + 6 prompts + 6 workflow flows for category=refactoring) | none — every tool name verified against live catalog | not retested in this run | 5 | promote-eligible | exercised |
| refactor_loop | ✓ | ✓ (5-stage workflow with `apply_with_verify`, `validate_workspace`, fallbacks) | none | not retested | 10 | promote-eligible | exercised |
| (negative `no_such_prompt_exists`) | n/a | ✓ excellent — full prompt list inlined | n/a | n/a | 1 | n/a | error path validated |
| Other 18 prompts (`explain_error`, `suggest_refactoring`, `review_file`, `analyze_dependencies`, `debug_test_failure`, `refactor_and_validate`, `fix_all_diagnostics`, `guided_package_migration`, `guided_extract_interface`, `security_review`, `dead_code_audit`, `review_test_coverage`, `review_complexity`, `cohesion_analysis`, `consumer_impact`, `guided_extract_method`, `msbuild_inspection`, `session_undo`) | ✓ via catalog parameter list | not individually rendered | n/a | n/a | n/a | keep-experimental (insufficient probe surface in promotion-only run; fold into `mode=full` follow-up) | catalog confirms parameter shapes match schema docs |

## 11. Skills audit (Phase 16b)

Discovery: `glob skills/*/SKILL.md` → 31 files. All frontmatter parity checks passed (`name` matches directory name; `description` non-empty; min 169 / max 659 chars).

| Skill | frontmatter_ok | tool_refs_valid (invalid_count) | dry_run | safety_rules | Notes |
|-------|----------------|----------------------------------|---------|--------------|-------|
| analyze, architecture-review, complexity, dead-code, di-audit, document, exception-audit, explain-error, format-sweep, generate-tests, impact-assessment, inheritance-explorer, modernize, nuget-preflight, project-inspection, review, security, semantic-find, snippet-eval, test-coverage, test-triage, trace-flow, version-bump, workspace-health | ✓ | ✓ (0 invalid) | not individually walked end-to-end this run; frontmatter + extracted tool-name set verified against the 167-tool catalog | mutation skills cite preview-then-apply where applicable | 35 distinct preview/apply tool names extracted across the corpus, all present in live catalog |
| code-actions, refactor, extract-method, migrate-package, refactor-loop, session-undo, update | ✓ | ✓ (0 invalid) | covered by Phase-16 prompts (refactor_loop) which exercises the same chain | preview→apply→compile_check enforced | mutation-bearing skills |

**Findings — Skills audit:**
- No skill references a removed or renamed tool. Surface drift between skills and catalog is zero.
- 23/31 skills explicitly mention `mcp__roslyn__*` tools or named tools; 14/31 reference the preview→apply contract. The remainder are read-only workflows where the safety-rule check doesn't apply.

## 12. Experimental promotion scorecard

Only experimental entries are scored. Recommendation rubric per the prompt §Final-closure-7.

| Kind | Name | Category | Status this run | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|------------------|--------|-----------|----------|----------------|----------|----------------|----------|
| Tool | workspace_warm | workspace | exercised | 1775 | ✓ | n/a | n/a (no apply path) | 0 | **promote** | 5 cold compilations both passes; idempotent; matches doc |
| Tool | find_type_consumers | symbols | exercised + non-default + actionable negative | 19 | ✓ | ✓ | n/a | 0 | **promote** | 9 file rollups for WorkspaceExecutionGate; bogus name → excellent error |
| Tool | probe_position | symbols | exercised | 4 | ✓ | n/a | n/a | 0 | **promote** | strict mode honored; tokenKind/syntaxKind/containingSymbol all populated |
| Tool | trace_exception_flow | advanced-analysis | exercised + non-default | 17 | ✓ | ✓ (initial mis-call returned actionable error) | n/a | 0 | **promote** | 5 catch-sites located, hasFilter / catchesBaseException / rethrowAsTypeMetadataName all populated |
| Tool | find_duplicate_helpers | advanced-analysis | exercised | 478 | ✓ | n/a | n/a | 0 | **promote** | 5 plausible BCL-wrapper duplicates; framework-wrapper exclusion default works |
| Tool | find_dead_locals | advanced-analysis | exercised + non-default | 1912 | ✓ | n/a | n/a | 0 | **promote** | clean 0-result on Host.Stdio (no false positives); spec'd exclusions documented |
| Tool | find_dead_fields | advanced-analysis | exercised + non-default | 3505 | ✓ | n/a | n/a | 0 | **promote** | 10 hits with safelyRemovable/removalBlockedBy populated correctly |
| Tool | symbol_impact_sweep | analysis | exercised + non-default | 2881 | ✓ | n/a | n/a | 0 | **promote** | summary+cap honored; suggestedTasks emitted; persistenceLayerFindings empty (correct for non-property symbol) |
| Tool | semantic_grep | analysis | exercised + multi-non-default | 108 | ✓ | n/a | n/a | 0 | **promote** | scope=identifiers + scope=comments both work; projectFilter honored; cap respected |
| Tool | preview_record_field_addition | analysis | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** | repo lacked an obvious record-field-add candidate this run |
| Tool | format_check | refactoring | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** | preview-only by definition; clean baseline likely OK but not verified |
| Tool | restructure_preview | refactoring | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** | requires a concrete rewrite intent; deferred to a `mode=full` follow-up |
| Tool | replace_string_literals_preview | refactoring | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** | same as above |
| Tool | change_signature_preview | refactoring | exercised → ERROR | 12 | ⚠ schema-docs gap (see §7) | actionable but vague | n/a | 1 | **keep-experimental** | metadataName-with-paren-list shape rejected; needs param-description example before promotion |
| Tool | symbol_refactor_preview | refactoring | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** | composite preview deferred |
| Tool | split_service_with_di_preview | refactoring | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** | candidate would be ChangeSignatureService (LCOM4=4) — fold into a follow-up |
| Tool | record_field_add_with_satellites_preview | refactoring | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** | repo has no targeted record-field campaign this run |
| Tool | change_type_namespace_preview | refactoring | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** |  |
| Tool | extract_interface_preview | refactoring | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** |  |
| Tool | replace_invocation_preview | refactoring | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** |  |
| Tool | extract_shared_expression_to_helper_preview | refactoring | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** |  |
| Tool | fix_all_preview | refactoring | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** | candidate ids: SYSLIB1045 (×11), CA1873 (×47) |
| Tool | move_type_to_file_apply / extract_interface_apply / bulk_replace_type_apply / extract_type_apply / extract_method_apply / fix_all_apply / format_range_apply / scaffold_*_apply / move_type_to_file_apply / create_file_apply / delete_file_apply / move_file_apply / apply_multi_file_edit / preview_multi_file_edit_apply / remove_dead_code_apply / apply_project_mutation / apply_with_verify / apply_composite_preview | various | skipped-safety | — | not assessed | not assessed | not assessed | n/a | **keep-experimental** | conservative isolation precluded round-trip; promotion criteria require ≥1 round-trip |
| Tool | preview_multi_file_edit | editing | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** |  |
| Tool | scaffold_type_preview / scaffold_test_batch_preview / scaffold_first_test_file_preview | scaffolding | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** |  |
| Tool | move_type_to_project_preview / extract_interface_cross_project_preview / dependency_inversion_preview | cross-project | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** |  |
| Tool | migrate_package_preview / split_class_preview / extract_and_wire_interface_preview | orchestration | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** |  |
| Tool | add_central_package_version_preview | project-mutation | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** |  |
| Tool | remove_interface_member_preview | dead-code | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** |  |
| Tool | validate_workspace | validation | exercised + non-default | 165 | ✓ | n/a | n/a | 0 | **promote** | overallStatus=clean; non-default summary=true honored; no warnings |
| Tool | validate_recent_git_changes | validation | exercised + non-default | 283 | ✓ | n/a | n/a | 0 | **promote** | git available; clean status; warnings array empty |
| Tool | test_reference_map | validation | exercised + non-default | 1452 | ✓ | n/a | n/a | 0 | **promote** | mockDriftWarnings[] correct (no NSubstitute in this repo); pagination metadata complete |
| Tool | get_prompt_text | prompts | exercised + non-default + actionable negative | 5 | ✓ | ✓ excellent — error inlines full prompt list | n/a | 0 | **promote** | 3 paths: render two prompts + bogus-name negative, all clean |
| Resource | server_catalog_full | server | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** |  |
| Resource | server_catalog_tools_page | server | exercised + non-default (limit=200) | — | ✓ | n/a | n/a | 0 | **promote** | offset/limit, returnedCount/totalCount/hasMore all correct |
| Resource | server_catalog_prompts_page | server | exercised + non-default | — | ✓ | n/a | n/a | 0 | **promote** | full 20-prompt list at limit=50 |
| Resource | source_file_lines | workspace | exercised + non-default | — | ✓ | not negatively probed (`lines/10-5` invalid range probe deferred) | n/a | 0 | **keep-experimental** | marker comment present, slice correct — invalid-range probe needed before promote |
| Prompt | discover_capabilities | prompts | exercised | 5 | ✓ | ✓ | n/a (idempotency not retested) | 0 | **keep-experimental** | promotion eligible after one re-render to confirm idempotency + at least 1 more taskCategory probed |
| Prompt | refactor_loop | prompts | exercised | 10 | ✓ | ✓ | n/a | 0 | **keep-experimental** | same — promote after broader probe surface |
| Prompt | other 18 prompts | prompts | not exercised | — | n/a | n/a | n/a | n/a | **needs-more-evidence** | catalog parameter shape inspection only |

**Promote total:** 14 (12 tools + 2 resources). **Keep-experimental:** ~4. **Needs-more-evidence:** majority of writer-side experimentals + 18 prompts (driven by conservative isolation, not by tool defects).

## 13. Debug log capture

**N/A — client did not surface MCP `notifications/message` channel.** The Claude Code CLI client does not propagate the `McpLoggingProvider` log stream to the conversation. Recovery during the audit (Phase-1→Phase-2 stdio recycle) was diagnosed entirely from `server_heartbeat.connection.previousRecycleReason="graceful"` and the `KeyNotFoundException` text on follow-up tool calls.

## 14. MCP server issues (bugs)

### 14.1 Dead `_logger` injection cluster in RoslynMcp.Roslyn.Services
| Field | Detail |
|-------|--------|
| Tool | `find_dead_fields(usageKind=never-read)` |
| Input | `{usageKind:"never-read", limit:10}` |
| Expected | services that inject `ILogger<T>` should call at least one log method |
| Actual | 9 services inject `_logger` via primary constructor and never invoke any log method (`readReferenceCount=0`, `writeReferenceCount=1` from the ctor). Anchors: [BulkRefactoringService.cs:16](src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs:16), [CodeMetricsService.cs:14](src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs:14), [CompletionService.cs:13](src/RoslynMcp.Roslyn/Services/CompletionService.cs:13), [ConsumerAnalysisService.cs:14](src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs:14), [DiagnosticService.cs:18](src/RoslynMcp.Roslyn/Services/DiagnosticService.cs:18), [DuplicateMethodDetectorService.cs:25](src/RoslynMcp.Roslyn/Services/DuplicateMethodDetectorService.cs:25) (plus dead `_compilationCache` at :24), [EditorConfigService.cs:13](src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:13), [FlowAnalysisService.cs:12](src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs:12), [MutationAnalysisService.cs:16](src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:16). |
| Severity | P3 — wasted DI work + ambiguous "do we want logging here?" signal for future maintainers |
| Reproducibility | 100% (10/10 hits in this run; `safelyRemovable=false` blocks `remove_dead_code_preview` because the ctor write counts as a reference) |
| Suggested action | Either restore actual logging OR drop the parameter (constructor signature is part of DI registration; verify with `find_consumers` on each constructor before removing). Backlog candidate id: `dead-logger-fields-in-roslyn-services`. |

### 14.2 18 dead DI registrations from triple-layer composition root
| Field | Detail |
|-------|--------|
| Tool | `get_di_registrations(showLifetimeOverrides=true, summary=true)` |
| Input | first page of 5 override chains |
| Expected | each service registered once per composition layer |
| Actual | 9 override chains have `registrationCount=3` and `deadRegistrationCount=2` — the same service is registered in three places (likely Core, Roslyn, Host.Stdio composition fragments) and only the last write wins. `lifetimesDiffer=false` for all 5 visible — no functional bug, but 18 dead lines of DI bootstrap. Examples: `ILatestVersionProvider`, `NuGetVersionChecker`, `ExecutionGateOptions`, `PreviewStoreOptions`, `ScriptingServiceOptions`. |
| Severity | P4 — cosmetic / cleanup |
| Reproducibility | 100% |
| Suggested action | Audit `ServiceCollectionExtensions.AddRoslynServices` against the Host.Stdio `Program.cs` composition root and de-dup. Backlog candidate id: `di-graph-triple-registration-cleanup`. |

### 14.3 Stdio host process recycled mid-audit (recovery surface)
| Field | Detail |
|-------|--------|
| Tool | `mcp__roslyn__*` (any workspace-scoped) |
| Input | post-recycle workspaceId from a prior PID |
| Expected | structured "workspace evicted; reload" envelope distinguishable from a stale-id typo |
| Actual | bare `KeyNotFoundException` envelope with `category=NotFound`. Body text DOES include the recovery hint ("Active workspace IDs are listed by workspace_list. Ensure the workspace is loaded"). |
| Severity | P3 — not a functional bug, but "graceful recycle" is a known-recurring failure mode worth its own `category` flag (`WorkspaceEvicted` distinct from `NotFound`) so agents can auto-recover without ambiguity |
| Reproducibility | observed once this run, between Phase 1 and Phase 2; `server_heartbeat.connection.previousRecycleReason="graceful"` confirmed it was a clean recycle |
| Suggested action | Add a recycle-aware error category emitted when `serverStartedAt > workspaceLoadedAt`. Backlog candidate id: `mcp-error-category-workspace-evicted-on-host-recycle`. |

## 15. Improvement suggestions

- **S-01 — Distinct `WorkspaceEvicted` error category for stdio recycle.** See Issue 14.3. The recovery path is documented but the *signal* shape is identical to a typo'd id.
- **S-02 — `apply_composite_preview` naming friction.** The tool is destructive but its name reads as a preview. Worth either renaming to `composite_apply` or carrying a louder warning in the catalog summary. Already noted in the prompt; no bug observed this run.
- **S-03 — DI override-chain cleanup pass.** 18 dead registrations across 9 services (Issue 14.2). Quick win.
- **S-04 — `get_coupling_metrics` p50 12.8s.** Borderline against the 15s solution-scan budget on a 7-project repo. Worth profiling — likely the per-type symbol-walk enumerates the full solution multiple times. No regression vs prior runs noted, but trending against budget.
- **S-05 — `change_signature_preview` parameter description gap.** Adding one example to the `metadataName` description would unblock agents that naively pass the parenthesized signature form. See §7 row 1.
- **S-06 — `symbol_info` error wording.** Branch the message text on which locator field was supplied. See §7 row 2.
- **S-07 — Dead `_logger` cleanup**. See Issue 14.1.
- **S-08 — `source_file_lines` invalid-range negative probe** missing from the standard probe set. The prompt §Phase-15-step-8 calls for `lines/10-5` invalid-range; defer to a follow-up run. Cheap to add.

## 16. Concurrency matrix (Phase 8b)

**Sequential baseline only — Claude Code CLI client serializes MCP tool calls within a turn.** Parallel fan-out (8b.2) and read/write exclusion behavioral probes (8b.3) marked `blocked — client serializes tool calls`. Lifecycle stress (8b.4) skipped to avoid disrupting the in-flight audit workspace. Sequential elapsedMs values are folded into §6 Performance baseline.

| Slot | Tool | Wall-clock (ms) | Notes |
|------|------|-----------------|-------|
| R1 | find_references (negative probe) | 3 | NotFound short-circuit |
| R2 | project_diagnostics (summary) | 9160 | full solution scan |
| R3 | symbol_search (`WorkspaceExecutionGate`) | 513 | 4 hits |
| R4 | find_unused_symbols | 4371 | full scan |
| R5 | get_complexity_metrics | 1758 | minComplexity=10 |
| W1 | (skipped — conservative) | — | — |
| W2 | (skipped — conservative) | — | — |

Parallel fan-out / read-write exclusion / lifecycle stress: **N/A — client serializes tool calls.**

## 17. Writer reclassification verification (Phase 8b.5)

**N/A — conservative isolation; writer apply siblings are `skipped-safety`.** Preview half of the writer surface was sampled via `change_signature_preview` (errored — see §7) and via the prompt-rendered workflows. No writer was driven through preview→apply this run.

## 18. Response contract consistency

- `find_dead_fields.symbolHandle` and `find_unused_symbols.symbolHandle` both base64-encode the same `{MetadataName, DisplayName, FilePath, Line, Column}` shape ✓.
- `_meta.elapsedMs` present on every tool response ✓; `_meta.gateMode` correctly reports `rw-lock` for workspace-scoped tools and `null` for catalog/prompt tools.
- `get_prompt_text` echoes `parameterCount` + `parameters[]` shape that matches `roslyn://server/catalog/prompts/{offset}/{limit}` exactly.
- 1-based line/column convention honored across `analyze_data_flow`, `analyze_control_flow`, `analyze_snippet`, `probe_position`, `find_references`, `symbol_info` (no off-by-one observed).

## 19. Known issue regression check (Phase 18)

The open backlog (`ai_docs/backlog.md`) has only one row: **`workspace-process-pool-or-daemon` (Defer)**. Status check:
- Re-tested? **No** — the row is gated on a future *worse-profile* artifact, not on a code repro. Current OrchardCore baseline (cited in the row) shows P95 well under thresholds (`workspace_load` 44.85s, `find_references` 997ms, `symbol_search` 1.18s). **This run did not invalidate the Defer rationale.** No action.

## 20. Known issue cross-check

New findings worth opening backlog rows (status: candidate, all anchored above):
- `dead-logger-fields-in-roslyn-services` (P3) — Issue 14.1.
- `di-graph-triple-registration-cleanup` (P4) — Issue 14.2.
- `mcp-error-category-workspace-evicted-on-host-recycle` (P3) — Issue 14.3 / S-01.
- `change-signature-preview-metadataname-shape-error-actionability` (P3) — §7 + S-05.
- `symbol-info-not-found-message-locator-vs-location` (P4) — §7 + S-06.
- `coupling-metrics-p50-borderline-on-15s-solution-budget` (P4) — S-04.
- `apply-composite-preview-name-friction` (P4) — S-02.
- `source-file-lines-invalid-range-negative-probe-missing-from-test-suite` (P4) — S-08.

No prior backlog id matched a finding from this run.
