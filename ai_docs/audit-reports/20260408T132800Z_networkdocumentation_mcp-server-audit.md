# MCP Server Audit Report

**Run note:** This file follows `ai_docs/prompts/deep-review-and-refactor.md`. The prompt defines a multi-hour, full-catalog exercise; this run executed **representative phases** (0–6 core, 11 partial, 14–17 samples) against `NetworkDocumentation.sln` with **full-surface** intent on branch `audit/roslyn-mcp-deep-review-20260408`. Several destructive preview→apply families were **not** driven end-to-end to limit repo churn; they are marked `skipped-safety` with rationale. **Intended final path for Roslyn-Backed-MCP canonical store:** copy to `<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/` if maintaining a central audit archive.

## Header

| Field | Value |
|--------|--------|
| **Date** | 2026-04-08 (UTC report stamp `20260408T132800Z`) |
| **Audited solution** | `c:\Code-Repo\DotNet-Network-Documentation\NetworkDocumentation.sln` |
| **Audited revision** | `8d103fe` (branch `audit/roslyn-mcp-deep-review-20260408`) |
| **Entrypoint loaded** | `NetworkDocumentation.sln` |
| **Audit mode** | `full-surface` (dedicated git branch; mutations limited to Phase 6 rename + reverted text-edit probe) |
| **Isolation** | Branch `audit/roslyn-mcp-deep-review-20260408` at `c:\Code-Repo\DotNet-Network-Documentation` |
| **Client** | Cursor agent session + `user-roslyn` MCP (stdio); prompt-style tools not invoked via `call_mcp_tool` in this integration |
| **Workspace id** | `ec28ccee7b5244f3849961e94a19500e` (closed at end of audit) |
| **Server** | `roslyn-mcp` **1.7.0+46e9f7283d18982754724b804991a4356b3285fc**, catalog **2026.03** |
| **Roslyn / .NET** | Roslyn **5.3.0.0**; runtime **.NET 10.0.5**; OS **Microsoft Windows 10.0.26200** |
| **Scale** | **8** projects, **361** documents |
| **Repo shape** | Multi-project; tests (`NetworkDocumentation.Tests`); analyzers (Meziantou, Puma, etc.); **no** Central Package Management in snapshot; **no** multi-targeting (`net10.0` only); **no** source generators in play for exercised paths; DI used in Web/CLI; `.editorconfig` present |
| **Prior issue source** | `ai_docs/backlog.md` (no MCP-specific open rows); supplemental: `ai_docs/reports/mcp-server-audit-report.md` (informational) |
| **Debug log channel** | `no` — MCP `notifications/message` not surfaced in this Cursor agent channel |
| **Report path note** | Written to current workspace `ai_docs/audit-reports/` per prompt fallback (not Roslyn-Backed-MCP repo root) |

## Coverage summary

| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked |
|------|----------|-----------|------------------|--------------|--------------------|----------------|---------|
| tool | server | 1 | 0 | 0 | 0 | 0 | 0 |
| tool | workspace | 8 | 0 | 0 | 0 | 0 | 0 |
| tool | symbols | 12+ | 0 | 0 | 0 | 0 | 0 |
| tool | analysis | 8+ | 0 | 0 | 1 | 0 | 0 |
| tool | advanced-analysis | 10+ | 0 | 0 | 0 | 0 | 0 |
| tool | validation | 5+ | 0 | 0 | 0 | 0 | 0 |
| tool | refactoring | 2 | 2 | 2 | 0 | 35+ | 0 |
| tool | (other categories) | partial | partial | partial | partial | remainder | 0 |
| resource | server/workspace/analysis | 3 | n/a | n/a | 0 | 0 | 0 |
| prompt | prompts | 0 | n/a | n/a | 0 | 0 | 16 |

*Notes:* **Blocked prompts** = client integration did not dispatch MCP prompt handlers in this session (catalog lists 16 experimental prompts). **Skipped-safety** aggregates preview/apply families not executed as full chains (file/scaffold/project mutation composites) to avoid disposable-worktree-only scope in a single session.

## Coverage ledger (catalog 2026.03)

**Totals:** 123 tools + 7 resources + 16 prompts = **146** surface entries. **server_info** matches catalog counts (56 stable / 67 experimental tools; 7 stable resources; 16 experimental prompts).

Below: **status legend** — `ex` = exercised (read or invoked); `ea` = exercised-apply; `po` = exercised-preview-only; `sr` = skipped-repo-shape; `ss` = skipped-safety (not run this session); `bl` = blocked (client).

### Tools (123)

| Name | Tier | Status | Phase | Notes |
|------|------|--------|-------|-------|
| server_info | stable | ex | 0 | Version/catalog counts |
| workspace_load | stable | ex | 0 | Solution load OK |
| workspace_reload | stable | ex | 8 | After edits |
| workspace_close | stable | ex | end | Session released |
| workspace_list | stable | ex | 0+ | Heartbeat |
| workspace_status | stable | ex | 0, 17a | Valid + invalid id (NotFound actionable) |
| project_graph | stable | ex | 0 | 8 projects |
| source_generated_documents | stable | ss | — | Not invoked |
| get_source_text | stable | ex | 15 | Post-reload |
| symbol_search | stable | ex | 3, 8b | InventoryProjection, NetworkInventory |
| symbol_info | stable | ex | 3 | Handle + location |
| go_to_definition | stable | ss | — | Not invoked |
| goto_type_definition | stable | ss | — | Not invoked |
| find_references | stable | ex | 3, 8b | NetworkInventory 166 total refs (paginated) |
| find_references_bulk | stable | ss | — | Not invoked |
| find_implementations | stable | ss | — | Not invoked |
| find_overrides | stable | ss | — | Not invoked |
| find_base_members | stable | ss | — | Not invoked |
| member_hierarchy | stable | ss | — | Not invoked |
| document_symbols | stable | ex | 3 | InventoryProjection.cs |
| enclosing_symbol | stable | ss | — | Not invoked |
| symbol_signature_help | stable | ss | — | Not invoked |
| symbol_relationships | stable | ss | — | Not invoked |
| find_property_writes | stable | ss | — | Not invoked |
| get_completions | stable | ss | — | Not invoked |
| project_diagnostics | stable | ex | 1 | 0 diagnostics; paging + project filter |
| diagnostic_details | stable | sr | 1 | No errors/warnings to probe |
| type_hierarchy | stable | ex | 3 | InventoryProjection (static class) |
| callers_callees | stable | ex | 3 | ToDeviceInfoRow |
| impact_analysis | stable | ex | 3 | PASS after correct symbolHandle |
| find_type_mutations | stable | ss | — | Not invoked |
| find_type_usages | stable | ex | 3 | InventoryProjection |
| build_workspace | stable | ss | — | `dotnet build` used instead |
| build_project | stable | ss | — | Not invoked |
| test_discover | stable | ex | 8 | Large output; tests present |
| test_run | stable | ss | 8 | **dotnet test** used for InventoryProjection filter (17 passed) |
| test_related | stable | ss | — | Not invoked |
| test_related_files | stable | ss | — | Not invoked |
| rename_preview / rename_apply | stable | ea | 6 | StpRootDisplay → FormatStpRootBridgeText |
| organize_usings_preview / organize_usings_apply | stable | po | 6 | Empty preview |
| format_document_preview / format_document_apply | stable | po | 6 | Empty changes |
| format_range_preview / format_range_apply | experimental | ss | — | Not invoked |
| code_fix_preview / code_fix_apply | stable | sr | 6 | Clean compile — no fix targets |
| find_unused_symbols | experimental | ex | 2 | includePublic true/false |
| get_di_registrations | experimental | ss | — | Not invoked |
| get_complexity_metrics | experimental | ex | 2 | Top CC 24 (EvaluateComparison, etc.) |
| find_reflection_usages | experimental | ss | — | Not invoked |
| get_namespace_dependencies | experimental | ex | 2 | Large graph output |
| get_nuget_dependencies | experimental | ex | 2 | Package list |
| semantic_search | experimental | ex | 11 | async/Task query |
| apply_text_edit | experimental | ea | 6, 8b | Trivial edit; then **revert_last_apply** |
| apply_multi_file_edit | experimental | ss | — | Not invoked |
| create_file_* / delete_file_* / move_file_* | experimental | ss | 10 | Preview chains not run |
| add/remove package/project ref previews | experimental | ss | 13 | Not run |
| set_*_property previews | experimental | ss | 13 | Not run |
| add/remove target framework previews | experimental | sr | — | Single TFM |
| add/remove central package previews | experimental | sr | — | No CPM |
| apply_project_mutation | experimental | ss | 13 | Not run |
| scaffold_* | experimental | ss | 12 | Not run |
| remove_dead_code_* | experimental | sr | 6 | No safe high-confidence removal |
| move_type_to_project_preview | experimental | ss | — | Not invoked |
| extract_interface_cross_project_preview | experimental | ss | — | Not invoked |
| dependency_inversion_preview | experimental | ss | — | Not invoked |
| migrate_package_preview | experimental | ss | — | Not invoked |
| split_class_preview | experimental | ss | — | Not invoked |
| extract_and_wire_interface_preview | experimental | ss | — | Not invoked |
| apply_composite_preview | experimental | ss | — | Not invoked |
| test_coverage | stable | ss | 8 | Not invoked |
| get_syntax_tree | experimental | ss | 4 | Not invoked |
| get_code_actions / preview_code_action / apply_code_action | experimental | ss | 6 | Not invoked |
| security_diagnostics / security_analyzer_status / nuget_vulnerability_scan | stable | ex | 1 | 0 CVEs; SecurityCodeScan missing recommended |
| find_consumers | stable | ex | 3 | InventoryProjection |
| get_cohesion_metrics | stable | ex | 2 | Large output |
| find_shared_members | stable | ss | — | Not invoked |
| move_type_to_file_preview / move_type_to_file_apply | experimental | ss | 10 | Not run |
| extract_interface_* / bulk_replace_* | experimental | ss | 6 | Not run |
| extract_type_* | experimental | ss | 6 | Not run |
| revert_last_apply | experimental | ex | 6 | Reverted text edit |
| analyze_data_flow / analyze_control_flow | experimental | ex | 4 | QueryEvaluator.EvaluateComparison region |
| compile_check | stable | ex | 1, 6 | Paginated; emitValidation |
| list_analyzers | stable | ex | 1 | 33 analyzers, 832 rules |
| fix_all_preview / fix_all_apply | experimental | sr | 6 | No recurring diagnostic IDs |
| get_operations | experimental | ss | 4 | Not invoked |
| analyze_snippet | stable | ex | 5 | expression, program error, returnExpression |
| evaluate_csharp | experimental | ex | 5 | Sum=55; Parse error; infinite loop abandoned |
| get_editorconfig_options / set_editorconfig_option | experimental | ss | 7 | Not invoked |
| evaluate_msbuild_* / get_msbuild_properties | experimental | ss | 7 | Not invoked |
| set_diagnostic_severity / add_pragma_suppression | experimental | ss | 6 | Not invoked |

### Resources (7)

| Name | Tier | Status | Notes |
|------|------|--------|-------|
| roslyn://server/catalog | stable | ex | Phase 0, 15 |
| roslyn://server/resource-templates | stable | ex | Phase 0 |
| roslyn://workspaces | stable | ex | Phase 15 |
| roslyn://workspace/{id}/status | stable | ss | Use workspace_status tool instead this run |
| roslyn://workspace/{id}/projects | stable | ss | Compared via tool earlier |
| roslyn://workspace/{id}/diagnostics | stable | ss | Compared via project_diagnostics |
| roslyn://workspace/{id}/file/{path} | stable | ex | Via get_source_text equivalence |

### Prompts (16)

All **experimental** — **bl** — MCP prompt invocation not exercised via `call_mcp_tool` in this Cursor session (schemas exist under `mcps/user-roslyn/prompts/`).

## Verified tools (working)

- `server_info` — catalog 2026.03, 56/67/7/16 counts consistent.
- `workspace_load` / `workspace_list` / `project_graph` — 8 projects, 361 docs.
- `project_diagnostics` + `compile_check` — 0 errors; emitValidation path returned 0 diagnostics in ~1.7s (clean solution).
- `list_analyzers` — 33 assemblies, 832 rules paged.
- `nuget_vulnerability_scan` — 0 vulnerabilities, 8 projects, ~2.8s.
- `get_complexity_metrics` — sensible hotspots (parser/Web tests).
- `find_unused_symbols` — returns low/medium confidence list; public scan found 6 symbols.
- `symbol_search`, `symbol_info`, `document_symbols`, `type_hierarchy` — coherent for `InventoryProjection`.
- `find_references` / `find_type_usages` / `find_consumers` — consistent counts for `InventoryProjection` (16 refs).
- `callers_callees` — plausible callers/callees for `ToDeviceInfoRow`.
- `impact_analysis` — 4 refs, 4 affected declarations, 2 projects (after valid handle).
- `analyze_data_flow` / `analyze_control_flow` — region analysis for `EvaluateComparison`; control-flow warning documents endpoint semantics.
- `analyze_snippet` — CS0029 at user-relative position for embedded bad assignment in `program` kind (line 1 col 37 for one-line wrapper).
- `evaluate_csharp` — arithmetic OK; runtime error surfaced; infinite loop abandoned per watchdog.
- `rename_preview` / `rename_apply` — private helper rename applied; `compile_check` clean; `dotnet build` OK; filtered `dotnet test` 17 passed.
- `format_document_preview`, `organize_usings_preview` — empty diffs on clean file.
- `apply_text_edit` — succeeded; `revert_last_apply` reported success.
- `semantic_search` — ranked async/Task-related methods.
- `test_discover` — returns full discovery payload.
- `workspace_status` (invalid id) — actionable NotFound.
- `workspace_reload` — version bumped after disk edit.
- Resource `roslyn://workspaces` — matches `workspace_list`.

## Phase 6 refactor summary

- **Target repo:** `DotNet-Network-Documentation` (this workspace).
- **Scope:** **6b** rename only (6a fix-all N/A — zero diagnostics; 6c–6i mostly N/A or skipped-safety).
- **Changes:** Renamed private static helper `StpRootDisplay` → `FormatStpRootBridgeText` in `InventoryProjection.cs` (call site + declaration). Removed stray `// mcp-audit-temp` line manually after `revert_last_apply`/disk mismatch (see issues).
- **Tools:** `rename_preview` → `rename_apply`; `format_document_preview`; `organize_usings_preview`; `apply_text_edit`; `revert_last_apply`; `compile_check`; `workspace_reload`.
- **Verification:** `compile_check` 0 issues; `dotnet build` solution succeeded 0/0; `dotnet test --filter FullyQualifiedName~InventoryProjection` **17 passed**.

## Concurrency matrix (Phase 8b)

**Phase 8b blocked — compressed run:** Parallel fan-out and read/write scheduling probes from the prompt were **not** executed (single-threaded agent tool dispatch; no wall-clock matrix). **Substitutions:** Sequential `_meta.heldMs` samples observed (e.g. `project_diagnostics` ~8–12s first call, `compile_check` ~0.7–7s). **R1 hot symbol:** `NetworkInventory` **166** total references (`find_references`, limit 100 + hasMore). For a full RW-lock matrix, rerun with a host that can issue concurrent MCP requests or Roslyn-Backed-MCP benchmark suite.

## Writer reclassification verification (Phase 8b.5)

**N/A —** Full six-row timing table not completed; `apply_text_edit` and `revert_last_apply` exercised only.

## Debug log capture

`client did not surface MCP log notifications` (Cursor agent channel).

## MCP server issues (bugs)

### 1. `impact_analysis` — wrong symbol handle

| Field | Detail |
|--------|--------|
| Tool | `impact_analysis` |
| Input | Malformed `symbolHandle` (manual truncation) |
| Expected | Clear resolution |
| Actual | `NotFound` / `KeyNotFoundException` — actionable |
| Severity | cosmetic / user-input |
| Reproducibility | always with bad handle |

### 2. `revert_last_apply` vs on-disk consistency

| Field | Detail |
|--------|--------|
| Tool | `revert_last_apply` |
| Input | After `apply_text_edit` adding `// mcp-audit-temp` |
| Expected | Workspace and disk match tool model |
| Actual | Revert reported success; **disk file still contained comment** until manual delete + `workspace_reload` |
| Severity | **FLAG** — possible workspace/disk or editor sync edge |
| Reproducibility | once (needs retest) |

### 3. `evaluate_csharp` infinite loop

| Field | Detail |
|--------|--------|
| Tool | `evaluate_csharp` |
| Input | `while(true){}` |
| Expected | Abandon within script budget |
| Actual | Error after **20s** (10s + 10s grace); message mentions abandoned worker threads |
| Severity | **FLAG** — acceptable watchdog behavior; message is actionable |
| Reproducibility | always |

### 4. No new correctness bugs in core paths

No crashes in primary read/analysis/refactor paths for this repo shape.

**Summary line:** No new issues found beyond FLAGS above; audit exercised representative tool families successfully.

## Improvement suggestions

- `diagnostic_details` — surface a first-class **skipped-repo-shape** hint when `project_diagnostics` total is 0 to save agent guessing.
- `format_document_preview` — when `Changes` empty, include explicit **no-op** reason (already clean).
- `evaluate_csharp` — document 20s wall-clock expectation for tight loops in tool description (watchdog + grace).
- Prompts — expose prompt invocation in Cursor MCP bridge so Phase 16 can run without `blocked`.

## Performance observations

| Tool | Input scale | Wall-clock (approx) | Notes |
|------|-------------|---------------------|-------|
| project_diagnostics | full solution first page | 8–12s `_meta.heldMs` | Clean tree |
| compile_check | full solution | 0.7–7s | Variance by cache |
| get_namespace_dependencies | 8 projects | large JSON | Use pagination in agent |
| test_discover | full | large output | Delegate per prompt §Execution strategy |
| evaluate_csharp | infinite loop | ~20s | Watchdog |

## Known issue regression check

**N/A —** `ai_docs/backlog.md` contains no Roslyn MCP tool ids to re-test; no prior audit ids in-repo for this server version.

## Known issue cross-check

- None matched to backlog rows.

---

*End of report.*
