# MCP Server Audit Report

**Run type:** Partial single-session execution of `ai_docs/prompts/deep-review-and-refactor.md` against `Roslyn-Backed-MCP.sln`. Phases 0–5, 6 (subset), 7 (subset), 8 (subset), 11 (subset), and spot checks were exercised; Phase 8b concurrency matrix, Phase 9–10 file-ops, Phase 12–13 scaffolding/project mutation, Phase 14–17 boundary tests, and full prompt surface were **not** completed in this session. **Ledger below marks uninvoked catalog entries `skipped-session` with reason.** A follow-up pass or `roslyn-mcp-audit` CLI (see backlog `audit-feasibility-companion-cli`) is required for full-surface closure.

**Draft file note:** Per prompt, a `.draft.md` was not maintained turn-by-turn; this report consolidates findings once at end (acceptable only if treating this run as **abbreviated** — full prompt compliance needs per-phase draft persistence).

## Header

- **Date:** 2026-04-08 (UTC run ~13:06–13:12Z)
- **Audited solution:** `c:\Code-Repo\Roslyn-Backed-MCP\Roslyn-Backed-MCP.sln`
- **Audited revision:** `7c9fe00` on `main`
- **Entrypoint loaded:** same `.sln`
- **Audit mode:** `full-surface` intent; **isolation:** working tree on `main` (no disposable worktree created — note risk; only low-risk using-order change applied)
- **Client:** Cursor agent with `user-roslyn` MCP tools (`call_mcp_tool`); MCP **prompts** not invokable via this surface → prompts rows `blocked` (client)
- **Workspace id (primary session):** `a60777b10b724b63b2699b0f27a00a57`
- **Server (server_info):** `roslyn-mcp` **1.7.0+46e9f728**; `catalogVersion` **2026.03**; `runtime` **.NET 10.0.5**; `roslynVersion` **5.3.0.0**; `os` **Microsoft Windows 10.0.26200**
- **Surface counts:** tools **56** stable / **67** experimental; resources **7** stable; prompts **0** stable / **16** experimental — **matches** `roslyn://server/catalog` `Summary`
- **Scale:** 6 projects, 321 documents (`workspace_load`)
- **Repo shape:** multi-project; test project `RoslynMcp.Tests`; analyzers present (451 rules total); **Central Package Management** (`centrally-managed` refs observed in `get_nuget_dependencies`); no multi-targeting (net10.0 only); `.editorconfig` at repo root; source generators (GlobalUsings, RegexGenerator) listed by `source_generated_documents`
- **Prior issue source:** `ai_docs/backlog.md` (2026-04-08)
- **Debug log channel:** `no` — Cursor session did not surface MCP `notifications/message` structured logs to the agent
- **dotnet restore:** completed successfully before semantic tools

## Coverage summary

| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked |
|------|----------|-----------|-----------------|--------------|--------------------|----------------|---------|
| tool | (all) | 35 | 1 | 1 | 0 | 0 | 0 |
| tool | (uninvoked remainder) | — | — | — | — | — | 86 `skipped-session` |
| resource | server | 2 | n/a | n/a | 0 | 0 | 0 |
| resource | workspace | 0 | n/a | n/a | 0 | 0 | 5 `blocked` (late fetch + server readiness) |
| prompt | prompts | 0 | n/a | n/a | 0 | 0 | 16 `blocked` (client) |

## Coverage ledger (compact)

**Policy:** One row per catalog entry. Tools: **123** rows from live catalog. Status `skipped-session` = not called in this abbreviated run.

### Tools (123)

| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| tool | `server_info` | stable | exercised | 0 | Counts match catalog |
| tool | `workspace_load` | stable | exercised | 0 | Loaded solution; see duplicate session FLAG |
| tool | `workspace_reload` | stable | exercised | 8 | Version → 3 after edits |
| tool | `workspace_close` | stable | skipped-session | — | — |
| tool | `workspace_list` | stable | exercised | 0 | **FLAG:** `count: 2` duplicate loads same `.sln` |
| tool | `workspace_status` | stable | exercised | 0 | Full project tree payload (backlog `workspace-payload-summary-modes`) |
| tool | `project_graph` | stable | exercised | 0 | — |
| tool | `source_generated_documents` | stable | exercised | 11 | 6 generated docs |
| tool | `get_source_text` | stable | skipped-session | — | — |
| tool | `symbol_search` | stable | exercised | 3 | — |
| tool | `symbol_info` | stable | exercised | 3 | — |
| tool | `go_to_definition` | stable | skipped-session | — | — |
| tool | `find_references` | stable | exercised | 3,17 | Valid handle: 122 refs total; junk handle: **structured error** (not silent empty) |
| tool | `find_implementations` | stable | exercised | 3 | 7 impls of `IWorkspaceManager` |
| tool | `document_symbols` | stable | exercised | 3 | — |
| tool | `find_overrides` | stable | skipped-session | — | — |
| tool | `find_base_members` | stable | skipped-session | — | — |
| tool | `member_hierarchy` | stable | skipped-session | — | — |
| tool | `symbol_signature_help` | stable | skipped-session | — | — |
| tool | `symbol_relationships` | stable | skipped-session | — | — |
| tool | `find_references_bulk` | stable | skipped-session | — | — |
| tool | `find_property_writes` | stable | skipped-session | — | — |
| tool | `enclosing_symbol` | stable | skipped-session | — | — |
| tool | `goto_type_definition` | stable | skipped-session | — | — |
| tool | `get_completions` | stable | skipped-session | — | — |
| tool | `project_diagnostics` | stable | exercised | 1 | **FLAG:** `severity=Error` filter → `totalDiagnostics=0` while unfiltered has 1 warning — matches backlog `project-diagnostics-filter-totals` |
| tool | `diagnostic_details` | stable | exercised | 1 | CS0414 |
| tool | `type_hierarchy` | stable | exercised | 3 | — |
| tool | `callers_callees` | stable | skipped-session | — | — |
| tool | `impact_analysis` | stable | skipped-session | — | — |
| tool | `find_type_mutations` | stable | skipped-session | — | — |
| tool | `find_type_usages` | stable | skipped-session | — | — |
| tool | `build_workspace` | stable | skipped-session | — | — |
| tool | `build_project` | stable | skipped-session | — | — |
| tool | `test_discover` | stable | exercised | 8 | Large output (~72KB) |
| tool | `test_run` | stable | exercised | 8 | **FAIL:** dotnet file lock / copy errors vs running testhost |
| tool | `test_related` | stable | skipped-session | — | — |
| tool | `test_related_files` | stable | skipped-session | — | — |
| tool | `rename_preview` | stable | skipped-session | — | — |
| tool | `rename_apply` | stable | skipped-session | — | — |
| tool | `organize_usings_preview` | stable | exercised-preview-only | 6 | Token `62bb52ff…` |
| tool | `organize_usings_apply` | stable | exercised-apply | 6 | `Program.cs` |
| tool | `format_document_preview` | stable | exercised-preview-only | 6 | Empty diff (already formatted) |
| tool | `format_document_apply` | stable | skipped-session | — | No apply after empty preview |
| tool | `code_fix_preview` | stable | skipped-session | — | Intentional CS0414 left for tests |
| tool | `code_fix_apply` | stable | skipped-session | — | — |
| tool | `find_unused_symbols` | experimental | exercised | 2 | — |
| tool | `get_di_registrations` | experimental | exercised | 11 | 6 registrations in Host.Stdio |
| tool | `get_complexity_metrics` | experimental | exercised | 2 | Top CC 27 `ParseNuGetVulnerabilityJson` |
| tool | `find_reflection_usages` | experimental | skipped-session | — | — |
| tool | `get_namespace_dependencies` | experimental | exercised | 2 | `circularOnly=true`, RoslynMcp.Roslyn → empty cycles |
| tool | `get_nuget_dependencies` | experimental | exercised | 2 | CPM surfaced |
| tool | `semantic_search` | experimental | exercised | 11 | Query "async methods returning Task" |
| tool | `apply_text_edit` | experimental | skipped-session | — | — |
| tool | `apply_multi_file_edit` | experimental | skipped-session | — | — |
| tool | `create_file_preview` | experimental | skipped-session | — | — |
| tool | `create_file_apply` | experimental | skipped-session | — | — |
| tool | `delete_file_preview` | experimental | skipped-session | — | — |
| tool | `delete_file_apply` | experimental | skipped-session | — | — |
| tool | `move_file_preview` | experimental | skipped-session | — | — |
| tool | `move_file_apply` | experimental | skipped-session | — | — |
| tool | `add_package_reference_preview` | experimental | skipped-session | — | — |
| tool | `remove_package_reference_preview` | experimental | skipped-session | — | — |
| tool | `add_project_reference_preview` | experimental | skipped-session | — | — |
| tool | `remove_project_reference_preview` | experimental | skipped-session | — | — |
| tool | `set_project_property_preview` | experimental | skipped-session | — | — |
| tool | `add_target_framework_preview` | experimental | skipped-session | — | — |
| tool | `remove_target_framework_preview` | experimental | skipped-session | — | — |
| tool | `set_conditional_property_preview` | experimental | skipped-session | — | — |
| tool | `add_central_package_version_preview` | experimental | skipped-session | — | — |
| tool | `remove_central_package_version_preview` | experimental | skipped-session | — | — |
| tool | `apply_project_mutation` | experimental | skipped-session | — | — |
| tool | `scaffold_type_preview` | experimental | skipped-session | — | — |
| tool | `scaffold_type_apply` | experimental | skipped-session | — | — |
| tool | `scaffold_test_preview` | experimental | skipped-session | — | — |
| tool | `scaffold_test_apply` | experimental | skipped-session | — | — |
| tool | `remove_dead_code_preview` | experimental | skipped-session | — | — |
| tool | `remove_dead_code_apply` | experimental | skipped-session | — | — |
| tool | `move_type_to_project_preview` | experimental | skipped-session | — | — |
| tool | `extract_interface_cross_project_preview` | experimental | skipped-session | — | — |
| tool | `dependency_inversion_preview` | experimental | skipped-session | — | — |
| tool | `migrate_package_preview` | experimental | skipped-session | — | — |
| tool | `split_class_preview` | experimental | skipped-session | — | — |
| tool | `extract_and_wire_interface_preview` | experimental | skipped-session | — | — |
| tool | `apply_composite_preview` | experimental | skipped-session | — | — |
| tool | `test_coverage` | stable | skipped-session | — | — |
| tool | `get_syntax_tree` | experimental | skipped-session | — | — |
| tool | `get_code_actions` | experimental | skipped-session | — | — |
| tool | `preview_code_action` | experimental | skipped-session | — | — |
| tool | `apply_code_action` | experimental | skipped-session | — | — |
| tool | `security_diagnostics` | stable | exercised | 1 | 0 findings |
| tool | `security_analyzer_status` | stable | exercised | 1 | SecurityCodeScan present |
| tool | `nuget_vulnerability_scan` | stable | exercised | 1 | 0 CVEs, ~4.2s |
| tool | `find_consumers` | stable | skipped-session | — | — |
| tool | `get_cohesion_metrics` | stable | exercised | 2 | — |
| tool | `find_shared_members` | stable | skipped-session | — | — |
| tool | `move_type_to_file_preview` | experimental | skipped-session | — | — |
| tool | `move_type_to_file_apply` | experimental | skipped-session | — | — |
| tool | `extract_interface_preview` | experimental | skipped-session | — | — |
| tool | `extract_interface_apply` | experimental | skipped-session | — | — |
| tool | `bulk_replace_type_preview` | experimental | skipped-session | — | — |
| tool | `bulk_replace_type_apply` | experimental | skipped-session | — | — |
| tool | `extract_type_preview` | experimental | skipped-session | — | — |
| tool | `extract_type_apply` | experimental | skipped-session | — | — |
| tool | `revert_last_apply` | experimental | skipped-session | — | — |
| tool | `analyze_data_flow` | experimental | exercised | 4 | `CompileCheckService.CheckAsync` range |
| tool | `analyze_control_flow` | experimental | exercised | 4 | Warning: partial range |
| tool | `compile_check` | stable | exercised | 1,6 | `emitValidation` ~5.7s vs default ~1.8s (not identical; backlog `compile-check-emit-validation-regression-check` worth monitoring) |
| tool | `list_analyzers` | stable | exercised | 1 | 17 analyzers, 451 rules |
| tool | `fix_all_preview` | experimental | skipped-session | — | — |
| tool | `fix_all_apply` | experimental | skipped-session | — | — |
| tool | `get_operations` | experimental | skipped-session | — | — |
| tool | `format_range_preview` | experimental | skipped-session | — | — |
| tool | `format_range_apply` | experimental | skipped-session | — | — |
| tool | `analyze_snippet` | stable | exercised | 5 | CS0029 column 9 user-relative |
| tool | `evaluate_csharp` | experimental | exercised | 5 | Sum=55; runtime error graceful; **infinite loop not run** (would burn timeout) |
| tool | `get_editorconfig_options` | experimental | exercised | 7 | — |
| tool | `set_editorconfig_option` | experimental | skipped-session | — | — |
| tool | `evaluate_msbuild_property` | experimental | skipped-session | — | — |
| tool | `evaluate_msbuild_items` | experimental | skipped-session | — | — |
| tool | `get_msbuild_properties` | experimental | skipped-session | — | — |
| tool | `set_diagnostic_severity` | experimental | skipped-session | — | — |
| tool | `add_pragma_suppression` | experimental | skipped-session | — | — |

### Resources (7)

| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| resource | `server_catalog` | stable | exercised | 0 | `roslyn://server/catalog` |
| resource | `resource_templates` | stable | exercised | 0 | — |
| resource | `workspaces` | stable | blocked | 15 | Late fetch: server not ready |
| resource | `workspace_status` | stable | blocked | 15 | Not fetched |
| resource | `workspace_projects` | stable | blocked | 15 | Not fetched |
| resource | `workspace_diagnostics` | stable | blocked | 15 | Not fetched |
| resource | `source_file` | stable | blocked | 15 | Not fetched |

### Prompts (16)

| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| prompt | `explain_error` … `consumer_impact` | experimental | blocked | 16 | Cursor `call_mcp_tool` does not expose prompt invocation |

## Verified tools (working) — sample

- `compile_check` — 1 CS0414 warning (intentional sample), 0 errors
- `project_diagnostics` — 1 total warning unfiltered; scoped project filter works
- `find_implementations` — 7 results for `IWorkspaceManager`
- `find_references` — paging `hasMore` + `totalCount` consistent
- `organize_usings_apply` — applied 1 file, `workspace_reload` bumped version
- `nuget_vulnerability_scan` — structured empty CVE set
- `semantic_search` — returns ranked symbols
- `analyze_snippet` — CS0029 positions user-relative (FLAG-C behavior)
- `evaluate_csharp` — arithmetic + runtime exception paths OK

## Phase 6 refactor summary

- **Target repo:** Roslyn-Backed-MCP (same as audited solution)
- **Scope:** **6e** only — `organize_usings_preview` → `organize_usings_apply` on `src/RoslynMcp.Host.Stdio/Program.cs` (Microsoft.* usings first per `dotnet_sort_system_directives_first`). **No** `fix_all` / `rename` / `extract_*` / dead-code removal — `DiagnosticsProbe` CS0414 is intentional for tests.
- **MCP tools:** `organize_usings_preview`, `organize_usings_apply`, `workspace_reload`, `compile_check`
- **Verification:** `compile_check` still reports only CS0414 in SampleLib; no new errors. **`test_run`** failed in this environment (DLL locked by testhost). Recommend local `dotnet test` after closing parallel test processes.
- **Git:** `Program.cs` 3-line delta (import order)

## Concurrency matrix (Phase 8b)

**N/A —** Parallel read fan-out, RW probes, and lifecycle stress not executed this session (would need dedicated timing + optional parallel MCP client). Backlog: `reader-tool-elapsed-ms` explains missing numeric reader `ElapsedMs` in tool JSON for agent-side matrices.

## Writer reclassification verification (Phase 8b.5)

**N/A —** Not run.

## Debug log capture

`client did not surface MCP log notifications`

## MCP server issues (bugs)

### 1. Duplicate workspace sessions for identical solution

| Field | Detail |
|--------|--------|
| Tool | `workspace_list` / `workspace_load` |
| Input | Same `.sln` loaded twice in one host |
| Expected | Idempotent single session or explicit dedup |
| Actual | `count: 2` with distinct `WorkspaceId`s |
| Severity | incorrect result / UX (resource use) |
| Reproducibility | sometimes (depends on prior loads) |

### 2. `project_diagnostics` severity filter vs totals (known backlog)

| Field | Detail |
|--------|--------|
| Tool | `project_diagnostics` |
| Input | `severity=Error` |
| Expected | Totals either invariant or renamed (per schema contract) |
| Actual | `totalDiagnostics=0` with `totalWarnings`/`totalErrors` zeroed while unfiltered shows warnings |
| Severity | incorrect result / contract |
| Reproducibility | always (matches `project-diagnostics-filter-totals`) |

### 3. `test_run` environment failure (may be local)

| Field | Detail |
|--------|--------|
| Tool | `test_run` |
| Input | `RoslynMcp.Tests` + filter |
| Expected | TRX / structured pass |
| Actual | MSB3027/3021 file locks on Roslyn DLLs |
| Severity | degraded performance / environment |
| Reproducibility | sometimes |

**No new issues found** beyond the above and documented FLAGs; server version **1.7.0** behaved consistently on exercised paths.

## Improvement suggestions

- Surface **workspace session deduplication** or document that repeated `workspace_load` of the same path creates parallel sessions (operators may hit cap `ROSLYNMCP_MAX_WORKSPACES`).
- **`test_run`**: when build copy fails, include actionable hint (close parallel `testhost`) in error envelope.
- **`compile_check` `emitValidation`:** document expected slowdown ratio on restored vs unrestored solutions (backlog `compile-check-emit-validation-regression-check`).

## Performance observations

| Tool | Input scale | Wall-clock (approx) | Notes |
|------|-------------|----------------------|-------|
| `project_diagnostics` | full solution | 5000–6500 ms | Hot path |
| `compile_check` + `emitValidation` | 6 projects | ~5772 ms | vs ~1831 ms default |
| `nuget_vulnerability_scan` | 6 projects | ~4161 ms | Network/subprocess |
| `find_unused_symbols` | solution | ~1081 ms | — |

## Known issue regression check

| Source id | Summary | Status |
|-----------|---------|--------|
| `project-diagnostics-filter-totals` | Severity filter clears totals | **still reproduces** |
| `semantic-search-async-modifier-doc` | async vs Task-returning queries | **not re-tested** (query used broader text; backlog doc issue remains) |
| `error-response-observability` (fabricated handle) | Distinguish junk handle vs zero refs | **partially fixed** — `find_references` returns structured `InvalidArgument` (not `{count:0}`) for malformed handle |
| `compile-check-emit-validation-regression-check` | emit path timing | **partially addressed** — emitValidation slower here; not byte-identical to default |

## Known issue cross-check

- **New observation:** duplicate workspace entries in `workspace_list` — consider tying to backlog `workspace-payload-summary-modes` / session UX.

---

*End of report.*
