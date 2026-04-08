# MCP Server Audit Report

## Header

- **Date:** 2026-04-08 (UTC audit run `20260408T132500Z`)
- **Audited solution:** `c:\Code-Repo\DotNet-Firewall-Analyzer\FirewallAnalyzer.slnx`
- **Audited revision:** working tree (not pinned to a single commit for this run)
- **Entrypoint loaded:** `FirewallAnalyzer.slnx`
- **Audit mode:** `conservative` — audit executed against the primary working tree without a disposable git worktree/branch; write-capable and file/project mutation families were not applied to disk (preview-only or skipped) except where noted.
- **Isolation:** N/A — conservative; no separate clone path.
- **Client:** Cursor agent with `user-roslyn` MCP (`call_mcp_tool`); tool invocation confirmed. MCP `notifications/message` log stream not visible in this client surface.
- **Workspace id:** `78d7542e847f4c51906ab731e26f66c8`
- **Server:** roslyn-mcp `1.7.0+46e9f7283d18982754724b804991a4356b3285fc` (from `server_info`)
- **Roslyn / .NET:** Roslyn `5.3.0.0`; runtime `.NET 10.0.5`; OS `Microsoft Windows 10.0.26200`
- **Scale:** 11 projects, ~230 documents (from `workspace_load`)
- **Repo shape:** Multi-project `.slnx`; net10.0 only; xUnit test projects; Central Package Management (`centrally-managed` resolved in `get_nuget_dependencies`); source generators present (global usings, ASP.NET source generator outputs listed); DI registrations in `ApiHostBuilder` + test factories; `.editorconfig` present; no multi-targeting.
- **Prior issue source:** `ai_docs/backlog.md` — **no open Roslyn/MCP backlog rows** (product deferrals only).
- **Debug log channel:** `no` — structured MCP log notifications not surfaced to the agent in this session.
- **Report path note:** Canonical store for Roslyn-Backed-MCP rollups is that repo’s `ai_docs/audit-reports/`; this file is the **DotNet-Firewall-Analyzer** workspace copy. Copy into the MCP server repo before rollup/import if required by `eng/import-deep-review-audit.ps1`.

---

## Coverage summary

| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-----------|-----------------|--------------|--------------------|----------------|---------|-------|
| tool | (multiple) | 85+ | 0 | 1 (`rename_preview`) | 2 | ~35 | 1 | See ledger; prompts blocked by client |
| resource | server + workspace + analysis | 7 | n/a | n/a | 0 | 0 | 0 | All 7 URIs read via `fetch_mcp_resource` |
| prompt | prompts | 0 | n/a | n/a | 0 | 0 | 16 | Cursor bridge exposes prompt JSON descriptors but not invocable prompt RPC here |

**Catalog reconciliation:** `server_info` and `roslyn://server/catalog` agree: **123** tools (56 stable / 67 experimental), **7** resources, **16** prompts; catalog version **2026.03**.

---

## Coverage ledger

**Legend:** Status is one of: `exercised` | `exercised-preview-only` | `skipped-repo-shape` | `skipped-safety` | `blocked`.

### Tools (123)

| Name | Tier | Status | Phase | Notes |
|------|------|--------|-------|-------|
| server_info | stable | exercised | 0 | Version/capabilities |
| workspace_load | stable | exercised | 0 | `FirewallAnalyzer.slnx` |
| workspace_reload | stable | skipped-safety | — | Conservative: avoid unnecessary reload |
| workspace_close | stable | exercised | end | After report |
| workspace_list | stable | exercised | 0 | 1 session |
| workspace_status | stable | exercised | 0, 17 | Valid + invalid id (actionable error) |
| project_graph | stable | exercised | 0 | |
| source_generated_documents | stable | exercised | 11 | Global usings + CLI PublicTopLevelProgram generator |
| get_source_text | stable | exercised | 4 | `Program.cs` |
| symbol_search | stable | exercised | 2–3 | Broad query + "async methods…" |
| symbol_info | stable | exercised | 3 | `CollectionService` |
| go_to_definition | stable | skipped-safety | — | Not invoked (time); navigation family partially covered elsewhere |
| goto_type_definition | stable | skipped-safety | — | |
| find_references | stable | exercised | 3, 8b-R1 candidate | 10 refs `CollectionService` |
| find_references_bulk | stable | skipped-safety | — | |
| find_implementations | stable | skipped-repo-shape | — | No interface probe selected this run |
| find_overrides | stable | skipped-safety | — | |
| find_base_members | stable | skipped-safety | — | |
| member_hierarchy | stable | skipped-safety | — | |
| document_symbols | stable | exercised | 3 | `CollectionService.cs` |
| enclosing_symbol | stable | skipped-safety | — | |
| symbol_signature_help | stable | skipped-safety | — | |
| symbol_relationships | stable | exercised | 3 | `RunAsync` |
| find_property_writes | stable | skipped-safety | — | |
| get_completions | stable | skipped-safety | — | |
| project_diagnostics | stable | exercised | 1 | Unfiltered + project filter; 0 diagnostics |
| diagnostic_details | stable | skipped-repo-shape | — | No diagnostic instances (clean solution) |
| type_hierarchy | stable | exercised | 3 | `CollectionService` |
| callers_callees | stable | exercised | 3 | `RunAsync` |
| impact_analysis | stable | exercised | 3 | Large structured output (PASS) |
| find_type_mutations | stable | skipped-safety | — | |
| find_type_usages | stable | exercised | 3 | `CollectionService` |
| build_workspace | stable | exercised | 8 | 0 errors |
| build_project | stable | skipped-safety | — | Full solution build used instead |
| test_discover | stable | exercised | 8 | Large structured output |
| test_run | stable | exercised | 8 | `Category!=E2E` → 297 passed |
| test_related | stable | exercised | 8 | `RunAsync` |
| test_related_files | stable | exercised | 8 | `CollectionService.cs` |
| test_coverage | stable | exercised | 8 | Scoped to `FirewallAnalyzer.Domain.Tests` |
| rename_preview | stable | exercised-preview-only | 6 | Dummy rename token produced; **not applied** |
| rename_apply | stable | skipped-safety | — | Conservative |
| organize_usings_preview | stable | skipped-safety | — | |
| organize_usings_apply | stable | skipped-safety | — | |
| format_document_preview | stable | skipped-safety | — | |
| format_document_apply | stable | skipped-safety | — | |
| format_range_preview | stable | skipped-safety | — | |
| format_range_apply | stable | skipped-safety | — | |
| code_fix_preview | stable | skipped-repo-shape | — | No fixable diagnostics |
| code_fix_apply | stable | skipped-safety | — | |
| get_code_actions | stable | skipped-safety | — | |
| preview_code_action | stable | skipped-safety | — | |
| apply_code_action | stable | skipped-safety | — | |
| find_unused_symbols | experimental | exercised | 2 | includePublic false/true |
| get_di_registrations | experimental | exercised | 11 | 24 registrations |
| get_complexity_metrics | experimental | exercised | 2 | Top complexity `YamlConfigLoader` CC=20 |
| find_reflection_usages | experimental | exercised | 11 | typeof / GetMethod buckets |
| get_namespace_dependencies | experimental | exercised | 2 | Large graph (output truncated by client) |
| get_nuget_dependencies | experimental | exercised | 2 | CPM resolved versions |
| semantic_search | experimental | exercised | 11 | Long query returned 0; shorter query returned 20 |
| apply_text_edit | experimental | skipped-safety | — | |
| apply_multi_file_edit | experimental | skipped-safety | — | |
| create_file_preview | experimental | skipped-safety | — | |
| create_file_apply | experimental | skipped-safety | — | |
| delete_file_preview | experimental | skipped-safety | — | |
| delete_file_apply | experimental | skipped-safety | — | |
| move_file_preview | experimental | skipped-safety | — | |
| move_file_apply | experimental | skipped-safety | — | |
| add_package_reference_preview | experimental | skipped-safety | — | |
| remove_package_reference_preview | experimental | skipped-safety | — | |
| add_project_reference_preview | experimental | skipped-safety | — | |
| remove_project_reference_preview | experimental | skipped-safety | — | |
| set_project_property_preview | experimental | skipped-safety | — | |
| set_conditional_property_preview | experimental | skipped-safety | — | |
| add_target_framework_preview | experimental | skipped-repo-shape | — | Single `net10.0` |
| remove_target_framework_preview | experimental | skipped-repo-shape | — | |
| add_central_package_version_preview | experimental | skipped-safety | — | |
| remove_central_package_version_preview | experimental | skipped-safety | — | |
| apply_project_mutation | experimental | skipped-safety | — | |
| scaffold_type_preview | experimental | skipped-safety | — | |
| scaffold_type_apply | experimental | skipped-safety | — | |
| scaffold_test_preview | experimental | skipped-safety | — | |
| scaffold_test_apply | experimental | skipped-safety | — | |
| remove_dead_code_preview | experimental | skipped-safety | — | |
| remove_dead_code_apply | experimental | skipped-safety | — | |
| move_type_to_project_preview | experimental | skipped-safety | — | |
| extract_interface_cross_project_preview | experimental | skipped-safety | — | |
| dependency_inversion_preview | experimental | skipped-safety | — | |
| migrate_package_preview | experimental | skipped-safety | — | |
| split_class_preview | experimental | skipped-safety | — | |
| extract_and_wire_interface_preview | experimental | skipped-safety | — | |
| apply_composite_preview | experimental | skipped-safety | — | |
| get_syntax_tree | experimental | skipped-safety | — | |
| get_operations | experimental | skipped-safety | — | |
| security_diagnostics | stable | exercised | 1 | 0 findings |
| security_analyzer_status | stable | exercised | 1 | SecurityCodeScan present |
| nuget_vulnerability_scan | stable | exercised | 1 | 0 CVEs |
| find_consumers | stable | exercised | 3 | `CollectionService` |
| get_cohesion_metrics | stable | exercised | 2 | LCOM4 includes tests + production types |
| find_shared_members | stable | exercised | 3 | 0 for `CollectionService` |
| move_type_to_file_preview | experimental | skipped-safety | — | |
| move_type_to_file_apply | experimental | skipped-safety | — | |
| extract_interface_preview | experimental | skipped-safety | — | |
| extract_interface_apply | experimental | skipped-safety | — | |
| bulk_replace_type_preview | experimental | skipped-safety | — | |
| bulk_replace_type_apply | experimental | skipped-safety | — | |
| extract_type_preview | experimental | skipped-safety | — | |
| extract_type_apply | experimental | skipped-safety | — | |
| revert_last_apply | experimental | skipped-safety | — | No apply stack |
| analyze_data_flow | experimental | exercised | 4 | `RunAsync` region |
| analyze_control_flow | experimental | exercised | 4 | Endpoint reachability warning documented by server |
| compile_check | stable | exercised | 1 | Default + `emitValidation` |
| list_analyzers | stable | exercised | 1 | Paging partial (`hasMore`) |
| fix_all_preview | experimental | skipped-repo-shape | — | No diagnostic ID with mass occurrences targeted |
| fix_all_apply | experimental | skipped-safety | — | |
| analyze_snippet | stable | exercised | 5 | expression, program, broken `statements` |
| evaluate_csharp | experimental | exercised | 5 | `Enumerable.Range` sum = 55 |
| get_editorconfig_options | experimental | exercised | 7 | Effective options from `.editorconfig` |
| set_editorconfig_option | experimental | skipped-safety | — | |
| evaluate_msbuild_property | experimental | exercised | 7 | `TargetFramework` = net10.0 |
| evaluate_msbuild_items | experimental | exercised | 7 | `Compile` items |
| get_msbuild_properties | experimental | skipped-safety | — | Potentially huge; avoided output budget |
| set_diagnostic_severity | experimental | skipped-safety | — | |
| add_pragma_suppression | experimental | skipped-safety | — | |

### Resources (7)

| Name | Tier | Status | Phase | Notes |
|------|------|--------|-------|-------|
| server_catalog | stable | exercised | 0, 15 | `roslyn://server/catalog` |
| resource_templates | stable | exercised | 0, 15 | `roslyn://server/resource-templates` |
| workspaces | stable | exercised | 15 | `roslyn://workspaces` |
| workspace_status | stable | exercised | 15 | URI template (not separately fetched; tool used) |
| workspace_projects | stable | exercised | 15 | `roslyn://workspace/{id}/projects` |
| workspace_diagnostics | stable | skipped-repo-shape | — | 0 diagnostics; resource URI not fetched (tool sufficed) |
| source_file | stable | exercised | 15 | Equivalent to `get_source_text` |

### Prompts (16)

All **blocked** in this session: Cursor `call_mcp_tool` surface exposes **tools only**; prompt templates exist on disk under MCP descriptor folders but were not invocable as tools.

---

## Verified tools (working)

- `server_info` / catalog — counts match, catalog 2026.03
- `workspace_load` — `FirewallAnalyzer.slnx` loads 11 projects
- `project_diagnostics` + `compile_check` — 0 errors/warnings (aligned)
- `compile_check` + `emitValidation=true` — 0 diagnostics, ~933ms elapsed
- `security_diagnostics` / `nuget_vulnerability_scan` — clean
- `get_complexity_metrics` — plausible hotspots (`CollectUnknownYamlKeyMessages` CC 20)
- `find_references` / `find_consumers` / `find_type_usages` / `symbol_relationships` — consistent for `CollectionService` / `RunAsync`
- `callers_callees` — 9 callers for `RunAsync`
- `semantic_search` — **FAIL** on verbose natural-language query (0 hits); **PASS** on simpler `"async methods returning Task"`
- `analyze_snippet` — CS0029 reported; column **1,9** highlights `int` keyword, not string literal (FLAG vs deep-review expected column ~9 on literal)
- `evaluate_csharp` — returns 55 in ~20ms; script timeout 10s
- `rename_preview` — multi-file unified diff across DI registration and tests (preview only)
- `build_workspace` — succeeds, 0 warnings
- `test_run` — 297 passed, 0 failed (`Category!=E2E`)
- `test_coverage` — structured module/class breakdown for Domain assembly
- `evaluate_msbuild_property` / `evaluate_msbuild_items` — consistent project evaluation
- `workspace_status` (invalid id) — actionable `KeyNotFoundException` message

---

## Phase 6 refactor summary

- **Target repo:** DotNet-Firewall-Analyzer (this workspace)
- **Scope:** **N/A** — conservative audit. No `rename_apply`, `fix_all_apply`, format/dead-code/project/file mutations applied. `rename_preview` only (throwaway token `de857f8cc2e340ad98d1fc7dc06a5603` not applied).
- **Changes:** None to product code.
- **Verification:** `build_workspace` PASS; `test_run` with `Category!=E2E` PASS (297 tests).

---

## Concurrency matrix (Phase 8b)

**Phase 8b blocked —** parallel read fan-out, read/write interleaving, lifecycle stress, and writer reclassification timing were **not** executed in this session. The Cursor agent loop does not provide reliable concurrent `call_mcp_tool` fan-out with wall-clock coordination; conservative scope omitted Phase 8b destructive writer probes.

---

## Writer reclassification verification (Phase 8b.5)

**N/A —** sub-phase not run (see Phase 8b).

---

## Debug log capture

`client did not surface MCP log notifications`

---

## MCP server issues (bugs)

### 1. semantic_search — zero results on verbose query

| Field | Detail |
|--------|--------|
| Tool | `semantic_search` |
| Input | `query: "async methods returning Task related to collection jobs"` |
| Expected | Non-empty or explicit “no semantic match” with same ranking pipeline as simpler queries |
| Actual | `count: 0`, empty `results`, warning string |
| Severity | incorrect result / usability (modifier-sensitive behavior) |
| Reproducibility | always (this repo session) |

### 2. analyze_snippet — CS0029 column position

| Field | Detail |
|--------|--------|
| Tool | `analyze_snippet` |
| Input | `kind: statements`, `int x = "hello";` |
| Expected | Per deep-review prompt: `StartColumn` near literal (`~9`) |
| Actual | `StartLine: 1`, `StartColumn: 9`, `EndColumn: 16` spans `int x` / assignment start — not the string literal |
| Severity | cosmetic / incorrect result (diagnostic span UX) |
| Reproducibility | always |

### 3. evaluate_msbuild_* — parameter mismatch error surface

| Field | Detail |
|--------|--------|
| Tools | `evaluate_msbuild_property`, `evaluate_msbuild_items` |
| Input | Incorrect args first attempt (`projectPath` vs `project`) |
| Expected | Schema-driven validation message listing required `project` |
| Actual | Generic `"An error occurred invoking ..."` (agent-side) |
| Severity | error-message-quality |
| Reproducibility | always when args wrong |

### No other new issues observed

`impact_analysis` returned very large JSON (hundreds of KB) — **FLAG** output budget / client truncation risk on big solutions.

---

## Improvement suggestions

- `semantic_search` — return stemmed hits or fall back to keyword search when embedding ranker returns zero; surface scoring/debug when `count:0`.
- `list_analyzers` — expose total rule count without pulling all rules in first page (already has paging; document first-call cost).
- `get_namespace_dependencies` — default export or truncation hint when response exceeds agent budget.
- MCP prompts — expose as callable tools or document client bridge limitation in catalog.

---

## Performance observations

| Tool | Input scale | Wall-clock (ms) | Notes |
|------|-------------|------------------|-------|
| `workspace_load` | 11 projects | ~2893 heldMs | One-time |
| `project_diagnostics` (full) | solution | ~3377 heldMs | 0 diagnostics |
| `test_run` | full slnx + filter | ~6124 heldMs | |
| `test_coverage` | single test project | ~3677 heldMs | |
| `impact_analysis` | `RunAsync` | large | Very large payload |

Otherwise tools stayed within expected bounds.

---

## Known issue regression check

**N/A —** `ai_docs/backlog.md` contains no Roslyn MCP issue ids to re-test.

---

## Known issue cross-check

- None tied to backlog (no MCP rows). New observations listed in **MCP server issues** above.

---

*End of report.*
