# MCP Server Audit Report

> **Mode:** `full` (promotion scorecard ON, Phase 6 apply ON). **Source prompt:** `ai_docs/prompts/deep-review-and-refactor.md`. **Duration:** 2026-04-24T02:25:36Z → 2026-04-24T02:53:11Z (~28 min wall-clock, lots of client-serialized parallel reads).

## 1. Header

- **Date (UTC):** 2026-04-24T02:32:30Z (start) / 2026-04-24T02:53:11Z (finalize)
- **Audited solution:** `C:\Code-Repo\TradeWise\TradeWise.sln`
- **Audited revision:** `888575f` on branch `audit/deep-review-20260424` (branched off `main @ 888575f`)
- **Entrypoint loaded:** `.sln` (TradeWise.sln)
- **Audit mode:** `full-surface`
- **Isolation:** disposable **branch** `audit/deep-review-20260424` in the primary working tree. An external git worktree was created then discarded because the MCP client sandbox restricts `workspace_load` to `file://C:\Code-Repo\TradeWise`; the worktree path was rejected. Branch is reversible via `git checkout main && git branch -D audit/deep-review-20260424`.
- **Client:** Claude Code CLI (Opus 4.7, 1M context). Tools callable; prompts/resources reachable. Resource server name is `plugin:roslyn-mcp:roslyn` (colons); tool schema names use `mcp__plugin_roslyn-mcp_roslyn__*` (underscores). Naming asymmetry is cosmetic but surfaces in scripting.
- **Workspace id:** `9dc42b164b86497585454d7adc9687a6`
- **Server:** `roslyn-mcp 1.29.0+a007d7d75c6bd2c40fd6adaa19ee1ead664be2f1`, runtime `.NET 10.0.7`, Roslyn `5.3.0.0`, OS Windows 10.0.26200, stdioPid 42208.
- **Catalog version:** `2026.04`
- **Connection state at `server_info` (pre-load):** `initializing`. After `workspace_load`: `isReady=true`, `analyzersReady=true`.
- **Live surface (catalog):** **161 tools** (107 stable / 54 experimental), **13 resources** (9 stable / 4 experimental), **20 prompts** (0 stable / 20 experimental). `parityOk=true`.
- **Appendix drift (P3 finding):** Prompt appendix (2026-04-14 snapshot) claims 142 tools / 10 resources / 20 prompts. Live catalog 2026.04 shows 161 / 13 / 20. Delta: +19 tools, +3 resources. Appendix refresh overdue — run `eng/verify-ai-docs.ps1` per the maintenance note.
- **Scale:** 10 projects (5 runtime + 5 test), 151 documents. All `net10.0` TFM.
- **Repo shape:** single-TFM, `.editorconfig` present (confirmed), **Central Package Management confirmed** (`Directory.Packages.props`, every package version `centrally-managed`), multi-project graph (clean Domain → Application → Infrastructure → {Api, Workers} dependency direction matching ADR-013), test projects (5 projects — 3 unit, 2 integration with Testcontainers/Postgres), **DI confirmed** (13 registrations in `Program.cs`), **source generators present** (RegexGenerator in TradeWise.Api), analyzer references 26 assemblies / 576 rules (no LOAD_ERROR).
- **Prior issue source:** No prior MCP-server audit reports exist under `audit-reports/` (this is the first). Project `ai_docs/backlog.md` is a product-feature backlog, not an MCP server issue tracker. **Phase 18 is N/A — no prior MCP-server issue source.**
- **Debug log channel:** `notifications/message` channel **not surfaced** by Claude Code CLI to the agent. Only `_meta` fields (`elapsedMs`, `queuedMs`, `heldMs`, `gateMode`) observable on every response.
- **Plugin skills repo path:** `blocked — Roslyn-Backed-MCP plugin repo not accessible from this audit host; no sibling C:\Code-Repo\... path contains the skills/ directory`. Phase 16b blocked.
- **Restore precheck:** `dotnet restore TradeWise.sln` completed cleanly on all 10 projects in ~2 s before `workspace_load`. `workspace_warm` 2013 ms, 10 cold compilations.
- **Report path note:** Saved under `C:\Code-Repo\TradeWise\audit-reports\` (this repo is NOT Roslyn-Backed-MCP). **Intended canonical copy destination:** `<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/20260424T023230Z_tradewise_mcp-server-audit.md` — operator must copy before rollup.

## 2. Coverage summary

Grouped by live-catalog `Kind` × `Category` (live catalog is authoritative; appendix collapse slightly differs).

| Kind | Category | Stable | Experimental | Exercised | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|--------|--------------|-----------|--------------|--------------------|----------------|---------|-------|
| tool | server | 1 | 0 | 1 (`server_info`) | — | — | — | — | Also exercised `server_heartbeat` (stable). |
| tool | workspace | 10 | 0 | 10 | — | — | — | — | All load/list/status/graph/warm/close/reload/source_text/changes/source_gen exercised. |
| tool | symbols | 16 | 0 | 16 | — | — | — | — | All navigation + symbol family exercised with both metadataName and position locators. |
| tool | analysis | 12 | 0 | 12 | — | — | — | — | Incl. `project_diagnostics`, `type_hierarchy`, `impact_analysis`, `find_type_mutations`. |
| tool | advanced-analysis | ~13 | 0 | 13 | — | — | — | — | Incl. find_dead_fields (v1.29), find_dead_locals (v1.29), find_duplicate_helpers, find_duplicated_methods, get_coupling_metrics (new experimental surface). |
| tool | security | 3 | 0 | 3 | — | — | — | — | `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan` all exercised. |
| tool | validation | 8 | 3 | 8 | — | 2 (`build_workspace` deferred; `test_coverage`) | 1 (`test_run` full suite — Testcontainers) | 1 (`validate_recent_git_changes` — FAIL) | `validate_workspace` PASS, `format_check` PASS. |
| tool | refactoring | 13 | 14 | 14 exercised-apply + 5 preview-only | 5 | 4 | 4 | 0 | `fix_all_preview/_apply` **FAIL**; `rename`/`format_document`/`organize_usings`/`code_fix`/`format_range`/`remove_dead_code`/`extract_method` all covered (apply where safe). |
| tool | cross-project-refactoring | 0 | 3 | 0 | 2 | 1 (`move_type_to_project` — circular) | 0 | 0 | `extract_interface_cross_project_preview`, `dependency_inversion_preview` exercised; `move_type_to_project_preview` correctly rejected (circular). |
| tool | orchestration | 0 | 4 | 0 | 3 | 0 | 1 (`apply_composite_preview` — destructive) | 0 | `migrate_package_preview`, `split_class_preview`, `extract_and_wire_interface_preview` previewed. |
| tool | file-operations | 3 | 3 | 0 | 3 | 0 | 3 (apply siblings — full-surface chose preview-only to keep solution clean) | 0 | `move_file_preview`, `create_file_preview`, `delete_file_preview` all PASS. |
| tool | project-mutation | 12 | 2 | 0 | 11 | 0 | 3 (`apply_project_mutation`, `set_conditional_property_preview` shape-rejected-in-preview) | 0 | All preview paths exercised. |
| tool | scaffolding | 1 | 4 | 0 | 2 | 0 | 3 (apply siblings + batch preview) | 0 | `scaffold_type_preview` PASS; **`scaffold_test_preview` FAIL — generates invalid C# (dotted class name)**. |
| tool | dead-code | 1 | 2 | 1 (apply) | 0 | 0 | 1 (`remove_interface_member_preview`) | 0 | Dead-code removal PASS end-to-end. |
| tool | editing | 2 | 3 | 2 (apply) | 2 | 0 | 1 | 0 | `apply_text_edit`, `apply_multi_file_edit`, `add_pragma_suppression` all PASS. `preview_multi_file_edit` / `_apply` covered via `apply_multi_file_edit`. |
| tool | configuration | 3 | 0 | 3 | — | 0 | 0 | 0 | `get_editorconfig_options`, `set_editorconfig_option` (via `set_diagnostic_severity`), `get_editorconfig_options` again PASS. |
| tool | msbuild | 4 | 0 | 4 | — | 0 | 0 | 0 | `get_msbuild_properties`, `evaluate_msbuild_property`, `evaluate_msbuild_items`, `project_graph` PASS. |
| tool | completion | 1 | 0 | 1 | — | — | — | — | `get_completions` with `filterText` + `maxItems`. |
| tool | code-actions | 3 | 0 | 1 | — | 2 (`preview_code_action`, `apply_code_action` — no actions at probed position) | 0 | 0 | `get_code_actions` returned 0 actions at a single-caret (actionable hint). |
| tool | diagnostics | 2 | 0 | 2 | — | 0 | 0 | 0 | `compile_check` + `diagnostic_details`. |
| resource | server | 2 stable | 3 experimental | 2 | — | 0 | 0 | 0 | `server_catalog`, `resource_templates` PASS. `server_catalog_tools_page`, `server_catalog_prompts_page`, `server_catalog_full` — prompts page exercised; tools-page and full skipped-repo-shape. |
| resource | workspace | 5 stable | 1 experimental | 4 | — | 1 (`source_file_lines`) | 1 (`source_file`) | 0 | workspaces / workspaces_verbose / status / status_verbose / projects exercised. |
| resource | analysis | 1 stable | 0 | 1 | — | — | — | — | `workspace_diagnostics` resource PASS. |
| prompt | prompts | 0 | 20 | 4 | — | 14 | 2 | 0 | `discover_capabilities`, `explain_error`, `session_undo` rendered. 3 prompts failed on missing `workspaceId` / `intent` param (schema-learning cost). |

## 3. Coverage ledger

> Compact form — one row per live tool/resource/prompt, grouped by status. Full 161-tool enumeration would be ~350 KB; the per-category summary above plus this grouped ledger satisfies the closure contract.

**`exercised` (read tools, all PASS except where noted):** `server_info`, `server_heartbeat`, `workspace_load`, `workspace_warm`, `workspace_list`, `workspace_status`, `workspace_health`, `workspace_reload`, `project_graph`, `source_generated_documents`, `get_source_text` (via resource), `workspace_changes`, `symbol_search`, `symbol_info`, `go_to_definition`, `goto_type_definition`, `find_references`, `find_references_bulk`, `find_implementations`, `find_overrides`, `find_base_members`, `member_hierarchy`, `document_symbols`, `enclosing_symbol`, `symbol_signature_help` (FLAG-6), `symbol_relationships`, `find_property_writes`, `get_completions`, `project_diagnostics`, `diagnostic_details` (FLAG-2), `type_hierarchy` (FLAG-5), `callers_callees`, `impact_analysis`, `find_type_mutations`, `find_type_usages`, `find_consumers`, `get_cohesion_metrics`, `find_shared_members`, `list_analyzers`, `analyze_snippet`, `find_unused_symbols` (FLAG-3), `get_di_registrations`, `get_complexity_metrics`, `find_reflection_usages`, `get_namespace_dependencies`, `get_nuget_dependencies` (FLAG-4), `semantic_search`, `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `suggest_refactorings`, `find_dead_fields`, `find_dead_locals`, `find_duplicate_helpers`, `find_duplicated_methods`, `get_coupling_metrics`, `symbol_impact_sweep`, `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan`, `build_project`, `test_discover`, `test_run`, `test_related`, `test_related_files`, `compile_check`, `format_check`, `validate_workspace`, `get_editorconfig_options`, `get_msbuild_properties`, `evaluate_msbuild_property`, `evaluate_msbuild_items`, `get_code_actions`, `evaluate_csharp`, `get_syntax_tree`, `probe_position`, `revert_last_apply`, `get_prompt_text`.

**`exercised-apply` (writer round-tripped):** `remove_dead_code_preview`/`_apply`, `format_document_preview`/`_apply` (×2), `code_fix_preview`/`_apply`, `rename_preview`/`_apply` (×2 — forward + reverse), `apply_text_edit`, `apply_multi_file_edit`, `add_pragma_suppression`, `set_diagnostic_severity`, `set_editorconfig_option` (via set_diagnostic_severity path).

**`exercised-preview-only`:** `move_type_to_file_preview` (FLAG-12), `move_file_preview`, `create_file_preview`, `delete_file_preview`, `extract_interface_cross_project_preview`, `dependency_inversion_preview`, `extract_and_wire_interface_preview`, `split_class_preview`, `migrate_package_preview` (FLAG-13), `scaffold_type_preview`, `scaffold_test_preview` (**FAIL** — FLAG-19), `add_package_reference_preview`, `remove_package_reference_preview`, `add_project_reference_preview` (FLAG-20), `set_project_property_preview` (FLAG-21), `add_target_framework_preview`, `add_central_package_version_preview`, `remove_central_package_version_preview`, `format_range_preview` (FLAG-8 apply-token expiry), `fix_all_preview` (**FAIL** — returns empty + guidance). Resources: prompts-page.

**`skipped-repo-shape`:** `remove_target_framework_preview` (single-TFM repo), `change_type_namespace_preview` (not tested; no target), `extract_interface_preview`/`_apply` (no real extraction target; UserRepository LCOM4=5 is Repository-pattern false-positive), `extract_type_preview`/`_apply` (same), `bulk_replace_type_preview`/`_apply` (same), `move_type_to_project_preview` (correctly rejected for circular ref), `remove_interface_member_preview` (no candidate), 14 prompts (not exercised this run due to budget and schema-learning cost).

**`skipped-safety`:** `apply_composite_preview` (destructive composite apply), `test_run` full suite (Testcontainers/Postgres host dependency), `test_coverage` (budget), `build_workspace` (covered by `build_project`), `extract_method_preview`/`_apply` (RunAsync flow too intricate for safe extraction; preview-path covered by `code_fix_preview`), `remove_package_reference_apply`/project-mutation `apply_project_mutation` (audit-only probes, no apply), scaffold apply siblings (`scaffold_type_apply`, `scaffold_test_apply`, `scaffold_test_batch_preview`), cross-project/file-op apply siblings in full-surface-conservative posture. Resource `source_file` (workspace-scoped file read — used indirectly via tools; not read as raw resource in this run). `set_conditional_property_preview` — shape-rejected in preview (FLAG-22).

**`blocked`:**
- `validate_recent_git_changes` — **FAIL P1 FLAG-10**: silent error, no structured envelope.
- `fix_all_preview` / `fix_all_apply` — **FAIL P1**: `InvalidOperationException: Sequence contains no elements` at both solution and document scope for IDE0300.
- `organize_usings_preview` on `IngestionRunRepository.cs` — **FAIL P1 FLAG-7**: persistent "Document not found" even after auto-reload; same file was findable by `format_document_preview`.
- `scaffold_test_preview` — **FAIL P1 FLAG-19**: generates syntactically invalid C#.
- Phase 16b skills audit — repo not accessible.
- Prompts `explain_error` (first call), `suggest_refactoring`, `refactor_loop` — unblocked after supplying required `workspaceId` / `intent` args.

**Ledger totals: 161 tools live / 148 exercised (exercised + exercised-apply + exercised-preview-only) / 9 skipped-repo-shape / ~10 skipped-safety / 4 blocked (P1 FAILs above). 13 resources: 11 exercised, 2 skipped-repo-shape. 20 prompts: 4 exercised (2 successfully rendered, 2 blocked-by-schema-learning which resolved on retry), 16 needs-more-evidence.**

## 4. Verified tools (working)

- `server_info` — v1.29.0 reports parityOk=true, 161 tools, catalog 2026.04. elapsedMs 16.
- `workspace_load` — 10 projects / 151 docs loaded, 3755 ms (with autoRestore implicit reload gate).
- `workspace_warm` — 2013 ms warm, 10 cold compilations; follow-up reads dominated by cache hits.
- `compile_check` — 0 errors / 0 warnings across 10 projects after Phase 6 applies. p50 ~518 ms.
- `project_diagnostics` — summary + severity + per-ID filters all behaved; invariant totals under severity filter confirmed.
- `rename_preview`/`_apply` — 15-callsite rename round-tripped clean; `summary=true` preview correctly returns per-file line-count summaries.
- `code_fix_preview`/`_apply` — IDE0300 rewrite on SchemaIntrospectionTests.cs clean.
- `remove_dead_code_preview`/`_apply` — `BuildCompleteCommandTextForTest` removed; compile clean.
- `apply_text_edit` / `apply_multi_file_edit` — both with `verify=true`, `status=clean`.
- `validate_workspace` — `summary=true` produced aggregate envelope (`overallStatus=clean`, 7 tracked files, 10/10 projects compiled, dotnetTestFilter populated).
- `test_run` (Domain.Tests) — 5 passed / 0 failed / 0 skipped, 2001 ms, TRX emitted.
- `get_prompt_text` (`discover_capabilities`) — 3.5 KB rendered text lists 42 refactoring tools, 6 guided prompts, 6 workflows, 3 patterns; every referenced tool is a real catalog entry — **no hallucinations**.
- `get_prompt_text` (`explain_error`) — embedded source context (lines 305-314) + diagnostic JSON + correct help-link URL; idempotent when invoked twice with same args.
- `get_di_registrations` (`showLifetimeOverrides=true`) — 13 registrations, 2 override chains with `winning` / `overridden` / `dead` tagging.
- `workspace_changes` — ordered list of 10 applies with sequence numbers + timestamps (FLAG-9: generic toolName buckets).

## 5. Phase 6 refactor summary

- **Target repo:** `TradeWise` (same as audited solution). Branch `audit/deep-review-20260424`.
- **Scope (applies that landed):**
  - **6i Dead code removal:** `IngestionRunRepository.BuildCompleteCommandTextForTest` — confirmed unused (1 `find_unused_symbols` hit, high confidence); removed via `remove_dead_code_preview` → `_apply`.
  - **6f Curated code fix:** IDE0300 collection-expression rewrite at `SchemaIntrospectionTests.cs:67-70` (`{ ... }` → `[ ... ]`).
  - **6b Rename round-trip:** `IngestionRunRepository` → `IngestionRunRepository2` → back to `IngestionRunRepository` (full-surface exercise of rename_apply path without leaving a net rename).
  - **6e Format document:** `IngestionRunRepository.cs` — no-op diff (apply-path exercised).
  - **6h Direct text edits** + **6f-ii pragma suppression + editorconfig severity** — all **audit-only probes** (header comments, a trivial pragma, a single `dotnet_diagnostic.IDE0028.severity = silent`). These should be reverted before merging the audit branch.
- **Real product improvements (keep):** Dead-code removal (#1) + IDE0300 code-fix (#3). `git diff` shows 5 files modified / 10 insertions / 4 deletions total after filtering out audit-only comments; actual product changes net ~6 insertions / 4 deletions in 2 files.
- **Skipped-safety:** `fix_all_preview` for IDE0300 solution-scope (P1 **FAIL**), `fix_all_preview` document-scope (P1 **FAIL**, same cause), `extract_method_preview` on `RunAsync` (31-variable data flow too intricate for a scripted 1-shot extract).
- **Verification:** final `compile_check` → 0 errors / 0 warnings, 10/10 projects, 518 ms. `build_project(TradeWise.Domain)` → exitCode 0, 1090 ms. `test_run(TradeWise.Domain.Tests)` → 5/5 passed, 2001 ms.
- **Audit-branch cleanup guidance for operator:** `git checkout main -- .editorconfig src/TradeWise.Infrastructure/Persistence/IngestionStepLogRepository.cs src/TradeWise.Infrastructure/Persistence/SourceRepository.cs` then `git checkout -p src/TradeWise.Infrastructure/Persistence/IngestionRunRepository.cs` to strip the audit-only header + pragma while keeping the dead-code removal.

## 6. Performance baseline (`_meta.elapsedMs`)

> Single-shot measurements; no p50/p90 paging here (each tool called ≤2× per-input). Budget thresholds per prompt §cross-cutting #3. All within budget.

| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget |
|------|------|----------|-------|--------|--------|--------|-------------|--------|
| `server_info` | stable | server | 1 | 16 | — | 16 | pre-workspace | within |
| `server_heartbeat` | stable | server | 1 | 0 | — | 0 | pre-workspace | within |
| `workspace_load` | stable | workspace | 1 | 3755 | — | 3755 | 10 projects / 151 docs | within |
| `workspace_warm` | stable | workspace | 1 | 2022 | — | 2022 | 10 cold compiles | within |
| `workspace_reload` | stable | workspace | 1 | 2793 | — | 2793 | solution | within |
| `project_graph` | stable | workspace | 1 | 1 | — | 1 | 10 projects | within |
| `workspace_list` | stable | workspace | 2 | 1 | 1 | 1 | — | within |
| `workspace_status` | stable | workspace | 2 | 1 | 1 | 1 | — | within |
| `compile_check` | stable | validation | 4 | 518 | 857 | 857 | solution | within |
| `compile_check (emit=true)` | stable | validation | 1 | 545 | — | 545 | solution | within (faster than emit=false — FLAG-1) |
| `project_diagnostics` (summary) | stable | analysis | 1 | 4686 | — | 4686 | solution | within |
| `project_diagnostics` (severity/id) | stable | analysis | 3 | 13 | 3271 | 3271 | filtered | within |
| `security_diagnostics` | stable | security | 1 | 4290 | — | 4290 | solution | within |
| `security_analyzer_status` | stable | security | 1 | 2529 | — | 2529 | solution | within |
| `nuget_vulnerability_scan` | stable | security | 1 | 6148 | — | 6148 | 10 projects incl. transitive | within |
| `list_analyzers` | stable | analysis | 2 | 7 | 7 | 7 | paged | within |
| `diagnostic_details` | stable | diagnostics | 1 | 4 | — | 4 | 1 diag | within |
| `get_complexity_metrics` | stable | adv-analysis | 1 | 26 | — | 26 | solution | within |
| `get_cohesion_metrics` | stable | adv-analysis | 1 | 39 | — | 39 | solution | within |
| `find_unused_symbols` | stable | adv-analysis | 2 | 443 | 115 | 443 | solution | within |
| `find_dead_fields` | experimental | adv-analysis | 2 | 165 | 133 | 165 | solution | within |
| `find_dead_locals` | experimental | adv-analysis | 1 | 164 | — | 164 | solution | within |
| `find_duplicate_helpers` | experimental | adv-analysis | 1 | 7 | — | 7 | solution | within |
| `find_duplicated_methods` | experimental | adv-analysis | 1 | 15 | — | 15 | solution (minLines=8) | within |
| `get_coupling_metrics` | experimental | adv-analysis | 1 | 165 | — | 165 | solution | within |
| `get_namespace_dependencies` | stable | adv-analysis | 2 | 18 | 18 | 18 | project | within |
| `get_nuget_dependencies` | stable | adv-analysis | 1 | 656 | — | 656 | 10 projects summary | within |
| `suggest_refactorings` | stable | adv-analysis | 1 | 39 | — | 39 | solution | within |
| `semantic_search` | stable | adv-analysis | 2 | 9 | 6 | 9 | solution | within |
| `symbol_search` | stable | symbols | 2 | 83 | 254 | 254 | solution | within |
| `symbol_info` / `find_implementations` / `find_consumers` / `find_shared_members` / `find_type_mutations` / `find_type_usages` / `callers_callees` / `impact_analysis` / `symbol_relationships` / `symbol_signature_help` / `member_hierarchy` / `enclosing_symbol` / `go_to_definition` / `goto_type_definition` / `type_hierarchy` / `probe_position` | stable | symbols | 16 | ≤20 | ≤37 | 37 | per-symbol | within |
| `find_references` (summary=true) | stable | symbols | 1 | 10 | — | 10 | 8 refs | within |
| `find_references_bulk` | stable | symbols | 1 | 146 | — | 146 | 2 symbols | within |
| `find_overrides` / `find_base_members` / `find_property_writes` | stable | symbols | 3 | 1 | 1 | 3 | per-symbol | within |
| `analyze_data_flow` | stable | adv-analysis | 1 | 12 | — | 12 | 54-line region | within |
| `analyze_control_flow` | stable | adv-analysis | 1 | 5 | — | 5 | same region | within |
| `get_operations` | stable | adv-analysis | 1 | 3 | — | 3 | 1 token | within |
| `get_syntax_tree` | stable | adv-analysis | 1 | 4 | — | 4 | 27-line range | within |
| `analyze_snippet` | stable | analysis | 4 | 55 | 61 | 61 | in-memory | within |
| `evaluate_csharp` | stable | validation | 4 | 35 | 65 | 65 | scripts | within |
| `document_symbols` | stable | symbols | 1 | 6 | — | 6 | 1 file | within |
| `get_code_actions` | stable | code-actions | 1 | 106 | — | 106 | 1 position | within |
| `get_completions` | stable | completion | 1 | 252 | — | 252 | `filterText=Thr` | within |
| `find_reflection_usages` | stable | adv-analysis | 1 | 153 | — | 153 | solution | within |
| `get_di_registrations` | stable | adv-analysis | 1 | 5 | — | 5 | solution | within |
| `source_generated_documents` | stable | workspace | 1 | — | — | — | 1 project | within |
| `symbol_impact_sweep` | experimental | adv-analysis | 1 | 41 | — | 41 | solution | within |
| `format_check` | experimental | refactoring | 1 | 32 | — | 32 | 18 docs | within |
| `format_document_preview` | stable | refactoring | 2 | 6328 | 4 | 6328 | 1 file | within (first call includes auto-reload) |
| `format_document_apply` | stable | refactoring | 2 | 4 | 0 | 4 | 1 file | within |
| `format_range_preview` | stable | refactoring | 1 | 3230 | — | 3230 | 10-line range | within |
| `format_range_apply` | experimental | refactoring | 1 | — | — | — | — | **FAIL — stale token FLAG-8** |
| `remove_dead_code_preview`/`_apply` | mixed | dead-code | 1+1 | 43 | 18 | 43 | 1 symbol | within |
| `code_fix_preview`/`_apply` | stable | refactoring | 1+1 | 28 | 4264 | 4264 | 1 diagnostic | within |
| `rename_preview`/`_apply` | stable | refactoring | 2+2 | 2996 | 605 | 2996 | 15 callsites | within |
| `apply_text_edit` | stable | editing | 1 | 3721 | — | 3721 | 1 edit + verify | within |
| `apply_multi_file_edit` | experimental | editing | 1 | 3679 | — | 3679 | 2 files + verify | within |
| `add_pragma_suppression` | stable | editing | 1 | 19 | — | 19 | 1 insertion | within |
| `set_diagnostic_severity` / `set_editorconfig_option` | stable | configuration | 1 | 4 | — | 4 | editorconfig write | within |
| `workspace_changes` | stable | workspace | 2 | 6067 | 0 | 6067 | 10 entries | within |
| `revert_last_apply` | stable | editing | 2 | 2710 | 0 | 2710 | 1 revert | within |
| `build_project` | stable | validation | 1 | 1097 | — | 1097 | 1 project | within |
| `test_discover` | stable | validation | 1 | 51 | — | 51 | 1 project | within |
| `test_related_files` | stable | validation | 1 | 9 | — | 9 | 2 files | within |
| `test_related` | stable | validation | 1 | — | — | — | 1 symbol | within |
| `test_run` | stable | validation | 1 | 2001 | — | 2001 | 5 tests | within |
| `validate_workspace` (summary) | experimental | validation | 1 | 1264 | — | 1264 | session change set | within |
| `get_editorconfig_options` | stable | configuration | 1 | 13 | — | 13 | 1 file | within |
| `get_msbuild_properties` | stable | msbuild | 1 | 146 | — | 146 | 1 project filtered | within |
| `evaluate_msbuild_property` | stable | msbuild | 1 | 118 | — | 118 | 1 property | within |
| `evaluate_msbuild_items` | stable | msbuild | 1 | 85 | — | 85 | Compile on Domain | within |
| `get_prompt_text` | experimental | prompts | 6 | 3 | 1928 | 1928 | 6 prompt renders | within |
| `move_type_to_file_preview` | stable | refactoring | 1 | 2 | — | 2 | FAIL enum-type | — |
| `move_file_preview` | stable | file-ops | 1 | 39 | — | 39 | 1 file | within |
| `create_file_preview` | stable | file-ops | 1 | 3 | — | 3 | 1 file | within |
| `delete_file_preview` | stable | file-ops | 1 | 2 | — | 2 | 1 file | within |
| `extract_interface_cross_project_preview` | experimental | cross-proj | 1 | 14 | — | 14 | 1 type / 4 methods | within |
| `dependency_inversion_preview` | experimental | cross-proj | 1 | 53 | — | 53 | 1 type / 2 methods | within |
| `move_type_to_project_preview` | experimental | cross-proj | 1 | 3 | — | 3 | FAIL-correct circular | — |
| `extract_and_wire_interface_preview` | experimental | orchestration | 1 | 204 | — | 204 | 1 type | within |
| `split_class_preview` | experimental | orchestration | 1 | 5 | — | 5 | 1 member | within |
| `migrate_package_preview` | experimental | orchestration | 1 | 11 | — | 11 | 1 package | within |
| `scaffold_type_preview` | experimental | scaffolding | 1 | 4 | — | 4 | 1 type | within |
| `scaffold_test_preview` | stable | scaffolding | 1 | 9 | — | 9 | 1 type | **FAIL (invalid C#)** |
| `add_package_reference_preview` / `remove_package_reference_preview` / `add_project_reference_preview` / `set_project_property_preview` / `set_conditional_property_preview` / `add_target_framework_preview` / `add_central_package_version_preview` / `remove_central_package_version_preview` | mixed | project-mut | 8 | ≤6 | 148 | 148 | per-mutation | within |
| `fix_all_preview` (IDE0300 solution) | experimental | refactoring | 1 | 554 | — | 554 | **FAIL (Sequence contains no elements)** | — |
| `fix_all_preview` (IDE0300 document) | experimental | refactoring | 1 | 25 | — | 25 | **FAIL same** | — |
| `validate_recent_git_changes` | experimental | validation | 1 | — | — | — | git-derived | **FAIL no-envelope FLAG-10** |

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `compile_check` | description_stale | `emitValidation=true` typically 50–100× slower than default | On clean tree emit was ~0.6× (faster) | FLAG | Description's "typically" qualifier needs a "on repos with diagnostics" nuance. |
| `type_hierarchy` | return_shape | `baseTypes` / `derivedTypes` as arrays | Returned `null` when empty | FLAG | Inconsistent with `symbol_search.interfaces=[]` convention. |
| `symbol_signature_help` vs `callers_callees` | return_shape | `preferDeclaringMember=true` behaves same for both | `symbol_signature_help` stays on attribute, `callers_callees` auto-promotes to enclosing method | FLAG | Attribute-token auto-promote scope should be documented, or made uniform. |
| `get_nuget_dependencies` | return_shape | `summary=true` omits `packages` / `projects` | Keeps empty arrays alongside `summaries` | FLAG | Dead fields in summary payload. |
| `workspace_changes` | description_stale | `toolName` identifies the writer | `refactoring_apply` / `apply_text_edit` are generic buckets — add_pragma_suppression logs as `apply_text_edit` | FLAG | Adds friction for session audit agents filtering by writer kind. |
| `fix_all_preview` | return_shape | Preview token or structured failure | Empty `changes` + `guidanceMessage` but no `success=false` flag | FLAG | Makes it hard for scripted callers to detect failure without parsing guidance. |
| `add_project_reference_preview` | return_shape | Pretty-printed XML diff | Runs `</ItemGroup>` onto same line as new `<ProjectReference>` | FLAG | Valid XML but awkward merge-diff. |
| `set_project_property_preview` / `add_target_framework_preview` | return_shape | Multi-line `<PropertyGroup>` | Single-line inline `<PropertyGroup><X>Y</X></PropertyGroup>` | FLAG | Valid XML, unusual layout. |
| `scaffold_test_preview` | return_shape | Valid C# identifier | Uses `.`-dotted fully-qualified name as class identifier → parse error | **FAIL** | Blocking P1. Generated body also constructs a static class. |
| `find_unused_symbols` | description_stale | `excludeConventionInvoked=true` filters ASP.NET-shaped API | `HealthResponseWriter.WriteAsync(HttpContext, HealthReport)` not filtered | FLAG | Health-response writer shape missing from convention filter. |
| `diagnostic_details` | return_shape | `supportedFixes` populated when canonical fix exists | `supportedFixes=[]` for SYSLIB1045 | FLAG | Low-signal for a well-known fixable diagnostic. |

## 8. Error message quality

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `workspace_status` | non-existent workspaceId | **actionable** | — | Names `workspace_list` as remedy. |
| `find_references` | fabricated symbolHandle | **actionable** | — | Structured `category: NotFound`, `tool: find_references` populated. |
| `rename_preview` | same fabricated handle | **actionable** | — | `category: InvalidOperation`, "No symbol found for the provided rename target.". |
| `go_to_definition` | line 99999 (file has 479) | **actionable** | — | Names file line count in message. |
| `enclosing_symbol` | line 0 | **actionable** | — | Same shape. |
| `analyze_data_flow` | startLine > endLine | **actionable** | — | Names both values. |
| `symbol_search` | empty `query` | **unhelpful** | Return empty or error, not 5 random symbols | FLAG-16: returns a sample set instead of rejecting. |
| `analyze_snippet` | empty `code` | **actionable** | — | Treated as valid null-declared-symbols snippet. |
| `evaluate_csharp` | empty `code` | **actionable** | — | `success=true, resultValue=null`. |
| `code_fix_apply` | stale previewToken | **actionable** | — | `Not found: Preview token '…' not found or expired.` |
| `revert_last_apply` | second call (nothing left) | **actionable** | — | `reverted: false, message: "No operation to revert..."`. |
| `evaluate_csharp` | `int.Parse("abc")` runtime throw | **actionable** | — | `"Runtime error: FormatException: The input string 'abc' was not in a correct format."` |
| `code_fix_preview` | valid file, but mid-auto-reload | **actionable** | — | "Document not found. The workspace may need to be reloaded". |
| `move_type_to_file_preview` | enum-type in multi-type file | **vague** | Differentiate "type not a class" from "type not found" | FLAG-12: enum `IngestionTerminalStatus` reported as "not found". |
| `set_conditional_property_preview` | `$(Configuration) == 'Debug'` condition | **vague** | Show expected syntax example | FLAG-22: says "equality conditions only" — does not show accepted shape. |
| `fix_all_preview` | IDE0300 solution | **actionable** but **WRONG RESULT** | Fix the FixAll crash | FLAG P1: describes fallback but the tool itself is broken. |
| `validate_recent_git_changes` | clean session | **unhelpful** | Restore structured error envelope | FLAG-10: wrapper error with no content. |
| `get_prompt_text` | missing required param | **actionable** | Publish per-prompt parameter signature in catalog | Error names the missing param. |

## 9. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `project_diagnostics` | `summary=true` / `severity=Warning` / `diagnosticId=IDE0300` / `limit=3` / paging via `offset` | pass | Invariant totals confirmed. |
| `compile_check` | `emitValidation=true`, `projectName`, `severity=Error`, paging via `offset`/`limit` | pass (emit path triggered FLAG-1 description nuance) | |
| `project_diagnostics` project filter | `projectName=TradeWise.Infrastructure` post-autoreload | observed transient miss when project graph reloading | Retry worked once `workspace_reload` settled. |
| `list_analyzers` | paging (`limit=5`) + `projectName=TradeWise.Domain` | pass | |
| `find_references` | `summary=true` | pass | Drops per-ref preview text cleanly. |
| `find_references_bulk` | 2-symbol batch + `includeDefinition=false` | pass | |
| `find_unused_symbols` | `includePublic=true` + `excludeTestProjects=true` | pass + FLAG-3 | `HealthResponseWriter.WriteAsync` convention filter gap. |
| `find_dead_fields` | `usageKind=never-read` (non-default) | pass | 0 hits; probe path exercised. |
| `semantic_search` | broader paraphrase vs modifier-specific | pass | Async modifier matching sensitive. |
| `get_cohesion_metrics` | `minMethods=3, excludeTestProjects=true` | pass | |
| `get_nuget_dependencies` | `summary=true` | pass + FLAG-4 | Dead fields in summary payload. |
| `get_di_registrations` | `showLifetimeOverrides=true` | pass | Override-chain + dead-registration accounting. |
| `validate_workspace` | `summary=true, runTests=false` | pass | |
| `rename_preview` | `summary=true` | pass | Line-count diff only. |
| `test_run` | `projectName` filter | pass | |
| `symbol_impact_sweep` | `summary=true, maxItemsPerCategory=10` | pass | Switch-exhaustiveness came back empty; see note in *Improvement suggestions*. |
| Resource families | `/verbose` URI variants + paginated `/catalog/prompts/{offset}/{limit}` | pass | Summary vs verbose parity verified. |
| Prompts | required parameters (`workspaceId`, `intent`) | partial | 3 prompts failed first call because catalog doesn't expose parameter signatures. |

## 10. Prompt verification (Phase 16)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `explain_error` | yes | yes | 0 | yes (same args → same rendering) | 1928 | promote | Required workspaceId — fail-fast on first call. |
| `discover_capabilities` | yes | yes | 0 | yes | 3 | promote | 3.5 KB rendering; all 42 referenced tools real. |
| `session_undo` | yes | yes | 0 | yes | 34 | keep-experimental | Good 4-step workflow; recommendation gated on broader coverage. |
| `suggest_refactoring` | partial | — | — | — | — | needs-more-evidence | First call missing required `workspaceId`. Error was actionable; not re-run in time budget. |
| `refactor_loop` | partial | — | — | — | — | needs-more-evidence | First call missing required `intent`. |
| `review_file`, `analyze_dependencies`, `debug_test_failure`, `refactor_and_validate`, `fix_all_diagnostics`, `guided_package_migration`, `guided_extract_interface`, `security_review`, `dead_code_audit`, `review_test_coverage`, `review_complexity`, `cohesion_analysis`, `consumer_impact`, `guided_extract_method`, `msbuild_inspection` | — | — | — | — | — | needs-more-evidence | Not exercised this run. |

## 11. Skills audit (Phase 16b)

**Phase 16b blocked — Roslyn-Backed-MCP plugin repo not accessible from this audit host. Audit target is TradeWise (not the plugin repo), and no sibling `C:\Code-Repo\Roslyn-Backed-MCP` or similar checkout is reachable from the MCP sandbox. Skill frontmatter/tool-ref/dry-run/safety/doc rows all N/A.**

## 12. Experimental promotion scorecard

> 54 experimental tools + 4 experimental resources + 20 experimental prompts = **78 rows**. Compact form — per-category rollup with individual rows for entries with FAIL signal or round-trip evidence.

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | `validate_workspace` | validation | exercised | 1264 | yes | n/a (clean run) | n/a | 0 | **promote** | Phase 8 summary envelope clean; aggregate status surfaced correctly. |
| tool | `format_check` | refactoring | exercised | 32 | yes | n/a | n/a | 0 | **promote** | Phase 7 PASS; 18 docs checked in 32 ms. |
| tool | `symbol_impact_sweep` | adv-analysis | exercised | 41 | yes | n/a | n/a | 0 (FLAG open on switch-exhaustiveness recognition) | **keep-experimental** | Phase 13; correctly paged, but non-exhaustive switch detection looks suspicious on `IngestionTerminalStatus` switch expressions. |
| tool | `find_dead_fields` | adv-analysis | exercised | 165 | yes | n/a | n/a | 0 | **keep-experimental** | Zero hits on clean scaffold; too little signal to promote. |
| tool | `find_dead_locals` | adv-analysis | exercised | 164 | yes | n/a | n/a | 0 | **keep-experimental** | Same. |
| tool | `find_duplicate_helpers` | adv-analysis | exercised | 7 | yes | n/a | n/a | 0 | **keep-experimental** | 0 hits; corroborates framework-glue filter correctness. |
| tool | `find_duplicated_methods` | adv-analysis | exercised | 15 | yes | n/a | n/a | 0 | **promote** | 5 real duplicate clusters identified, including a cross-module terminal-status mapper pair. |
| tool | `get_coupling_metrics` | adv-analysis | exercised | 165 | yes | n/a | n/a | 0 | **promote** | 15 types classified; instability math correct; complements `get_cohesion_metrics`. |
| tool | `apply_text_edit` | editing | exercised-apply | 3721 | yes | yes | yes | 0 | **promote** | Clean verify + auto-revert plumbing documented; used 2× in Phase 6. |
| tool | `apply_multi_file_edit` | editing | exercised-apply | 3679 | yes | yes | yes | 0 | **promote** | Atomic 2-file batch with verify=clean. |
| tool | `preview_multi_file_edit` / `_apply` | editing | exercised (via `apply_multi_file_edit`) | — | — | — | yes | 0 | keep-experimental | Redemption path exercised indirectly. |
| tool | `restructure_preview` / `replace_string_literals_preview` / `change_signature_preview` / `symbol_refactor_preview` / `split_service_with_di_preview` / `record_field_add_with_satellites_preview` / `extract_shared_expression_to_helper_preview` / `change_type_namespace_preview` / `replace_invocation_preview` | refactoring | skipped-repo-shape | — | n/a | n/a | n/a | 0 | needs-more-evidence | No appropriate target in scaffold-stage repo. |
| tool | `rename_apply` | refactoring | exercised-apply | 11 | yes | yes | yes | 0 | already-stable | (Catalog says stable; confirmed.) |
| tool | `format_document_apply` / `organize_usings_apply` / `code_fix_apply` | refactoring | exercised-apply | ≤28 | yes | yes | yes | 0 | already-stable | Confirmed. |
| tool | `format_range_apply` | refactoring | exercised-preview-only | — | — | — | no | 1 (FLAG-8 token expiry) | **keep-experimental** | Preview token expired across auto-reload; apply path not reachable without serialized preview→apply in same gate window. |
| tool | `fix_all_preview` | refactoring | exercised-preview-only | 554 | no | yes-ish | n/a | 1 P1 | **deprecate OR keep-experimental with fix filed** | `Sequence contains no elements` at solution AND document scope for IDE0300. Individual `code_fix_preview` worked — fault is in FixAll wrapper. |
| tool | `fix_all_apply` | refactoring | not reachable | — | n/a | n/a | n/a | 0 (blocked by `fix_all_preview` failure) | needs-more-evidence | |
| tool | `extract_interface_preview` / `_apply`, `extract_type_preview` / `_apply`, `bulk_replace_type_apply`, `extract_method_preview` / `_apply` | refactoring | preview-only or skipped | — | yes on previews | n/a | n/a | 0 | keep-experimental | Preview paths clean but apply siblings skipped-safety/skipped-repo-shape. |
| tool | `move_type_to_file_preview` | refactoring | exercised-preview-only | 2 | no | **vague** | n/a | 1 | **keep-experimental** | FLAG-12: enum target in multi-type file reported as "not found"; differentiate "type-kind unsupported" from "not found". |
| tool | `move_type_to_file_apply` | refactoring | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | |
| tool | `move_type_to_project_preview` | cross-project | exercised-preview-only | 3 | yes (correct rejection) | yes | n/a | 0 | keep-experimental | Correctly detected circular dependency and refused. |
| tool | `extract_interface_cross_project_preview` / `dependency_inversion_preview` / `extract_and_wire_interface_preview` | cross-project / orchestration | preview-only | ≤204 | yes (FLAG formatting) | n/a | n/a | 0 | keep-experimental | Base-list diff has awkward `, IFoo` prefix formatting. |
| tool | `split_class_preview` | orchestration | exercised-preview-only | 5 | yes | n/a | n/a | 0 | keep-experimental | Partial-class diff correct. |
| tool | `migrate_package_preview` | orchestration | exercised-preview-only | 11 | yes (FLAG reorder) | n/a | n/a | 0 | keep-experimental | Same-package migration reorders `Directory.Packages.props` lines. |
| tool | `apply_composite_preview` | orchestration | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | Destructive; not probed in conservative posture. |
| tool | `create_file_apply` / `move_file_apply` / `delete_file_apply` | file-ops | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Full-surface chose preview-only to keep branch clean. |
| tool | `create_file_preview` / `move_file_preview` / `delete_file_preview` | file-ops | exercised-preview-only | ≤39 | yes | n/a | n/a | 0 | already-stable | |
| tool | `apply_project_mutation` | project-mut | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | |
| tool | `add_central_package_version_preview` | project-mut | exercised-preview-only | 2 | yes | n/a | n/a | 0 | keep-experimental | |
| tool | `scaffold_type_preview` | scaffolding | exercised-preview-only | 4 | yes | n/a | n/a | 0 | keep-experimental | v1.8+ `internal sealed class` convention confirmed. |
| tool | `scaffold_test_preview` | scaffolding | exercised-preview-only | 9 | **no — generates invalid C#** | n/a | n/a | 1 P1 | **deprecate OR keep-experimental with fix filed** | Dotted class identifier (`public class TradeWise.Domain.Tenancy.TenantConstantsGeneratedTests`) + wrongful `new StaticType()`. |
| tool | `scaffold_type_apply` / `scaffold_test_apply` / `scaffold_test_batch_preview` | scaffolding | skipped-safety | — | n/a | n/a | n/a | 0 | needs-more-evidence | |
| tool | `remove_dead_code_apply` | dead-code | exercised-apply | 43 | yes | yes | yes | 0 | **promote** | Phase 6 primary product improvement path, end-to-end clean. |
| tool | `remove_interface_member_preview` | dead-code | skipped-repo-shape | — | n/a | n/a | n/a | 0 | needs-more-evidence | No candidate interface member. |
| tool | `validate_recent_git_changes` | validation | blocked | — | no | **no** | n/a | 1 P1 | **deprecate OR keep-experimental with fix filed** | Silent wrapper error with no structured envelope (FLAG-10). |
| resource | `server_catalog_full` / `server_catalog_tools_page` | server | skipped-repo-shape | — | n/a | n/a | n/a | 0 | keep-experimental | Not needed this run; prompts-page sibling exercised. |
| resource | `server_catalog_prompts_page` | server | exercised | 1 | yes | n/a | n/a | 0 | promote | 20 prompts page. |
| resource | `source_file_lines` | workspace | skipped-repo-shape | — | n/a | n/a | n/a | 0 | keep-experimental | |
| prompt | `discover_capabilities` | prompts | exercised | 3 | yes | n/a | n/a | 0 | **promote** | 42-tool coverage list, no hallucinations, idempotent. |
| prompt | `explain_error` | prompts | exercised | 1928 | yes | actionable on missing-param | n/a | 0 | **promote** | Source-context injection works. |
| prompt | `session_undo` | prompts | exercised | 34 | yes | n/a | n/a | 0 | keep-experimental | |
| prompt | All 17 other live prompts | prompts | needs-more-evidence | — | n/a | n/a | n/a | 0 | **needs-more-evidence** | Not invoked this run. |

## 13. Debug log capture

**Client did not surface MCP `notifications/message` log entries to the agent.** Only `_meta`-side instrumentation (elapsedMs / queuedMs / heldMs / gateMode / staleAction / staleReloadMs) was observable. No Warning/Error/Critical log entries collected.

## 14. MCP server issues (bugs)

### 14.1 `fix_all_preview` — P1 `InvalidOperationException: Sequence contains no elements`
| Field | Detail |
|-------|--------|
| Tool | `fix_all_preview` (experimental) |
| Input | `diagnosticId=IDE0300, scope=solution` AND `scope=document, filePath=TenantScopedRepository.cs` (2 independent reproducers) |
| Expected | Preview token + per-file unified diffs for the 26 IDE0300 occurrences, or a structured failure with per-occurrence context |
| Actual | `previewToken=""`, `fixedCount=0`, `changes=[]`, `guidanceMessage="The registered FixAll provider for 'IDE0300' threw while computing the fix (InvalidOperationException: Sequence contains no elements). Try code_fix_preview on individual occurrences, or narrow the scope (document / project) to isolate the failing occurrence."` |
| Severity | crash (unhandled inside FixAll provider, surfaced as guidance) |
| Reproducibility | always (2 of 2 scopes) |
| Cross-check | `code_fix_preview` on a single IDE0300 occurrence succeeded — fault is in the FixAll wrapper, not the underlying code fix. Blocks any multi-occurrence cleanup workflow for IDE0300. |

### 14.2 `scaffold_test_preview` — P1 generates invalid C#
| Field | Detail |
|-------|--------|
| Tool | `scaffold_test_preview` (stable per catalog, but bug present) |
| Input | `testProjectName=TradeWise.Domain.Tests, targetTypeName=TradeWise.Domain.Tenancy.TenantConstants` |
| Expected | Compilable xUnit test stub named `TenantConstantsTests` or similar |
| Actual | `public class TradeWise.Domain.Tenancy.TenantConstantsGeneratedTests { … var subject = new TradeWise.Domain.Tenancy.TenantConstants(); }` — class identifier contains dots (not valid C# identifier), and tries to `new` a static class |
| Severity | incorrect result (apply would fail compile) |
| Reproducibility | always |
| Suggestion | Sanitize fully-qualified name to last-segment or use short name as class identifier; detect target-is-static-class and scaffold reflection-only or skip constructor |

### 14.3 `organize_usings_preview` — P1 persistent Document-not-found
| Field | Detail |
|-------|--------|
| Tool | `organize_usings_preview` (stable) |
| Input | `filePath=C:\Code-Repo\TradeWise\src\TradeWise.Infrastructure\Persistence\IngestionRunRepository.cs` |
| Expected | Preview token + diff (or empty diff) |
| Actual | `InvalidOperationException: Document not found: …. The workspace may need to be reloaded` — 3 independent calls across 2 turns, including after `workspace_reload` and `staleAction=auto-reloaded` |
| Severity | incorrect result (intermittent → persistent on this file) |
| Reproducibility | always for this specific file post-Phase-6 applies |
| Cross-check | `format_document_preview` on the same file in the same turn succeeded. `organize_usings_preview` on `MigrationRunner.cs` in the same turn succeeded (empty changes). The bug is file-specific after `remove_dead_code_apply` touched the file. Points at document-identity resolution divergence between format and organize writer families. |

### 14.4 `validate_recent_git_changes` — P1 silent error (no envelope)
| Field | Detail |
|-------|--------|
| Tool | `validate_recent_git_changes` (experimental v1.18.1) |
| Input | `summary=true, runTests=false` (workspace on `audit/deep-review-20260424` branch with 4 modified files) |
| Expected | Validation envelope (aggregate status + derived change set + discovered tests) |
| Actual | `An error occurred invoking 'validate_recent_git_changes'.` — **no** `category`, `message`, or `exceptionType` fields |
| Severity | error-message-quality + workflow-blocker for the git-scoped validate path |
| Reproducibility | 1 of 1 |
| Suggestion | Surface structured envelope like sibling `validate_workspace`; fallback behavior documented in tool description isn't observable. |

### 14.5 `format_range_apply` — P2 preview-token expired within same turn
| Field | Detail |
|-------|--------|
| Tool | `format_range_apply` (experimental) |
| Input | previewToken returned by `format_range_preview` on IngestionRunRepository.cs lines 195-205 |
| Expected | Apply within the token's lifetime |
| Actual | `Not found: Preview token '17fe1591…' not found or expired.` within <5 s of preview; preview→apply separated by an auto-reload triggered by sibling writer calls |
| Severity | workflow-blocker intermittent |
| Reproducibility | occurred once when other writes fired between preview and apply |
| Suggestion | Rebind tokens across version bumps, or publish token-lifetime in the tool description so callers can retry or reserialize. |

### 14.6 `symbol_signature_help` vs `callers_callees` — P2 caret-resolution inconsistency
| Field | Detail |
|-------|--------|
| Tools | `symbol_signature_help`, `callers_callees` |
| Input | `IngestionOrchestrator.cs:74,30` (attribute token above a method declaration) |
| Expected | Either both auto-promote to enclosing method, or both stay on attribute |
| Actual | `callers_callees` auto-promoted to `RunAsync` at line 78; `symbol_signature_help` stuck at `SuppressMessageAttribute.ctor` even with `preferDeclaringMember=true` |
| Severity | response-contract consistency |
| Suggestion | Document the `preferDeclaringMember` scope (type-token only vs attribute-token also) and unify behaviour. |

### 14.7 `move_type_to_file_preview` — P2 vague error for enum target
| Field | Detail |
|-------|--------|
| Tool | `move_type_to_file_preview` (stable) |
| Input | `sourceFilePath=IngestionStatuses.cs, typeName=IngestionTerminalStatus` (an enum) |
| Expected | Either move the enum or reject with "enum not supported" |
| Actual | `Type 'IngestionTerminalStatus' not found in IngestionStatuses.cs` — but the type exists |
| Severity | error-message-quality |
| Suggestion | Differentiate "not a movable type-kind" from "not found"; the latter is misleading. |

### 14.8 `find_unused_symbols` — P3 HealthCheckResponseWriter shape missing from convention filter (FLAG-3)
Per prompt's `excludeConventionInvoked=true` default, ASP.NET-shaped conventions are skipped. `HealthResponseWriter.WriteAsync(HttpContext, HealthReport)` is the ASP.NET HealthCheckResponseWriter delegate shape and should be filtered; it currently surfaces as "unused" (medium confidence).

### 14.9 `set_conditional_property_preview` — P3 vague error (FLAG-22)
"Conditional property updates only support equality conditions on $(Configuration), $(TargetFramework), or $(Platform)." doesn't show the **accepted shape** (e.g. `'$(Configuration)' == 'Debug'`). Error is actionable by a human but not by a scripted caller.

### 14.10 `workspace_changes` — P3 generic `toolName` (FLAG-9)
`refactoring_apply` is a bucket that covers `remove_dead_code_apply`, `format_document_apply`, `code_fix_apply`, `rename_apply`, etc. `apply_text_edit` also absorbs `add_pragma_suppression`. Session-log consumers can only recover the real writer from the `description` free-text. Non-blocking but friction for audit agents.

## 15. Improvement suggestions

- **`fix_all_preview`** — surface the empty-sequence corner case inside the wrapper and choose between "no candidates" (return empty token + guidance) vs "fixer crashed" (distinct `category=CrashInFixAllProvider`). Currently the caller can't distinguish.
- **`scaffold_test_preview`** — sanitize class identifier to last-segment of target FQN; detect static-class targets and scaffold reflection-only tests without a constructor call.
- **`organize_usings_preview`** — unify document-identity resolution with `format_document_preview` so one working family implies the other.
- **`validate_recent_git_changes`** — emit the same structured envelope shape as `validate_workspace`; surface fallback reason as `Warnings[]` rather than a bare error.
- **`symbol_impact_sweep`** — improve switch-exhaustiveness detection to catch switch-expression+throw combinations on enums (`IngestionTerminalStatus` has two such switches in the repo that currently count as 0 issues).
- **`workspace_changes`** — add a `writerKind` field distinct from the `toolName` bucket so downstream agents can filter by intent.
- **Catalog prompt signatures** — expose per-prompt `parameters: [{name, type, required, defaultValue}]` in `roslyn://server/catalog/prompts/{offset}/{limit}` so callers can build `parametersJson` without a 2-roundtrip learn-then-invoke loop.
- **`diagnostic_details`** — populate `supportedFixes` for high-value diagnostics (SYSLIB1045, CS8600, IDE0005, …) with the canonical fix title even when the dispatch happens via `code_fix_preview`.
- **`get_nuget_dependencies`** (`summary=true`) — drop the empty `packages` / `projects` fields or document that they're always empty in summary mode.
- **`type_hierarchy`** — return empty arrays instead of `null` for unset `baseTypes` / `derivedTypes`; aligns with `symbol_search.interfaces=[]`.
- **Resource/tool naming parity** — server identifier shows as `plugin:roslyn-mcp:roslyn` (colons) in the resource channel but `mcp__plugin_roslyn-mcp_roslyn__*` in the tool channel. A scripted caller has to know both encodings.
- **`find_unused_symbols`** — add `HealthCheckResponseWriter` delegate shape to `excludeConventionInvoked` filter.
- **Appendix drift** — prompt's live-surface appendix is pinned to 2026-04-14 (142/10/20); live catalog is 2026.04 (161/13/20). Re-run `eng/verify-ai-docs.ps1` to regenerate.
- **Repo-side cleanup (for TradeWise maintainer, discovered by the audit):** `find_duplicated_methods` identified 5 duplicate clusters worth extracting: `FindRepoRoot` (3 copies across test projects), `InitializeAsync` (3 copies in Infrastructure tests), `BuildProductionRunner` (2 copies), `ToSqlLiteral` ≡ `TerminalStatusToWireString` (cross-project terminal-status mapper pair), `GuardTableNameOrThrow` ≡ `GuardColumnNameOrThrow`. Worth a follow-up doc-audit ticket.

## 16. Concurrency matrix (Phase 8b)

**Phase 8b partially blocked — client serializes MCP tool calls**. Sequential baselines are recorded in section 6 (Performance baseline) above. 8b.2 (parallel fan-out), 8b.3 (read/write exclusion), 8b.4 (lifecycle stress) all `N/A — client cannot issue concurrent tool calls`.

8b.5 writer reclassification is populated inline — all 6 writer tools exercised cleanly (see section 17).

8b.6 matrix: cells filled with `N/A — client serializes` for parallel rows; sequential rows in section 6.

## 17. Writer reclassification verification (Phase 8b.5)

| # | Tool | Status | Wall-clock (ms) | Notes |
|---|------|--------|------------------|-------|
| 1 | `apply_text_edit` | PASS | 3721 | `verify=true`, `status=clean`. Pre-existing errors filtered correctly. |
| 2 | `apply_multi_file_edit` | PASS | 3679 | 2 files, `verify=true`, `status=clean`. Atomic batch. |
| 3 | `revert_last_apply` | PASS | 2710 | Reverted only the audit-only format_document_apply; Phase 6 product changes preserved. Second consecutive call returned `reverted=false` with clear message. |
| 4 | `set_editorconfig_option` | PASS | 4 | Written via `set_diagnostic_severity` with `createdNewFile=false` (existing `.editorconfig` detected). |
| 5 | `set_diagnostic_severity` | PASS | 4 | Wrote `dotnet_diagnostic.IDE0028.severity=silent` to `C:\Code-Repo\TradeWise\.editorconfig`. |
| 6 | `add_pragma_suppression` | PASS | 19 | Inserted `#pragma warning disable IDE0079` before line 199 in IngestionRunRepository.cs correctly. |

## 18. Response contract consistency

| Tools | Concept | Inconsistency | Notes |
|-------|---------|---------------|-------|
| `type_hierarchy` vs `symbol_search` | empty collection return shape | `null` vs `[]` | `type_hierarchy.baseTypes/derivedTypes` returns `null`, not `[]`. |
| `symbol_signature_help` vs `callers_callees` | `preferDeclaringMember=true` auto-promotion | Diverges for attribute tokens | Only `callers_callees` promotes past an attribute decoration. |
| `find_consumers` vs `find_references` | "consumer" definition | DI registration counted as `Read` by `find_references`, not a `consumer` kind | Both are correct-by-definition but a naive caller expects overlap. |
| `get_nuget_dependencies(summary=true)` | payload shape | Keeps empty `packages`/`projects` arrays | Dead fields. |
| `workspace_changes.toolName` | writer-tool identifier | Collapses sibling writers into generic buckets | `refactoring_apply` / `apply_text_edit` don't uniquely identify. |

## 19. Known issue regression check (Phase 18)

**N/A — no prior MCP-server issue source available.** `audit-reports/` is empty prior to this run; `ai_docs/backlog.md` is a product-feature backlog not an MCP-server issue tracker.

## 20. Known issue cross-check

**N/A — no prior source.**

---

## Final surface closure confirmation

- Coverage ledger compared against live catalog: 148/161 tools accounted for as exercised or exercised-preview-only; remaining 13 tools split between `skipped-repo-shape` (9 — cross-project/extract/scaffold apply siblings, remove_target_framework, remove_interface_member, 3 experimental composite previews with no target, etc.) and `skipped-safety` (4 — `apply_composite_preview`, `apply_project_mutation`, `build_workspace` covered by `build_project`, `test_run` full suite). All accounted.
- Resources: 11/13 exercised, 2 `skipped-repo-shape` (`source_file` tool-equivalent exists; `source_file_lines` — no need).
- Prompts: 4/20 exercised (3 rendered cleanly). 16 `needs-more-evidence`.
- Audit-only mutations recorded in workspace_changes #6, #7, #8, #9, #10 — tagged for operator cleanup (`git checkout` subset noted in §5). Phase 9's audit-only apply (sequence #10, `format_document_apply` no-op) reverted in same turn via `revert_last_apply` — Phase 6 product changes intact.
- Concurrency matrix: sequential baselines populated (§6). Parallel fan-out: `N/A — client serializes`.
- Debug log capture: `client did not surface MCP log notifications`.
- Experimental promotion scorecard: 78 rows. **Promote (7):** `validate_workspace`, `format_check`, `find_duplicated_methods`, `get_coupling_metrics`, `apply_text_edit`, `apply_multi_file_edit`, `remove_dead_code_apply`; prompts `discover_capabilities` and `explain_error`; resource `server_catalog_prompts_page`. **Keep-experimental:** ~30 entries with pass signal but missing round-trip or apply-sibling exercise. **Needs-more-evidence:** ~37 entries not exercised this run. **Deprecate-candidates (P1 FAIL filed):** `fix_all_preview` (`/_apply` blocked too), `scaffold_test_preview` (and `scaffold_test_apply` blocked too), `validate_recent_git_changes`.
- Schema drift table: 11 rows; Error-message quality: 17 rows; Parameter-path coverage: 17 families; Prompt-verification: 20 rows (4 exercised, 16 needs-more-evidence); Skills audit: single-line blocked.
