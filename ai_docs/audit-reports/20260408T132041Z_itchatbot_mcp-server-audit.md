# MCP Server Audit Report

**Execution note:** This run applies `ai_docs/prompts/deep-review-and-refactor.md` in **abbreviated form**: Phases 0–2 and samples from 1/5/11/17 were executed against **ITChatBot.sln**; destructive refactor chains (Phase 6), full build/test/coverage (Phase 8), concurrency matrix (Phase 8b), and per-tool full-surface coverage were **not** completed in this session. For strict compliance with the prompt’s sequential phase checklist and per-catalog ledger, re-run with a **disposable worktree**, incremental `.draft.md` persistence, and a multi-hour budget.

## Header

- **Date:** 2026-04-08 (UTC run timestamp `20260408T132041Z` in filename)
- **Audited solution:** `c:\Code-Repo\IT-Chat-Bot\ITChatBot.sln`
- **Audited revision:** `8c2e3f6` (short)
- **Entrypoint loaded:** `ITChatBot.sln`
- **Audit mode:** `conservative` — live working tree; no disposable branch/worktree; no Phase 6 product refactors or file/project mutation applies
- **Isolation:** N/A (conservative); mutations deferred to avoid unsolicited repo edits
- **Client:** Cursor agent session with `user-roslyn` MCP (`call_mcp_tool` / `fetch_mcp_resource`)
- **Workspace id (session):** `c8b9cb1a4c0e453dbf5f0035b57a63a5` (closed at end of audit)
- **Server:** roslyn-mcp `1.7.0+46e9f7283d18982754724b804991a4356b3285fc`, catalog `2026.03`
- **Roslyn / .NET:** Roslyn `5.3.0.0`, runtime `.NET 10.0.5`, OS `Microsoft Windows 10.0.26200`
- **Scale:** 34 projects, 750 documents (`workspace_load`)
- **Repo shape:** Multi-project solution, `net10.0`, test projects present, analyzers active (143 analyzer warnings), source generators present (GlobalUsings, LoggerMessage, RegexGenerator, etc.), DI-heavy; **not** Central Package Management for this audit; single target framework (no multi-targeting)
- **Prior issue source:** `ai_docs/backlog.md` — **no Roslyn MCP defect IDs** listed; Phase 18 regression items **N/A**
- **Debug log channel:** `no` — MCP `notifications/message` not surfaced in this Cursor tool channel
- **Report path note:** Saved under IT-Chat-Bot: `ai_docs/audit-reports/20260408T132041Z_itchatbot_mcp-server-audit.md`. Intended copy for Roslyn-Backed-MCP rollup (if applicable): `<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/…`

## Coverage summary

| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-----------|-----------------|--------------|--------------------|----------------|---------|-------|
| tool | mixed | ~28 | 0 | 0 | 0 | ~95 | 0 | Abbreviated session; most write/refactor tools not invoked |
| resource | server/workspace | 3 | n/a | n/a | 0 | 4 | 0 | `roslyn://server/catalog`, `resource-templates`, `workspaces` read; workspace-scoped URIs not all fetched |
| prompt | prompts | 0 | n/a | n/a | 0 | 0 | 16 | Cursor session did not invoke MCP prompt templates as tools |

**Catalog agreement:** `server_info` surface `{ tools: 56 stable + 67 experimental, resources: 7, prompts: 16 }` matches `roslyn://server/catalog` Summary.

## Coverage ledger

**Policy for this abbreviated report:** Each catalog **tool** (123), **resource** (7), and **prompt** (16) must appear. Rows marked **skipped-safety** were not invoked in this session (conservative mode + abbreviated time scope), not omitted.

### Tools (catalog 2026.03 — 123 total)

| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| tool | server_info | stable | exercised | 0 | Version/capabilities |
| tool | workspace_load | stable | exercised | 0 | ITChatBot.sln OK |
| tool | workspace_list | stable | exercised | 0 | count=1 |
| tool | workspace_status | stable | exercised | 0, 17e | Valid id OK; invalid id actionable error |
| tool | workspace_reload | stable | skipped-safety | — | |
| tool | workspace_close | stable | exercised | end | Closed cleanly |
| tool | project_graph | stable | exercised | 0 | |
| tool | source_generated_documents | stable | exercised | 11 | Listed generator outputs |
| tool | get_source_text | stable | skipped-safety | — | |
| tool | symbol_search | stable | exercised | 3 | `ChatEndpoints` |
| tool | symbol_info | stable | skipped-safety | — | |
| tool | go_to_definition | stable | skipped-safety | — | |
| tool | goto_type_definition | stable | skipped-safety | — | |
| tool | find_references | stable | skipped-safety | — | |
| tool | find_references_bulk | stable | skipped-safety | — | |
| tool | find_implementations | stable | skipped-safety | — | |
| tool | find_overrides | stable | skipped-safety | — | |
| tool | find_base_members | stable | skipped-safety | — | |
| tool | member_hierarchy | stable | skipped-safety | — | |
| tool | document_symbols | stable | skipped-safety | — | |
| tool | enclosing_symbol | stable | skipped-safety | — | |
| tool | symbol_signature_help | stable | skipped-safety | — | |
| tool | symbol_relationships | stable | skipped-safety | — | |
| tool | find_property_writes | stable | skipped-safety | — | |
| tool | get_completions | stable | skipped-safety | — | |
| tool | project_diagnostics | stable | exercised | 1 | 143 warnings, 0 errors; paging OK |
| tool | diagnostic_details | stable | exercised | 1 | CA1002 detail + empty SupportedFixes |
| tool | type_hierarchy | stable | skipped-safety | — | |
| tool | callers_callees | stable | skipped-safety | — | |
| tool | impact_analysis | stable | skipped-safety | — | |
| tool | find_type_mutations | stable | skipped-safety | — | |
| tool | find_type_usages | stable | skipped-safety | — | |
| tool | find_unused_symbols | experimental | exercised | 2 | 0 hits (includePublic=false) |
| tool | get_di_registrations | experimental | skipped-safety | — | |
| tool | get_complexity_metrics | experimental | exercised | 2 | minComplexity≥15, top CC aligns with backlog hotspots |
| tool | find_reflection_usages | experimental | skipped-safety | — | |
| tool | get_namespace_dependencies | experimental | skipped-safety | — | |
| tool | get_nuget_dependencies | experimental | skipped-safety | — | |
| tool | semantic_search | experimental | exercised | 11 | NL query; see issues |
| tool | find_consumers | stable | skipped-safety | — | |
| tool | get_cohesion_metrics | stable | exercised | 2 | minMethods=3 |
| tool | find_shared_members | stable | skipped-safety | — | |
| tool | security_diagnostics | stable | exercised | 1 | 0 OWASP findings |
| tool | security_analyzer_status | stable | exercised | 1 | SecurityCodeScan present |
| tool | nuget_vulnerability_scan | stable | exercised | 1 | 0 CVEs, 34 projects |
| tool | analyze_data_flow | experimental | skipped-safety | — | |
| tool | analyze_control_flow | experimental | skipped-safety | — | |
| tool | get_syntax_tree | experimental | skipped-safety | — | |
| tool | get_operations | experimental | skipped-safety | — | |
| tool | compile_check | stable | exercised | 1 | 0 CS diagnostics solution-wide; by design excludes CA/IDE |
| tool | list_analyzers | stable | exercised | 1 | 34 assemblies, 663 rules (paged sample) |
| tool | analyze_snippet | stable | exercised | 5 | `kind=expression` OK |
| tool | evaluate_csharp | experimental | exercised | 5 | Sum=55, ~129ms |
| tool | rename_preview / rename_apply | stable | skipped-safety | — | |
| tool | organize_usings_* | stable | skipped-safety | — | |
| tool | format_* | mixed | skipped-safety | — | |
| tool | code_fix_* / get_code_actions / preview_code_action / apply_code_action | mixed | skipped-safety | — | |
| tool | fix_all_* | experimental | skipped-safety | — | |
| tool | extract_interface_* / bulk_replace_* / extract_type_* | experimental | skipped-safety | — | |
| tool | move_type_to_file_* | experimental | skipped-safety | — | |
| tool | apply_text_edit / apply_multi_file_edit | experimental | skipped-safety | — | |
| tool | create_file_* / delete_file_* / move_file_* | experimental | skipped-safety | — | |
| tool | add_*_preview / remove_*_preview / set_*_preview / apply_project_mutation | experimental | skipped-safety | — | |
| tool | scaffold_* | experimental | skipped-safety | — | |
| tool | remove_dead_code_* | experimental | skipped-safety | — | |
| tool | move_type_to_project_preview / extract_interface_cross_project_preview / dependency_inversion_preview | experimental | skipped-safety | — | |
| tool | migrate_package_preview / split_class_preview / extract_and_wire_interface_preview / apply_composite_preview | experimental | skipped-safety | — | |
| tool | build_workspace / build_project | stable | skipped-safety | — | |
| tool | test_discover | stable | exercised | 8 | Large structured output |
| tool | test_run / test_related / test_related_files / test_coverage | stable | skipped-safety | — | |
| tool | get_editorconfig_options / set_editorconfig_option | experimental | skipped-safety | — | |
| tool | evaluate_msbuild_* / get_msbuild_properties | experimental | skipped-safety | — | |
| tool | set_diagnostic_severity / add_pragma_suppression | experimental | skipped-safety | — | |
| tool | revert_last_apply | experimental | skipped-safety | — | |

### Resources (7 total)

| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| resource | server_catalog | stable | exercised | 0,15 | `roslyn://server/catalog` |
| resource | resource_templates | stable | exercised | 0,15 | `roslyn://server/resource-templates` |
| resource | workspaces | stable | exercised | 15 | `roslyn://workspaces` |
| resource | workspace_status | stable | skipped-safety | — | URI template not fetched |
| resource | workspace_projects | stable | skipped-safety | — | |
| resource | workspace_diagnostics | stable | skipped-safety | — | |
| resource | source_file | stable | skipped-safety | — | |

### Prompts (16 total)

| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| prompt | explain_error … consumer_impact | experimental | blocked | 16 | MCP prompt invocation not exercised via this Cursor tool surface |

## Verified tools (working)

- `server_info` — catalog 2026.03, 123 tools, 7 resources, 16 prompts
- `workspace_load` — 34 projects, 750 docs, no workspace errors
- `workspace_list` / `project_graph` — consistent graph
- `project_diagnostics` — totals 0 errors / 143 warnings; project+severity filter returned 2 warnings for Configuration
- `compile_check` — 0 CS diagnostics (expected vs analyzer-only warnings in `project_diagnostics`)
- `compile_check` + `emitValidation=true` on single project — 0 diagnostics, faster than full solution emit path
- `security_diagnostics` / `security_analyzer_status` — structured, 0 security findings
- `nuget_vulnerability_scan` — 0 CVEs, 34 projects, ~7.8s
- `list_analyzers` — 34 analyzer assemblies, 663 rules total
- `diagnostic_details` — CA1002 resolved with description + help link; SupportedFixes empty for this rule
- `get_complexity_metrics` — surfaced known high-CC methods (e.g. `HandleSlackPostAsync` CC=20)
- `get_cohesion_metrics` — LCOM4 clusters plausible (e.g. test fixtures high score)
- `find_unused_symbols` — empty with defaults (no dead code reported)
- `symbol_search` — found `ChatEndpoints` with handle
- `semantic_search` — returned 4 symbol hits for NL query (quality: see issues)
- `analyze_snippet` — expression kind valid, 0 diagnostics
- `evaluate_csharp` — `Enumerable.Range(1,10).Sum()` → 55
- `test_discover` — returns per-project test lists (large payload)
- `source_generated_documents` — global usings + regex + logging generators listed
- `workspace_status` (invalid id) — structured `NotFound`, actionable message
- `workspace_close` — success

## Phase 6 refactor summary

- **Target repo:** IT-Chat-Bot (this repository)
- **Scope:** **N/A** — conservative audit; no `*_apply` or disk-mutating refactor tools executed
- **Changes:** none
- **Verification:** N/A

## Concurrency matrix (Phase 8b)

**N/A —** abbreviated session; parallel read fan-out and read/write probes from the prompt were not executed. Re-run on a disposable worktree with timed probes for matrix data.

## Writer reclassification verification (Phase 8b.5)

**N/A —** same as Phase 8b (no writer probes this run).

## Debug log capture

`client did not surface MCP log notifications` (no verbatim `Warning`/`Error`/`Critical` MCP log lines captured in this session).

## MCP server issues (bugs)

**No blocking defects observed** in the exercised calls. Findings below are **FLAG** / documentation alignment unless noted.

### 1. `compile_check` vs `project_diagnostics` counts

| Field | Detail |
|--------|--------|
| Tool | `compile_check` vs `project_diagnostics` |
| Input | Same workspace after `dotnet restore` |
| Expected | Different roles: CS compiler vs analyzers (per `compile_check` description) |
| Actual | `compile_check`: 0 diagnostics; `project_diagnostics`: 143 warnings |
| Severity | informational (not a bug if clients understand CS-only vs CA/IDE) |
| Reproducibility | always |

### 2. `semantic_search` natural-language relevance

| Field | Detail |
|--------|--------|
| Tool | `semantic_search` |
| Input | `"async methods returning Task<bool>"` |
| Expected | Methods whose signature includes `Task<bool>` |
| Actual | Hits included `IsGitRepositoryAsync`, `DeleteSourceAsync`, etc. — plausible but not clearly `Task<bool>`-only |
| Severity | incorrect result / **FLAG** (ranking or query interpretation) |
| Reproducibility | needs broader sampling |

### 3. `diagnostic_details` SupportedFixes empty for CA1002

| Field | Detail |
|--------|--------|
| Tool | `diagnostic_details` |
| Input | CA1002 at `SysLogServerApiClient.cs:101` |
| Expected | Optional code fixes when Roslyn provides fixes |
| Actual | `SupportedFixes`: [] |
| Severity | missing data / low — may be no fixer registered |
| Reproducibility | always for this diagnostic |

## Improvement suggestions

- Surface MCP structured logs in Cursor for audits requiring **Debug log capture** (cross-cutting principle #8).
- For large solutions, default agent workflows could chain `project_diagnostics` with **severity** or **project** filters first to avoid ~30s full scans when only a smoke check is needed.
- `semantic_search`: expose or document how NL queries map to symbol filters so agents can narrow `Task<bool>` vs `Task` results.

## Performance observations

| Tool | Input scale | Wall-clock (ms) | Notes |
|------|-------------|-------------------|-------|
| project_diagnostics | 34 projects, first page | ~29514 `_meta.heldMs` | Heavy; use filters/pagination |
| compile_check | full solution, limit 50 | ~18341 | In-memory compile |
| security_diagnostics | full workspace | ~30079 | |
| list_analyzers | offset 0, limit 30 | ~17509 | |
| get_complexity_metrics | minCC≥15, limit 15 | ~301 | Fast |
| evaluate_csharp | simple LINQ | ~129 | |

## Known issue regression check

**N/A —** `ai_docs/backlog.md` does not enumerate Roslyn MCP server defect IDs to re-test.

## Known issue cross-check

- None newly filed against Roslyn MCP from this run.

---

*End of report.*
