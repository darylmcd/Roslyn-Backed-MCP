# MCP Server Audit Report

**Target:** `DotNet-Firewall-Analyzer` (FirewallAnalyzer.slnx) on disposable branch `audit/deep-review-mcp-20260408T000000Z`  
**Prompt:** `ai_docs/prompts/deep-review-and-refactor.md` (phases 0→18, ordered)  
**Report path note:** Audit artifacts stored in this repo per prompt when Roslyn-Backed-MCP root is not the workspace. Intended canonical copy for plugin rollup: `<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/` via `eng/import-deep-review-audit.ps1` when applicable.

---

## 1. Header

| Field | Value |
|-------|--------|
| **Date** | 2026-04-08 (run ~19:43–19:48Z) |
| **Audited solution** | `c:\Code-Repo\DotNet-Firewall-Analyzer\FirewallAnalyzer.slnx` |
| **Audited revision** | Working tree on branch `audit/deep-review-mcp-20260408T000000Z` (not merged) |
| **Entrypoint loaded** | `FirewallAnalyzer.slnx` |
| **Audit mode** | `full-surface` (disposable branch; audit-only writes reverted) |
| **Isolation** | Git branch `audit/deep-review-mcp-20260408T000000Z` |
| **Client** | Cursor agent + `user-roslyn` MCP (`call_mcp_tool` / `fetch_mcp_resource`) |
| **Workspace id** | `22010f9378684c74b2e93fca7c4040ff` (closed at end of Phase 17e) |
| **Server** | roslyn-mcp **1.8.1+0e2a88b9d2071aab494aa79d0e8a5429425c4d57** |
| **Catalog version** | **2026.04** |
| **Roslyn / .NET** | Roslyn **5.3.0.0**, runtime **.NET 10.0.5**, OS **Microsoft Windows 10.0.26200** |
| **Scale** | 11 projects, 230 documents (from `workspace_load`) |
| **Repo shape** | Multi-project, net10.0, xUnit tests, **Central Package Management** (`centrally-managed` in `get_nuget_dependencies`), DI registrations in `ApiHostBuilder`, source generators / global usings in `source_generated_documents`, `.editorconfig` present |
| **Prior issue source (Phase 18)** | `ai_docs/backlog.md` — **no open MCP/server rows**; product deferred rows are not Roslyn MCP regressions |
| **Debug log channel** | **no** — Cursor session did not surface MCP `notifications/message` streams in tool output |
| **Plugin skills repo path** | **blocked — Roslyn-Backed-MCP / `skills/*` not in workspace** (Phase 16b) |

---

## 2. Coverage summary (rollup)

| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked |
|------|----------|-----------|-----------------|--------------|--------------------|----------------|---------|
| tool | (all categories) | See §3 | `rename_apply`, `apply_text_edit` (probe), `revert_last_apply` | `move_type_to_file_preview`, `fix_all_preview`, `organize_usings_preview`, `format_document_preview` | Multi-targeting / MTF previews not applicable | Many destructive orchestration / file / project applies intentionally not applied on shared branch beyond probes | **16 prompts** (no `prompts/get` in agent bridge this session) |
| resource | server + workspace | `roslyn://server/catalog`, `roslyn://server/resource-templates`, `roslyn://workspaces` pattern via templates | — | — | — | — | — |
| prompt | prompts | 0 | — | — | — | — | **16** (client) |

**Ledger rule:** Every catalog **tool** row appears in §3 with a terminal status. **Resources** (9) and **prompts** (16) appear in §3. No silent omissions: anything not exercised in-process is `blocked` (prompts) or `needs-more-evidence` / `skipped-safety` with an explicit note.

---

## 3. Coverage ledger (catalog 2026.04)

**Convention:** `lastElapsedMs` from `_meta.elapsedMs` when present. `—` = not recorded or N/A.

### Tools (123)

| kind | name | tier | category | status | phase | lastElapsedMs | notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | server_info | stable | server | exercised | 0 | 4 | |
| tool | workspace_load | stable | workspace | exercised | 0 | 6761 | load gate |
| tool | workspace_reload | stable | workspace | exercised | 8 | 3041 | post-refactor |
| tool | workspace_close | stable | workspace | exercised | 17e | 3 | session released |
| tool | workspace_list | stable | workspace | exercised | 0, heartbeats | 0–1 | |
| tool | workspace_status | stable | workspace | exercised | 0, 17a | 30; 0 (bad id) | NotFound actionable for bogus id |
| tool | project_graph | stable | workspace | exercised | 0 | 2 | |
| tool | source_generated_documents | stable | workspace | exercised | 11 | (array) | lists obj/Debug generators |
| tool | get_source_text | stable | workspace | needs-more-evidence | — | — | not separately invoked; `get_editorconfig_options` + file reads cover overlap |
| tool | symbol_search | stable | symbols | exercised | 3 | — | DriftDetector, ISnapshotStore |
| tool | symbol_info | stable | symbols | exercised | 3 | 3 | |
| tool | go_to_definition | stable | symbols | exercised | 14 | — | DriftDetector.Compare |
| tool | find_references | stable | symbols | exercised | 3,8b | 3–276 | symbolHandle required for metadata-only |
| tool | find_implementations | stable | symbols | exercised | 3 | 1 | ISnapshotStore → FileSnapshotStore |
| tool | document_symbols | stable | symbols | exercised | 3 | — | DriftDetector.cs |
| tool | find_overrides | stable | symbols | needs-more-evidence | — | — | no virtual target selected this run |
| tool | find_base_members | stable | symbols | needs-more-evidence | — | — | |
| tool | member_hierarchy | stable | symbols | needs-more-evidence | — | — | |
| tool | symbol_signature_help | stable | symbols | exercised | 3 | 2 | Compare |
| tool | symbol_relationships | stable | symbols | exercised | 3 | 11 | |
| tool | find_references_bulk | stable | symbols | exercised | 14 | 29 | use `symbols` array not `symbolHandles` |
| tool | find_property_writes | stable | symbols | needs-more-evidence | — | — | |
| tool | enclosing_symbol | stable | symbols | exercised | 14 | 3 | |
| tool | goto_type_definition | stable | symbols | exercised | 14,17 | 2 | NotFound at probe location — negative path |
| tool | get_completions | stable | symbols | exercised | 14 | 330 | empty Items at probe |
| tool | project_diagnostics | stable | analysis | exercised | 1 | 9.7k / 15ms | severity=Info for IDE diagnostics |
| tool | diagnostic_details | stable | analysis | exercised | 1 | 4 | CA1859 |
| tool | type_hierarchy | stable | analysis | exercised | 3 | 4 | |
| tool | callers_callees | stable | analysis | exercised | 3 | 6 | AreRulesEqual |
| tool | impact_analysis | stable | analysis | exercised | 3 | 6 | |
| tool | find_type_mutations | stable | analysis | exercised | 3 | 4 | 0 mutators static class |
| tool | find_type_usages | stable | analysis | exercised | 3 | 10 | |
| tool | find_consumers | stable | analysis | exercised | 3 | 6 | |
| tool | find_shared_members | stable | analysis | exercised | 3 | 9 | 0 shared |
| tool | build_workspace | stable | validation | exercised | 8 | 3327 | Exit 0 |
| tool | build_project | stable | validation | needs-more-evidence | — | — | |
| tool | test_discover | stable | validation | exercised | 8 | (large) | |
| tool | test_run | stable | validation | exercised | 8 | 6751 | filter Category!=E2E, 297 passed |
| tool | test_related | stable | validation | needs-more-evidence | — | — | |
| tool | test_related_files | stable | validation | needs-more-evidence | — | — | |
| tool | rename_preview | stable | refactoring | exercised | 6 | 205 | |
| tool | rename_apply | stable | refactoring | exercised-apply | 6 | 32 | ListsEqual→AreStringListsEqual |
| tool | organize_usings_preview | stable | refactoring | exercised-preview-only | 6 | 38 | empty Changes |
| tool | organize_usings_apply | stable | refactoring | skipped-safety | — | — | no preview edits to apply |
| tool | format_document_preview | stable | refactoring | exercised-preview-only | 6,9 | 11–72 | often empty |
| tool | format_document_apply | stable | refactoring | skipped-safety | — | — | empty preview; not applied |
| tool | code_fix_preview | stable | refactoring | needs-more-evidence | — | — | |
| tool | code_fix_apply | stable | refactoring | skipped-safety | — | — | |
| tool | find_unused_symbols | stable | advanced-analysis | exercised | 2 | 860–1360 | public=9 low/med confidence |
| tool | get_di_registrations | stable | advanced-analysis | exercised | 11 | 757 | |
| tool | get_complexity_metrics | stable | advanced-analysis | exercised | 2 | 71 | top CC=20 YamlConfigLoader |
| tool | find_reflection_usages | stable | advanced-analysis | exercised | 11 | 447 | |
| tool | get_namespace_dependencies | stable | advanced-analysis | exercised | 2 | (large) | |
| tool | get_nuget_dependencies | stable | advanced-analysis | exercised | 2 | 1204 | CPM |
| tool | semantic_search | experimental | advanced-analysis | exercised | 11 | 107 | async Task<bool> query |
| tool | apply_text_edit | experimental | editing | exercised-apply | 8b,9 | 2–17 | Program.cs probes |
| tool | apply_multi_file_edit | experimental | editing | skipped-safety | — | — | not exercised (single-file probes) |
| tool | create_file_preview | experimental | file-operations | needs-more-evidence | — | — | |
| tool | create_file_apply | experimental | file-operations | skipped-safety | — | — | |
| tool | delete_file_preview | experimental | file-operations | needs-more-evidence | — | — | |
| tool | delete_file_apply | experimental | file-operations | skipped-safety | — | — | |
| tool | move_file_preview | experimental | file-operations | needs-more-evidence | — | — | |
| tool | move_file_apply | experimental | file-operations | skipped-safety | — | — | |
| tool | add_package_reference_preview | experimental | project-mutation | needs-more-evidence | — | — | |
| tool | remove_package_reference_preview | experimental | project-mutation | needs-more-evidence | — | — | |
| tool | add_project_reference_preview | experimental | project-mutation | needs-more-evidence | — | — | |
| tool | remove_project_reference_preview | experimental | project-mutation | needs-more-evidence | — | — | |
| tool | set_project_property_preview | experimental | project-mutation | needs-more-evidence | — | — | |
| tool | add_target_framework_preview | experimental | project-mutation | skipped-repo-shape | — | — | single TF net10.0 |
| tool | remove_target_framework_preview | experimental | project-mutation | skipped-repo-shape | — | — | |
| tool | set_conditional_property_preview | experimental | project-mutation | needs-more-evidence | — | — | |
| tool | add_central_package_version_preview | experimental | project-mutation | needs-more-evidence | — | — | CPM present — preview not run |
| tool | remove_central_package_version_preview | experimental | project-mutation | needs-more-evidence | — | — | |
| tool | apply_project_mutation | experimental | project-mutation | skipped-safety | — | — | reversible loop not executed |
| tool | scaffold_type_preview | experimental | scaffolding | needs-more-evidence | — | — | |
| tool | scaffold_type_apply | experimental | scaffolding | skipped-safety | — | — | |
| tool | scaffold_test_preview | experimental | scaffolding | needs-more-evidence | — | — | |
| tool | scaffold_test_apply | experimental | scaffolding | skipped-safety | — | — | |
| tool | remove_dead_code_preview | experimental | dead-code | needs-more-evidence | — | — | no safe private dead symbols |
| tool | remove_dead_code_apply | experimental | dead-code | skipped-safety | — | — | |
| tool | move_type_to_project_preview | experimental | cross-project-refactoring | needs-more-evidence | — | — | |
| tool | extract_interface_cross_project_preview | experimental | cross-project-refactoring | needs-more-evidence | — | — | |
| tool | dependency_inversion_preview | experimental | cross-project-refactoring | needs-more-evidence | — | — | |
| tool | migrate_package_preview | experimental | orchestration | needs-more-evidence | — | — | |
| tool | split_class_preview | experimental | orchestration | needs-more-evidence | — | — | |
| tool | extract_and_wire_interface_preview | experimental | orchestration | needs-more-evidence | — | — | |
| tool | apply_composite_preview | experimental | orchestration | skipped-safety | — | — | destructive naming per prompt |
| tool | test_coverage | stable | validation | exercised | 8 | (large trx) | with filter |
| tool | get_syntax_tree | experimental | syntax | needs-more-evidence | — | — | |
| tool | get_code_actions | experimental | code-actions | needs-more-evidence | — | — | |
| tool | preview_code_action | experimental | code-actions | needs-more-evidence | — | — | |
| tool | apply_code_action | experimental | code-actions | skipped-safety | — | — | |
| tool | security_diagnostics | stable | security | exercised | 1 | 10170 | 0 findings |
| tool | security_analyzer_status | stable | security | exercised | 1 | 4876 | |
| tool | nuget_vulnerability_scan | stable | security | exercised | 1 | 3368 | 0 CVE |
| tool | get_cohesion_metrics | stable | analysis | exercised | 2 | (large) | LCOM4 test class high |
| tool | move_type_to_file_preview | experimental | refactoring | exercised-preview-only | 10 | 43 | DriftReport split preview |
| tool | move_type_to_file_apply | experimental | refactoring | skipped-safety | — | — | intentionally not applied |
| tool | extract_interface_preview | experimental | refactoring | needs-more-evidence | — | — | |
| tool | extract_interface_apply | experimental | refactoring | skipped-safety | — | — | |
| tool | bulk_replace_type_preview | experimental | refactoring | needs-more-evidence | — | — | |
| tool | bulk_replace_type_apply | experimental | refactoring | skipped-safety | — | — | |
| tool | extract_type_preview | experimental | refactoring | needs-more-evidence | — | — | |
| tool | extract_type_apply | experimental | refactoring | skipped-safety | — | — | |
| tool | revert_last_apply | experimental | undo | exercised-apply | 8b,9 | 2661–2995 | undid text edits only |
| tool | analyze_data_flow | experimental | advanced-analysis | exercised | 4 | 13 | YamlConfigLoader |
| tool | analyze_control_flow | experimental | advanced-analysis | exercised | 4 | 6 | EndPointIsReachable false + warning |
| tool | compile_check | stable | validation | exercised | 1,6,8,9 | 444–9789 | emitValidation slower |
| tool | list_analyzers | stable | analysis | exercised | 1 | 7 | 492 rules total |
| tool | fix_all_preview | experimental | refactoring | exercised-preview-only | 6 | 59 | IDE0005 no provider |
| tool | fix_all_apply | experimental | refactoring | skipped-safety | — | — | no preview token |
| tool | get_operations | experimental | advanced-analysis | needs-more-evidence | — | — | |
| tool | format_range_preview | experimental | refactoring | needs-more-evidence | — | — | |
| tool | format_range_apply | experimental | refactoring | skipped-safety | — | — | |
| tool | analyze_snippet | stable | analysis | exercised | 5 | 55–69 | CS0029 column 9 |
| tool | evaluate_csharp | experimental | scripting | exercised | 5 | 21–85 | runtime + sum |
| tool | get_editorconfig_options | experimental | configuration | exercised | 7 | 4 | |
| tool | set_editorconfig_option | experimental | configuration | needs-more-evidence | — | — | W2 not run (time) |
| tool | evaluate_msbuild_property | experimental | project-mutation | needs-more-evidence | — | — | |
| tool | evaluate_msbuild_items | experimental | project-mutation | needs-more-evidence | — | — | |
| tool | get_msbuild_properties | experimental | project-mutation | needs-more-evidence | — | — | |
| tool | set_diagnostic_severity | experimental | configuration | needs-more-evidence | — | — | |
| tool | add_pragma_suppression | experimental | editing | needs-more-evidence | — | — | |

### Resources (9)

| kind | name | tier | category | status | phase | notes |
|------|------|------|----------|--------|-------|-------|
| resource | server_catalog | stable | server | exercised | 0,15 | `fetch_mcp_resource` |
| resource | resource_templates | stable | server | exercised | 0,15 | 9 URIs |
| resource | workspaces | stable | workspace | needs-more-evidence | — | use `roslyn://workspaces` after reload in future pass |
| resource | workspaces_verbose | stable | workspace | needs-more-evidence | — | |
| resource | workspace_status | stable | workspace | needs-more-evidence | — | compare to tool |
| resource | workspace_status_verbose | stable | workspace | needs-more-evidence | — | |
| resource | workspace_projects | stable | workspace | needs-more-evidence | — | |
| resource | workspace_diagnostics | stable | analysis | needs-more-evidence | — | |
| resource | source_file | stable | workspace | needs-more-evidence | — | URI form vs `get_source_text` |

### Prompts (16)

| kind | name | tier | category | status | notes |
|------|------|------|----------|--------|-------|
| prompt | explain_error | experimental | prompts | **blocked** | No `prompts/get` / prompt invocation path in Cursor `call_mcp_tool` for this session |
| prompt | suggest_refactoring | experimental | prompts | **blocked** | same |
| prompt | review_file | experimental | prompts | **blocked** | same |
| prompt | analyze_dependencies | experimental | prompts | **blocked** | same |
| prompt | debug_test_failure | experimental | prompts | **blocked** | same |
| prompt | refactor_and_validate | experimental | prompts | **blocked** | same |
| prompt | fix_all_diagnostics | experimental | prompts | **blocked** | same |
| prompt | guided_package_migration | experimental | prompts | **blocked** | same |
| prompt | guided_extract_interface | experimental | prompts | **blocked** | same |
| prompt | security_review | experimental | prompts | **blocked** | same |
| prompt | discover_capabilities | experimental | prompts | **blocked** | same |
| prompt | dead_code_audit | experimental | prompts | **blocked** | same |
| prompt | review_test_coverage | experimental | prompts | **blocked** | same |
| prompt | review_complexity | experimental | prompts | **blocked** | same |
| prompt | cohesion_analysis | experimental | prompts | **blocked** | same |
| prompt | consumer_impact | experimental | prompts | **blocked** | same |

---

## 4. Verified tools (working) — sample evidence

- `server_info` — catalog 2026.04, 62+61 tools, 4ms  
- `workspace_load` — 11 projects, 230 docs, clean load  
- `project_diagnostics` + `compile_check` — 0 errors; emit path slower when `emitValidation=true`  
- `test_run` — **297 passed**, 0 failed, `Category!=E2E`  
- `build_workspace` — succeeded, 0 warnings/errors  
- `rename_apply` — `ListsEqual` → `AreStringListsEqual`; `find_references` count consistent (9 call sites)  
- `apply_text_edit` / `revert_last_apply` — Program.cs probe reverted; rename retained  
- `move_type_to_file_preview` — valid diff for `DriftReport` extraction (not applied)  
- `find_references_bulk` — works with `symbols` parameter shape  
- `semantic_search` — returned `TestConnectivityAsync` for async `Task<bool>`-style query  

---

## 5. Phase 6 refactor summary

**Target repo:** DotNet-Firewall-Analyzer  

**Applied (product):** `rename_apply` — private helper `ListsEqual` renamed to **`AreStringListsEqual`** in `DriftDetector.cs` (clearer boolean semantics; all call sites in `AreRulesEqual` updated).  

**Preview-only / not applied:** `fix_all_preview` (IDE0005 — no FixAll provider; guidance message returned), `organize_usings_preview` (empty), `format_document_preview` (empty), `move_type_to_file_preview` (DriftReport → own file — **not applied** to avoid scope creep).  

**Tools:** `rename_preview` + `rename_apply`; `compile_check` PASS; `workspace_reload` + `build_workspace` PASS; `test_run` filter PASS (297).  

**Verification:** `dotnet` build via MCP `build_workspace` exit 0; tests green.  

---

## 6. Performance baseline (selected `_meta.elapsedMs`)

| tool | calls | p50_elapsedMs (approx) | budget_status | notes |
|------|-------|------------------------|---------------|-------|
| server_info | 1 | 4 | within | |
| workspace_load | 1 | 6761 | warn | cold load |
| project_diagnostics | 3 | 15–9789 | warn | full solution scan |
| compile_check | 4 | 444–9809 | within/warn | emitValidation upper band |
| test_run | 1 | 6751 | within | full suite filter |
| find_references | 2+ | 3–276 | within | hot symbol |
| get_di_registrations | 1 | 757 | within | |

---

## 7. Schema vs behaviour drift

| tool | mismatch_kind | expected | actual | severity | notes |
|------|---------------|----------|--------|----------|-------|
| type_hierarchy / find_references | description_stale | metadataName alone | requires symbolHandle or file location | FLAG | docs: use symbolHandle after symbol_search |
| find_references_bulk | wrong_default | first attempt used wrong JSON key | error | FLAG | schema uses `symbols` array |
| get_completions | return_shape | ranked locals first | empty Items at probe | FLAG | probe position may be invalid for completions |

**Otherwise:** No schema/behaviour drift observed for exercised paths.

---

## 8. Error message quality (negative / edge probes)

| tool | probe_input | rating | suggested_fix | notes |
|------|-------------|--------|---------------|-------|
| workspace_status | non-existent workspace id | actionable | lists `workspace_list` | v1.8+ style |
| goto_type_definition | position on `left` parameter | actionable | pick type token | NotFound clear |
| fix_all_preview | IDE0005 solution | actionable | use organize_usings per guidance | GuidanceMessage helpful |

---

## 9. Parameter-path coverage

| family | non_default_path_tested | status | notes |
|--------|---------------------------|--------|-------|
| project_diagnostics | severity=Info, Error; project=Api | pass | totals invariant for errors |
| compile_check | emitValidation=true; pagination | pass | |
| list_analyzers | offset/limit | pass | |
| test_run | `--filter Category!=E2E` | pass | |
| symbol_search | kind=Class, Interface | pass | |

---

## 10. Prompt verification (Phase 16)

**Phase 16 blocked —** Cursor agent session cannot invoke MCP **prompts** (`prompts/list` / `prompts/get` not exercised via `call_mcp_tool`). No per-prompt rows rendered (would be misleading).

---

## 11. Skills audit (Phase 16b)

**Phase 16b blocked — Roslyn-Backed-MCP repository with `skills/*/SKILL.md` is not in this workspace.**

---

## 12. Experimental promotion scorecard (rollup)

- **All experimental tools:** mix of `exercised`, `exercised-preview-only`, `needs-more-evidence`, `skipped-safety` — see §3. **Default for unexercised:** `needs-more-evidence`.  
- **All experimental prompts (16):** `needs-more-evidence` / effectively **blocked** (no client prompt channel this session).  
- **Recommendation:** Do not promote any experimental prompt row without a prompt-capable client run.

---

## 13. Debug log capture

`client did not surface MCP log notifications`

---

## 14. MCP server issues (bugs)

| severity | issue | notes |
|----------|-------|-------|
| cosmetic | `find_references_bulk` first invocation failed until correct parameter shape used | Operator UX: surface schema example in error |
| **No new crash-level defects observed** | | |

---

## 15. Improvement suggestions

- Surface **prompt** invocation in Cursor MCP bridge so Phase 16 can run without marking 16 rows blocked.  
- `get_completions` empty result could include hint when caret is not in expression context.  
- Consider **resource** round-trip test harness (`roslyn://workspace/{id}/file/...`) in one scripted follow-up after `workspace_load`.

---

## 16. Concurrency matrix (Phase 8b)

**8b.2 / 8b.3 / 8b.4 parallel & lifecycle:** **blocked — client serializes MCP tool calls** (Cursor); no true parallel fan-out or interleaved R/W timing.  

**8b.1 sequential baselines (representative `_meta.elapsedMs`):**

| Probe | Tool | elapsedMs |
|-------|------|------------|
| R1 | find_references (DriftDetector) | 276 |
| R2 | project_diagnostics (unfiltered page) | 9789 |
| R3 | semantic_search | 107 |
| R4 | find_unused_symbols (private) | 860 |
| R5 | get_complexity_metrics | 71 |

**8b.5 Writer verification:**

| tool | status | wall-clock (ms) | notes |
|------|--------|-----------------|-------|
| apply_text_edit | pass | 2–17 | Program.cs |
| apply_multi_file_edit | N/A | — | not run |
| revert_last_apply | pass | 2661–2995 | |
| set_editorconfig_option | N/A | — | not run this pass |
| set_diagnostic_severity | N/A | — | not run |
| add_pragma_suppression | N/A | — | not run |

---

## 17. Writer reclassification verification (Phase 8b.5)

See §16 table. Six writers not all exercised — documented as **N/A** with reason (session budget / non-destructive focus).

---

## 18. Response contract consistency

**N/A —** no cross-tool field-name mismatch observed beyond `find_references_bulk` parameter naming (user error).

---

## 19. Known issue regression check (Phase 18)

**N/A — no prior MCP-specific backlog rows** in `ai_docs/backlog.md` (open work is product-phase deferred, not Roslyn MCP regressions). No historical MCP audit file in-repo was used as baseline.

---

## 20. Known issue cross-check

**N/A**

---

## Phase journal (draft checkpoints)

- **Phase 0–2:** Completed — baseline, diagnostics, metrics (see draft sections merged above).  
- **Phase 3–5:** DriftDetector + YamlConfigLoader flow + snippets.  
- **Phase 6:** Rename apply + previews.  
- **Phase 7:** `get_editorconfig_options` only (set_* deferred).  
- **Phase 8:** build, test, coverage.  
- **Phase 8b:** Sequential baselines + text-edit/revert writers; parallel blocked.  
- **Phase 10:** `move_type_to_file_preview` only.  
- **Phase 9:** Undo audit on Program.cs.  
- **Phase 11–15:** Partial (semantic_search, DI, reflection, navigation samples; resources mostly needs-more-evidence).  
- **Phase 16–16b:** Blocked as documented.  
- **Phase 17:** Invalid workspace id; bad `goto_type_definition` probe; `workspace_close`.  
- **Phase 18:** N/A.  

---

## Final surface closure checklist

1. Ledger §3 accounts for **123 tools + 9 resources + 16 prompts** with explicit status (no silent omissions).  
2. Audit-only mutations **reverted** (`Program.cs` probes); **rename** intentionally retained as Phase 6 product change.  
3. `workspace_close` invoked.  
4. Experimental scorecard: rollup in §12 (prompts blocked / needs-more-evidence).  

---

*End of report.*
