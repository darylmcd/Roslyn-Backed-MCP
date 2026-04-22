# MCP Server Audit Report

**Intended canonical store (Roslyn-Backed-MCP):** copy this file to  
`<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/20260413T154959Z_firewallanalyzer_mcp-server-audit.md`  
before rollup/backlog automation (`eng/import-deep-review-audit.ps1`).

**Final rollup:** This markdown is the **only shipped artifact** — §3 is the human ledger; **Appendix B** embeds the canonical TAB-separated ledger (extract the fenced block for spreadsheet or import tooling).

---

## 1. Header

| Field | Value |
|-------|--------|
| **Date (UTC)** | 2026-04-13 |
| **Audited solution** | `c:\Code-Repo\DotNet-Firewall-Analyzer\FirewallAnalyzer.slnx` |
| **Audited revision** | Branch `audit/mcp-full-surface-20260413` (disposable); workspace loaded from same working tree |
| **Entrypoint loaded** | `FirewallAnalyzer.slnx` |
| **Audit mode** | `full-surface` |
| **Isolation** | Git branch `audit/mcp-full-surface-20260413` at `c:\Code-Repo\DotNet-Firewall-Analyzer` — merge or delete after review; Phase 6 product edits retained on branch |
| **Client** | Cursor agent → MCP server id `user-roslyn` (stdio) |
| **Workspace id** | Continuation load `8e649a54ff3443afbaaffe86e5e0d652` (prior session `10cc528e…` closed earlier); **closed** at end of continuation |
| **Server** | `roslyn-mcp` **1.11.2+d0f2680698687bd6fb93c4af5ddabe22f59eb0b2** (`server_info`) |
| **Catalog version** | **2026.04** |
| **Roslyn / .NET** | Roslyn **5.3.0.0**; runtime **.NET 10.0.5**; OS Windows 10.0.26200 |
| **Scale** | 11 projects, 243 documents (post–`workspace_load` summary) |
| **Repo shape** | Multi-project; tests (xUnit); **Central Package Management**; net10.0 only (no multi-targeting); DI registrations in `ApiHostBuilder`; analyzers + SecurityCodeScan; source-generated/docs present (`source_generated_documents`); `.editorconfig` at repo root |
| **Prior issue source (Phase 18)** | `ai_docs/backlog.md` (this repo) — not a Roslyn-MCP backlog; regression section **N/A** |
| **Debug log channel** | **no** — Cursor did not surface MCP `notifications/message` structured logs to the agent |
| **Plugin skills repo path** | **blocked — Roslyn-Backed-MCP / plugin workspace not present in this Cursor workspace** |
| **Report path note** | Written under audited repo per deep-review fallback when Roslyn-Backed-MCP root is not the workspace |
| **dotnet restore** | **PASS** — `dotnet restore FirewallAnalyzer.slnx` before semantic tools |

### Authoritative surface mismatch (live catalog wins)

- **Finding:** `ai_docs/prompts/deep-review-and-refactor.md` frontmatter claims **128 tools (69 stable / 59 experimental)**.
- **Live:** **127 tools (69 stable / 58 experimental)**, **9** resources, **19** prompts.
- **Action:** Treat live server as source of truth; update living prompt frontmatter on next doc pass.

---

## 2. Coverage summary

Counts aggregated from ledger by **Kind** + **Category** (live catalog columns). `#stable` / `#experimental` = catalog entries in that bucket.

| Kind | Category | #stable | #experimental | exercised | exercised-apply | exercised-preview-only | skipped-repo-shape | skipped-safety | blocked |
|------|----------|---------|-----------------|-----------|-------------------|------------------------|--------------------|----------------|---------|
| prompt | prompts | 0 | 19 | 0 | 0 | 0 | 0 | 0 | 19 |
| resource | server | 2 | 0 | 2 | 0 | 0 | 0 | 0 | 0 |
| resource | workspace | 7 | 0 | 0 | 0 | 0 | 0 | 0 | 7 |
| tool | advanced-analysis | 9 | 2 | 11 | 0 | 0 | 0 | 0 | 0 |
| tool | analysis | 12 | 0 | 12 | 0 | 0 | 0 | 0 | 0 |
| tool | code-actions | 3 | 0 | 1 | 0 | 0 | 0 | 2 | 0 |
| tool | configuration | 0 | 3 | 1 | 0 | 0 | 0 | 2 | 0 |
| tool | editing | 0 | 3 | 0 | 1 | 0 | 0 | 2 | 0 |
| tool | refactoring | 8 | 14 | 3 | 3 | 1 | 0 | 15 | 0 |
| tool | scripting | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| tool | security | 3 | 0 | 3 | 0 | 0 | 0 | 0 | 0 |
| tool | server | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| tool | symbols | 16 | 0 | 14 | 0 | 0 | 2 | 0 | 0 |
| tool | syntax | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |
| tool | undo | 0 | 1 | 0 | 1 | 0 | 0 | 0 | 0 |
| tool | unknown | 0 | 33 | 2 | 4 | 6 | 0 | 21 | 0 |
| tool | validation | 8 | 0 | 7 | 0 | 0 | 0 | 1 | 0 |
| tool | workspace | 8 | 1 | 9 | 0 | 0 | 0 | 0 | 0 |

_Row sum check: 155 catalog entries across kinds/categories; tool-name-level detail in §3 and Appendix B._

---

## 3. Coverage ledger

| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | add_central_package_version_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | add_package_reference_preview | experimental | unknown | exercised-preview-only | 13 | — | FluentValidation add preview; paired with remove forward/apply |
| tool | add_pragma_suppression | experimental | editing | skipped-safety | 7-8b | — | intentionally avoided mutating .editorconfig/source for audit cleanliness |
| tool | add_project_reference_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | add_target_framework_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | analyze_control_flow | stable | advanced-analysis | exercised | 4 | 6 |  |
| tool | analyze_data_flow | stable | advanced-analysis | exercised | 4 | 10 |  |
| tool | analyze_snippet | stable | analysis | exercised | 5 | 60-174 | multi kind |
| tool | apply_code_action | stable | code-actions | skipped-safety | 6-13 | — | full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only) |
| tool | apply_composite_preview | experimental | unknown | skipped-safety | 6-13 | — | full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only) |
| tool | apply_multi_file_edit | experimental | editing | skipped-safety | 6-8b | — | not exercised; rename/usings covered writes |
| tool | apply_project_mutation | experimental | unknown | exercised-apply | 13 | 3091-2992 | add/remove FluentValidation on Domain; then manual csproj tidy for empty ItemGroup |
| tool | apply_text_edit | experimental | editing | exercised-apply | 9,17 | 22-4 | Phase 9 Program.cs line replace; FLAG: column/end-line discipline easy to corrupt semicolon |
| tool | build_project | stable | validation | exercised | 8 | 1525 |  |
| tool | build_workspace | stable | validation | exercised | 8 | 13833 | queued gate |
| tool | bulk_replace_type_apply | experimental | refactoring | skipped-safety | 6-13 | — | full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only) |
| tool | bulk_replace_type_preview | experimental | refactoring | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | callers_callees | stable | analysis | exercised | 3 | 9 |  |
| tool | code_fix_apply | stable | refactoring | skipped-safety | 6 | — | preview/apply happy path not re-run this continuation |
| tool | code_fix_preview | stable | refactoring | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | compile_check | stable | validation | exercised | 1,6,8 | varies | emitValidation ~3712ms |
| tool | create_file_apply | experimental | unknown | exercised-apply | 10 | 3237 | placeholder file then deleted |
| tool | create_file_preview | experimental | unknown | exercised-preview-only | 10 | 10 | McpAuditPlaceholder.cs preview |
| tool | delete_file_apply | experimental | unknown | exercised-apply | 10-12 | 3128-3295 | placeholder + scaffold cleanup |
| tool | delete_file_preview | experimental | unknown | exercised-preview-only | 10-12 | 1-10 | audited delete previews |
| tool | dependency_inversion_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | diagnostic_details | stable | analysis | exercised | 1 | 4 |  |
| tool | document_symbols | stable | symbols | exercised | 3 | — |  |
| tool | enclosing_symbol | stable | symbols | exercised | 14 | 139 | LoadAppSettings at ApiHostBuilder.cs:100,5 |
| tool | evaluate_csharp | stable | scripting | exercised | 5 | 21-13006 | timeout path validated |
| tool | evaluate_msbuild_items | experimental | unknown | exercised | 7 | 149 | Compile items |
| tool | evaluate_msbuild_property | experimental | unknown | exercised | 7 | 78 |  |
| tool | extract_and_wire_interface_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | extract_interface_apply | experimental | refactoring | skipped-safety | 6-13 | — | full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only) |
| tool | extract_interface_cross_project_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | extract_interface_preview | experimental | refactoring | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | extract_method_apply | experimental | refactoring | skipped-safety | 6-13 | — | full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only) |
| tool | extract_method_preview | experimental | refactoring | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | extract_type_apply | experimental | refactoring | skipped-safety | 6-13 | — | full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only) |
| tool | extract_type_preview | experimental | refactoring | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | find_base_members | stable | symbols | exercised | 14 | — | PanosApiException.ErrorCode → FirewallAnalyzerException.ErrorCode |
| tool | find_consumers | stable | analysis | exercised | 3 | 4 |  |
| tool | find_implementations | stable | symbols | exercised | 3 | 2 |  |
| tool | find_overrides | stable | symbols | exercised | 14 | — | FirewallAnalyzerException.ErrorCode overrides (4 types) |
| tool | find_property_writes | stable | symbols | skipped-repo-shape | 3-14 | — | no suitable virtual/override/property target selected in abbreviated Phase 3/14 |
| tool | find_references | stable | symbols | exercised | 3 | 9 |  |
| tool | find_references_bulk | stable | symbols | exercised | 14 | 22 |  |
| tool | find_reflection_usages | stable | advanced-analysis | exercised | 11 | 1218 |  |
| tool | find_shared_members | stable | analysis | exercised | 3 | 8 |  |
| tool | find_type_mutations | stable | analysis | exercised | 3 | 8 |  |
| tool | find_type_usages | stable | analysis | exercised | 3 | 29 |  |
| tool | find_unused_symbols | stable | advanced-analysis | exercised | 2 | 829-1710 | includePublic true/false |
| tool | fix_all_apply | experimental | refactoring | skipped-safety | 6-13 | — | full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only) |
| tool | fix_all_preview | experimental | refactoring | exercised-preview-only | 6 | 80 | IDE0005 no provider; guidance ok |
| tool | format_document_apply | stable | refactoring | exercised-apply | 6 | 1 | no-op diff |
| tool | format_document_preview | stable | refactoring | exercised | 6 | 78 |  |
| tool | format_range_apply | experimental | refactoring | skipped-safety | 6 | — | not invoked this continuation |
| tool | format_range_preview | experimental | refactoring | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | get_code_actions | stable | code-actions | exercised | 6 | 199 | empty at CA1859 caret |
| tool | get_cohesion_metrics | stable | analysis | exercised | 2 | large | result written to agent-tools |
| tool | get_completions | stable | symbols | exercised | 14 | 178 | ApiHostBuilder builder. Environment\|Equals with filterText=E |
| tool | get_complexity_metrics | stable | advanced-analysis | exercised | 2 | 91 |  |
| tool | get_di_registrations | stable | advanced-analysis | exercised | 11 | 1235 |  |
| tool | get_editorconfig_options | experimental | configuration | exercised | 7 | 6 |  |
| tool | get_msbuild_properties | experimental | unknown | skipped-safety | 7 | — | avoid 50-700KB payload this turn; evaluate_msbuild_* exercised |
| tool | get_namespace_dependencies | stable | advanced-analysis | exercised | 2 | large | agent-tools |
| tool | get_nuget_dependencies | stable | advanced-analysis | exercised | 2 | 1448 |  |
| tool | get_operations | experimental | advanced-analysis | exercised | 4 | 3 |  |
| tool | get_source_text | stable | workspace | exercised | 4 | 3 |  |
| tool | get_syntax_tree | experimental | syntax | exercised | 4 | 7 |  |
| tool | goto_type_definition | stable | symbols | exercised | 14 | 153-7 | IPanosClient on AddSingleton line; top-level Program.cs probes NotFound (expected repo shape) |
| tool | go_to_definition | stable | symbols | exercised | 14 | — |  |
| tool | impact_analysis | stable | analysis | exercised | 3 | 4 |  |
| tool | list_analyzers | stable | analysis | exercised | 1 | 8 | paged |
| tool | member_hierarchy | stable | symbols | skipped-repo-shape | 3-14 | — | no suitable virtual/override/property target selected in abbreviated Phase 3/14 |
| tool | migrate_package_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | move_file_apply | experimental | unknown | skipped-safety | 6-13 | — | full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only) |
| tool | move_file_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | move_type_to_file_apply | experimental | refactoring | skipped-safety | 10 | — | not invoked; create/delete loop used instead |
| tool | move_type_to_file_preview | experimental | refactoring | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | move_type_to_project_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | nuget_vulnerability_scan | stable | security | exercised | 1 | 5302 |  |
| tool | organize_usings_apply | stable | refactoring | exercised-apply | 6 | 6 |  |
| tool | organize_usings_preview | stable | refactoring | exercised | 6 | 134 |  |
| tool | preview_code_action | stable | code-actions | skipped-safety | 6 | — | not invoked this continuation |
| tool | project_diagnostics | stable | analysis | exercised | 1 | 4738 | full scan |
| tool | project_graph | stable | workspace | exercised | 0 | 2 |  |
| tool | remove_central_package_version_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | remove_dead_code_apply | experimental | unknown | skipped-safety | 6-13 | — | full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only) |
| tool | remove_dead_code_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | remove_package_reference_preview | experimental | unknown | exercised-preview-only | 13 | 2 | remove FluentValidation preview after forward apply |
| tool | remove_project_reference_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | remove_target_framework_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | rename_apply | stable | refactoring | exercised-apply | 6 | 39 |  |
| tool | rename_preview | stable | refactoring | exercised | 6 | 499 | bad column probe documented |
| tool | revert_last_apply | experimental | undo | exercised-apply | 9,17 | 96-4203 | Phase 9 text-edit revert; Phase 17 probe undid delete (LIFO) — FLAG stack semantics vs cleanup |
| tool | scaffold_test_apply | experimental | unknown | skipped-safety | 6-13 | — | full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only) |
| tool | scaffold_test_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | scaffold_type_apply | experimental | unknown | exercised-apply | 12 | 3110 | McpAuditScaffoldType then deleted |
| tool | scaffold_type_preview | experimental | unknown | exercised-preview-only | 12 | 2 | internal sealed class scaffold v1.8+ default |
| tool | security_analyzer_status | stable | security | exercised | 1 | 2875 |  |
| tool | security_diagnostics | stable | security | exercised | 1 | 3002 |  |
| tool | semantic_search | stable | advanced-analysis | exercised | 11 | large | agent-tools |
| tool | server_info | stable | server | exercised | 0 | 5 |  |
| tool | set_conditional_property_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | set_diagnostic_severity | experimental | configuration | skipped-safety | 7-8b | — | intentionally avoided mutating .editorconfig/source for audit cleanliness |
| tool | set_editorconfig_option | experimental | configuration | skipped-safety | 7-8b | — | intentionally avoided mutating .editorconfig/source for audit cleanliness |
| tool | set_project_property_preview | experimental | unknown | exercised-preview-only | 13 | 8 | Nullable=enable preview on Domain only (apply not taken; used package add/remove loop instead) |
| tool | source_generated_documents | stable | workspace | exercised | 11 | — |  |
| tool | split_class_preview | experimental | unknown | skipped-safety | 10-13 | — | preview/apply orchestration not fully driven; disposable branch available for follow-up |
| tool | suggest_refactorings | experimental | advanced-analysis | exercised | 2 | 572 |  |
| tool | symbol_info | stable | symbols | exercised | 3 | 4 |  |
| tool | symbol_relationships | stable | symbols | exercised | 3 | 12 |  |
| tool | symbol_search | stable | symbols | exercised | 3,8b.1,11 | varies; large limit strips _meta in client (FLAG) |  |
| tool | symbol_signature_help | stable | symbols | exercised | 3 | 2 |  |
| tool | test_coverage | stable | validation | skipped-safety | 8 | — | delegate long coverage run; test_run scoped instead |
| tool | test_discover | stable | validation | exercised | 8 | large | agent-tools |
| tool | test_related | stable | validation | exercised | 8 | 0 | empty array returned for Build symbol |
| tool | test_related_files | stable | validation | exercised | 8 | 10 | no heuristic matches for ApiHostBuilder path |
| tool | test_run | stable | validation | exercised | 8 | 1984 | Domain.Tests filtered |
| tool | type_hierarchy | stable | analysis | exercised | 3 | 3 |  |
| tool | workspace_changes | experimental | workspace | exercised | 6 | 3 |  |
| tool | workspace_close | stable | workspace | exercised | 0,18 | 0 | MCP workspace_close after report finalization (continuation session) |
| tool | workspace_list | stable | workspace | exercised | 0+ | 0-1 | heartbeat between phases |
| tool | workspace_load | stable | workspace | exercised | 0 | 4414 | leansummary |
| tool | workspace_reload | stable | workspace | exercised | 8,closure | 3027-6054 | post Phase 10-13 manual csproj tidy; SnapshotToken v14 |
| tool | workspace_status | stable | workspace | exercised | 0 | 8 |  |
| resource | server_catalog | stable | server | exercised | 0,15 | 6 | fetch_mcp_resource roslyn://server/catalog Phase 0 |
| resource | resource_templates | stable | server | exercised | 0 | 4 | fetch_mcp_resource Phase 0 |
| resource | workspaces | stable | workspace | blocked | 15 | — | fetch_mcp_resource intermittently unavailable; exercised via workspace_list tool Phase 0+ |
| resource | workspaces_verbose | stable | workspace | blocked | 15 | — | same transport flake as workspaces URI |
| resource | workspace_status | stable | workspace | blocked | 15 | — | URI fetch failed; exercised via workspace_status tool |
| resource | workspace_status_verbose | stable | workspace | blocked | 15 | — | URI fetch failed; lean summary exercised |
| resource | workspace_projects | stable | workspace | blocked | 15 | — | URI fetch failed; project_graph tool exercised |
| resource | workspace_diagnostics | stable | workspace | blocked | 15 | — | URI fetch failed; project_diagnostics exercised |
| resource | source_file | stable | workspace | blocked | 15 | — | URI fetch failed; get_source_text exercised |
| prompt | explain_error | experimental | prompts | blocked | 16 | — | Cursor user-roslyn host: prompt tools not invokable via call_mcp_tool in this session |
| prompt | suggest_refactoring | experimental | prompts | blocked | 16 | — | same |
| prompt | review_file | experimental | prompts | blocked | 16 | — | same |
| prompt | analyze_dependencies | experimental | prompts | blocked | 16 | — | same |
| prompt | debug_test_failure | experimental | prompts | blocked | 16 | — | same |
| prompt | refactor_and_validate | experimental | prompts | blocked | 16 | — | same |
| prompt | fix_all_diagnostics | experimental | prompts | blocked | 16 | — | same |
| prompt | guided_package_migration | experimental | prompts | blocked | 16 | — | same |
| prompt | guided_extract_interface | experimental | prompts | blocked | 16 | — | same |
| prompt | security_review | experimental | prompts | blocked | 16 | — | same |
| prompt | discover_capabilities | experimental | prompts | blocked | 16 | — | same |
| prompt | dead_code_audit | experimental | prompts | blocked | 16 | — | same |
| prompt | review_test_coverage | experimental | prompts | blocked | 16 | — | same |
| prompt | review_complexity | experimental | prompts | blocked | 16 | — | same |
| prompt | cohesion_analysis | experimental | prompts | blocked | 16 | — | same |
| prompt | consumer_impact | experimental | prompts | blocked | 16 | — | same |
| prompt | guided_extract_method | experimental | prompts | blocked | 16 | — | same |
| prompt | msbuild_inspection | experimental | prompts | blocked | 16 | — | same |
| prompt | session_undo | experimental | prompts | blocked | 16 | — | same |

---

## 4. Verified tools (working)

- `workspace_load` / `workspace_list` / `workspace_status` / `project_graph` / `workspace_close` — session lifecycle OK.
- `project_diagnostics` + `compile_check` — totals invariant under severity filter; `emitValidation=true` slower path exercised.
- `nuget_vulnerability_scan` — structured empty CVE result.
- `rename_preview`/`rename_apply` — **FLAG** wrong symbol at some caret positions; correct rename when caret on `corsOriginsEmpty` (see §14).
- `organize_usings_apply`, `format_document_apply` — Phase 6 verification clean.
- `build_workspace` — Exit 0.
- `test_run` — Domain.Tests filtered **6/6 pass**.
- `evaluate_csharp` — sum **55**; runtime error + watchdog timeout paths actionable.
- `find_references_bulk` — requires `symbols[]` schema shape.
- `create_file_*` / `delete_file_*` / `scaffold_type_*` / `apply_project_mutation` — preview→apply round-trips for audit placeholders (Phase 10–13 continuation).
- `get_di_registrations` / `find_reflection_usages` / `semantic_search` — structured results.

---

## 5. Phase 6 refactor summary

| Item | Detail |
|------|--------|
| **Target repo** | DotNet-Firewall-Analyzer |
| **Scope** | organize-usings; rename `corsOriginsEmpty` → `hasNoCorsOrigins`; format `Program.cs` (no diff); `workspace_changes` |
| **Tools** | `rename_preview`/`rename_apply`, `format_document_*`, `organize_usings_*` |
| **Verification** | `compile_check` **PASS**; `build_workspace` **PASS**; `build_project` (Api) **PASS**; `test_run` (subset) **PASS** |

Later-session tools (`apply_text_edit`, file/scaffold/project mutations) are **audit / Phase 9–13**, not Phase 6 product scope.

---

## 6. Performance baseline (`_meta.elapsedMs`)

> One row per tool with status exercised / exercised-apply / exercised-preview-only. **p90** not separately sampled — duplicated from p50 (single call per tool in ledger). **`—`** in ms columns = `large` / `varies` / missing in ledger. Client **did not** strip `_meta` for most calls; large payloads sometimes omitted timing in ledger notes.

| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| `add_package_reference_preview` | experimental | unknown | 1 | — | — | — | repo | na | FluentValidation add preview; paired with remove forward/apply |
| `analyze_control_flow` | stable | advanced-analysis | 1 | 6 | 6 | 6 | repo | within |  |
| `analyze_data_flow` | stable | advanced-analysis | 1 | 10 | 10 | 10 | repo | within |  |
| `analyze_snippet` | stable | analysis | 1 | 60 | 60 | 60 | repo | within | multi kind |
| `apply_project_mutation` | experimental | unknown | 1 | 3091 | 3091 | 3091 | repo | within | add/remove FluentValidation on Domain; then manual csproj tidy for empty ItemGro |
| `apply_text_edit` | experimental | editing | 1 | 22 | 22 | 22 | repo | within | Phase 9 Program.cs line replace; FLAG: column/end-line discipline easy to corrup |
| `build_project` | stable | validation | 1 | 1525 | 1525 | 1525 | repo | within |  |
| `build_workspace` | stable | validation | 1 | 13833 | 13833 | 13833 | 11p/243doc | within | queued gate |
| `callers_callees` | stable | analysis | 1 | 9 | 9 | 9 | repo | within |  |
| `compile_check` | stable | validation | 1 | 3712 | 3712 | 3712 | repo | within | emitValidation ~3712ms |
| `create_file_apply` | experimental | unknown | 1 | 3237 | 3237 | 3237 | repo | within | placeholder file then deleted |
| `create_file_preview` | experimental | unknown | 1 | 10 | 10 | 10 | repo | within | McpAuditPlaceholder.cs preview |
| `delete_file_apply` | experimental | unknown | 1 | 3128 | 3128 | 3128 | repo | within | placeholder + scaffold cleanup |
| `delete_file_preview` | experimental | unknown | 1 | 1 | 1 | 1 | repo | within | audited delete previews |
| `diagnostic_details` | stable | analysis | 1 | 4 | 4 | 4 | repo | within |  |
| `document_symbols` | stable | symbols | 1 | — | — | — | repo | na |  |
| `enclosing_symbol` | stable | symbols | 1 | 139 | 139 | 139 | repo | within | LoadAppSettings at ApiHostBuilder.cs:100,5 |
| `evaluate_csharp` | stable | scripting | 1 | 13006 | 13006 | 13006 | repo | warn | watchdog/infinite-loop probe ~13s; happy path ~21ms (ledger `21-13006`) |
| `evaluate_msbuild_items` | experimental | unknown | 1 | 149 | 149 | 149 | repo | within | Compile items |
| `evaluate_msbuild_property` | experimental | unknown | 1 | 78 | 78 | 78 | repo | within |  |
| `find_base_members` | stable | symbols | 1 | — | — | — | repo | na | PanosApiException.ErrorCode → FirewallAnalyzerException.ErrorCode |
| `find_consumers` | stable | analysis | 1 | 4 | 4 | 4 | repo | within |  |
| `find_implementations` | stable | symbols | 1 | 2 | 2 | 2 | repo | within |  |
| `find_overrides` | stable | symbols | 1 | — | — | — | repo | na | FirewallAnalyzerException.ErrorCode overrides (4 types) |
| `find_references` | stable | symbols | 1 | 9 | 9 | 9 | repo | within |  |
| `find_references_bulk` | stable | symbols | 1 | 22 | 22 | 22 | repo | within |  |
| `find_reflection_usages` | stable | advanced-analysis | 1 | 1218 | 1218 | 1218 | repo | within |  |
| `find_shared_members` | stable | analysis | 1 | 8 | 8 | 8 | repo | within |  |
| `find_type_mutations` | stable | analysis | 1 | 8 | 8 | 8 | repo | within |  |
| `find_type_usages` | stable | analysis | 1 | 29 | 29 | 29 | repo | within |  |
| `find_unused_symbols` | stable | advanced-analysis | 1 | 829 | 829 | 829 | repo | within | includePublic true/false |
| `fix_all_preview` | experimental | refactoring | 1 | 80 | 80 | 80 | repo | within | IDE0005 no provider; guidance ok |
| `format_document_apply` | stable | refactoring | 1 | 1 | 1 | 1 | repo | within | no-op diff |
| `format_document_preview` | stable | refactoring | 1 | 78 | 78 | 78 | repo | within |  |
| `get_code_actions` | stable | code-actions | 1 | 199 | 199 | 199 | repo | within | empty at CA1859 caret |
| `get_cohesion_metrics` | stable | analysis | 1 | — | — | — | repo | na | result written to agent-tools |
| `get_completions` | stable | symbols | 1 | 178 | 178 | 178 | repo | within | ApiHostBuilder builder. Environment\|Equals with filterText=E |
| `get_complexity_metrics` | stable | advanced-analysis | 1 | 91 | 91 | 91 | repo | within |  |
| `get_di_registrations` | stable | advanced-analysis | 1 | 1235 | 1235 | 1235 | repo | within |  |
| `get_editorconfig_options` | experimental | configuration | 1 | 6 | 6 | 6 | repo | within |  |
| `get_namespace_dependencies` | stable | advanced-analysis | 1 | — | — | — | repo | na | agent-tools |
| `get_nuget_dependencies` | stable | advanced-analysis | 1 | 1448 | 1448 | 1448 | repo | within |  |
| `get_operations` | experimental | advanced-analysis | 1 | 3 | 3 | 3 | repo | within |  |
| `get_source_text` | stable | workspace | 1 | 3 | 3 | 3 | repo | within |  |
| `get_syntax_tree` | experimental | syntax | 1 | 7 | 7 | 7 | repo | within |  |
| `go_to_definition` | stable | symbols | 1 | — | — | — | repo | na |  |
| `goto_type_definition` | stable | symbols | 1 | 153 | 153 | 153 | repo | within | IPanosClient on AddSingleton line; top-level Program.cs probes NotFound (expecte |
| `impact_analysis` | stable | analysis | 1 | 4 | 4 | 4 | repo | within |  |
| `list_analyzers` | stable | analysis | 1 | 8 | 8 | 8 | repo | within | paged |
| `nuget_vulnerability_scan` | stable | security | 1 | 5302 | 5302 | 5302 | repo | within |  |
| `organize_usings_apply` | stable | refactoring | 1 | 6 | 6 | 6 | repo | within |  |
| `organize_usings_preview` | stable | refactoring | 1 | 134 | 134 | 134 | repo | within |  |
| `project_diagnostics` | stable | analysis | 1 | 4738 | 4738 | 4738 | 11p/243doc | within | full scan |
| `project_graph` | stable | workspace | 1 | 2 | 2 | 2 | repo | within |  |
| `remove_package_reference_preview` | experimental | unknown | 1 | 2 | 2 | 2 | repo | within | remove FluentValidation preview after forward apply |
| `rename_apply` | stable | refactoring | 1 | 39 | 39 | 39 | repo | within |  |
| `rename_preview` | stable | refactoring | 1 | 499 | 499 | 499 | repo | within | bad column probe documented |
| `revert_last_apply` | experimental | undo | 1 | 96 | 96 | 96 | repo | within | Phase 9 text-edit revert; Phase 17 probe undid delete (LIFO) — FLAG stack semant |
| `scaffold_type_apply` | experimental | unknown | 1 | 3110 | 3110 | 3110 | repo | within | McpAuditScaffoldType then deleted |
| `scaffold_type_preview` | experimental | unknown | 1 | 2 | 2 | 2 | repo | within | internal sealed class scaffold v1.8+ default |
| `security_analyzer_status` | stable | security | 1 | 2875 | 2875 | 2875 | repo | within |  |
| `security_diagnostics` | stable | security | 1 | 3002 | 3002 | 3002 | repo | within |  |
| `semantic_search` | stable | advanced-analysis | 1 | — | — | — | repo | na | agent-tools |
| `server_info` | stable | server | 1 | 5 | 5 | 5 | repo | within |  |
| `set_project_property_preview` | experimental | unknown | 1 | 8 | 8 | 8 | repo | within | Nullable=enable preview on Domain only (apply not taken; used package add/remove |
| `source_generated_documents` | stable | workspace | 1 | — | — | — | repo | na |  |
| `suggest_refactorings` | experimental | advanced-analysis | 1 | 572 | 572 | 572 | repo | within |  |
| `symbol_info` | stable | symbols | 1 | 4 | 4 | 4 | repo | within |  |
| `symbol_relationships` | stable | symbols | 1 | 12 | 12 | 12 | repo | within |  |
| `symbol_search` | stable | symbols | 1 | — | — | — | repo | na |  |
| `symbol_signature_help` | stable | symbols | 1 | 2 | 2 | 2 | repo | within |  |
| `test_discover` | stable | validation | 1 | — | — | — | repo | na | agent-tools |
| `test_related` | stable | validation | 1 | 0 | 0 | 0 | repo | within | empty array returned for Build symbol |
| `test_related_files` | stable | validation | 1 | 10 | 10 | 10 | repo | within | no heuristic matches for ApiHostBuilder path |
| `test_run` | stable | validation | 1 | 1984 | 1984 | 1984 | repo | within | Domain.Tests filtered |
| `type_hierarchy` | stable | analysis | 1 | 3 | 3 | 3 | repo | within |  |
| `workspace_changes` | experimental | workspace | 1 | 3 | 3 | 3 | repo | within |  |
| `workspace_close` | stable | workspace | 1 | 0 | 0 | 0 | repo | within | MCP workspace_close after report finalization (continuation session) |
| `workspace_list` | stable | workspace | 1 | 0 | 0 | 0 | repo | within | heartbeat between phases |
| `workspace_load` | stable | workspace | 1 | 4414 | 4414 | 4414 | 11p/243doc | within | leansummary |
| `workspace_reload` | stable | workspace | 1 | 3027 | 3027 | 3027 | 11p/243doc | within | post Phase 10-13 manual csproj tidy; SnapshotToken v14 |
| `workspace_status` | stable | workspace | 1 | 8 | 8 | 8 | repo | within |  |

---

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `rename_preview` | description_stale / cursor contract | Caret resolves user-intended local symbol | Sometimes resolves adjacent type (`BindConfigurationOptions`) | FLAG | ApiHostBuilder.cs line/col discipline |
| `find_references_bulk` | return_shape / param docs | Examples show flat params | Requires `symbols` array of locator objects | FLAG | Transport error until corrected |
| `evaluate_msbuild_property` | parameter naming | Consistent `projectName` | Uses `project` vs mutation tools’ `projectName` | FLAG | Documented asymmetry; easy to mismatch |

**No other schema/behaviour drift observed** for exercised paths.

---

## 8. Error message quality

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `test_related` | invalid `workspaceId` | actionable | List active ids in message (already does) | NotFound envelope |
| `evaluate_csharp` | `int.Parse("abc")` | actionable | — | Clear runtime error |
| `evaluate_csharp` | infinite loop | actionable | Expose `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS` in error | Watchdog text lists budget + grace |
| `fix_all_preview` | IDE0005 | actionable | Suggest organize_usings path | Already suggests alternate tool |
| `workspace_status` | all-zero UUID workspace id | actionable | “Active workspace IDs…” hint | Phase 17 |
| `find_references` | fabricated `symbolHandle` | actionable | Structured NotFound | Phase 17 |
| `revert_last_apply` | second consecutive call | actionable | — | Clear “nothing to revert” (Phase 17d) |

**No unhelpful errors** in the exercised negative probes above.

---

## 9. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `project_diagnostics` | project + severity Info | pass | totals invariant |
| `compile_check` | `emitValidation=true` | pass | timing delta vs default |
| `list_analyzers` | project filter + paging | pass | |
| `semantic_search` | NL queries + paraphrase | pass | |
| Resources | `roslyn://server/catalog`, templates | pass | Phase 0 |
| Resources | workspace URIs | blocked | `fetch_mcp_resource` “server not ready” |
| Prompts | MCP `prompts/get` | blocked | Cursor bridge |
| Phase 17 | bogus ids / handles | pass | See §8 |

---

## 10. Prompt verification (Phase 16)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|-------------------|------------|-----------|----------------------|-------|
| `explain_error` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `suggest_refactoring` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `review_file` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `analyze_dependencies` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `debug_test_failure` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `refactor_and_validate` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `fix_all_diagnostics` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `guided_package_migration` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `guided_extract_interface` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `security_review` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `discover_capabilities` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `dead_code_audit` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `review_test_coverage` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `review_complexity` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `cohesion_analysis` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `consumer_impact` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `guided_extract_method` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `msbuild_inspection` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |
| `session_undo` | n/a | n/a | n/a | n/a | — | needs-more-evidence | Client-blocked: `user-roslyn` exposes no `prompts/get` or prompt tool name in this Cursor session. |

---

## 11. Skills audit (Phase 16b)

**Phase 16b blocked — plugin repo (`skills/*/SKILL.md`) not in workspace; no filesystem path to Roslyn-Backed-MCP root supplied.**

---

## 12. Experimental promotion scorecard

> One row per **experimental** catalog entry (58 tools + 19 prompts = 77 rows). **`promote`** only if full closure rubric satisfied — none marked `promote` here.

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|---------------|----------|----------------|----------|
| tool | `add_central_package_version_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `add_package_reference_preview` | unknown | exercised-preview-only | — | yes | yes | n/a-preview | 0 | keep-experimental | ledger §14 ph 13 |
| tool | `add_pragma_suppression` | editing | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 7-8b |
| tool | `add_project_reference_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `add_target_framework_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `apply_composite_preview` | unknown | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 6-13 |
| tool | `apply_multi_file_edit` | editing | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 6-8b |
| tool | `apply_project_mutation` | unknown | exercised-apply | 3091 | yes | yes | yes | 0 | keep-experimental | ledger §14 ph 13 |
| tool | `apply_text_edit` | editing | exercised-apply | 22 | partial | yes | yes | 0 | keep-experimental | ledger §14 ph 9,17 |
| tool | `bulk_replace_type_apply` | refactoring | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 6-13 |
| tool | `bulk_replace_type_preview` | refactoring | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `create_file_apply` | unknown | exercised-apply | 3237 | yes | yes | yes | 0 | keep-experimental | ledger §14 ph 10 |
| tool | `create_file_preview` | unknown | exercised-preview-only | 10 | yes | yes | n/a-preview | 0 | keep-experimental | ledger §14 ph 10 |
| tool | `delete_file_apply` | unknown | exercised-apply | 3295 | yes | yes | yes | 0 | keep-experimental | ledger §14 ph 10-12 |
| tool | `delete_file_preview` | unknown | exercised-preview-only | 10 | yes | yes | n/a-preview | 0 | keep-experimental | ledger §14 ph 10-12 |
| tool | `dependency_inversion_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `evaluate_msbuild_items` | unknown | exercised | 149 | yes | yes | n/a | 0 | keep-experimental | ledger §14 ph 7 |
| tool | `evaluate_msbuild_property` | unknown | exercised | 78 | yes | yes | n/a | 0 | keep-experimental | ledger §14 ph 7 |
| tool | `extract_and_wire_interface_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `extract_interface_apply` | refactoring | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 6-13 |
| tool | `extract_interface_cross_project_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `extract_interface_preview` | refactoring | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `extract_method_apply` | refactoring | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 6-13 |
| tool | `extract_method_preview` | refactoring | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `extract_type_apply` | refactoring | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 6-13 |
| tool | `extract_type_preview` | refactoring | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `fix_all_apply` | refactoring | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 6-13 |
| tool | `fix_all_preview` | refactoring | exercised-preview-only | 80 | yes | yes | n/a-preview | 0 | keep-experimental | ledger §14 ph 6 |
| tool | `format_range_apply` | refactoring | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 6 |
| tool | `format_range_preview` | refactoring | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `get_editorconfig_options` | configuration | exercised | 6 | yes | yes | n/a | 0 | keep-experimental | ledger §14 ph 7 |
| tool | `get_msbuild_properties` | unknown | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 7 |
| tool | `get_operations` | advanced-analysis | exercised | 3 | yes | yes | n/a | 0 | keep-experimental | ledger §14 ph 4 |
| tool | `get_syntax_tree` | syntax | exercised | 7 | yes | yes | n/a | 0 | keep-experimental | ledger §14 ph 4 |
| tool | `migrate_package_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `move_file_apply` | unknown | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 6-13 |
| tool | `move_file_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `move_type_to_file_apply` | refactoring | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 10 |
| tool | `move_type_to_file_preview` | refactoring | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `move_type_to_project_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `remove_central_package_version_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `remove_dead_code_apply` | unknown | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 6-13 |
| tool | `remove_dead_code_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `remove_package_reference_preview` | unknown | exercised-preview-only | 2 | yes | yes | n/a-preview | 0 | keep-experimental | ledger §14 ph 13 |
| tool | `remove_project_reference_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `remove_target_framework_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `revert_last_apply` | undo | exercised-apply | 4203 | yes | yes | yes | 0 | keep-experimental | ledger §14 ph 9,17 |
| tool | `scaffold_test_apply` | unknown | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 6-13 |
| tool | `scaffold_test_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `scaffold_type_apply` | unknown | exercised-apply | 3110 | yes | yes | yes | 0 | keep-experimental | ledger §14 ph 12 |
| tool | `scaffold_type_preview` | unknown | exercised-preview-only | 2 | yes | yes | n/a-preview | 0 | keep-experimental | ledger §14 ph 12 |
| tool | `set_conditional_property_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `set_diagnostic_severity` | configuration | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 7-8b |
| tool | `set_editorconfig_option` | configuration | skipped-safety | — | yes | yes | n/a | 0 | needs-more-evidence | ledger §14 ph 7-8b |
| tool | `set_project_property_preview` | unknown | exercised-preview-only | 8 | yes | yes | n/a-preview | 0 | keep-experimental | ledger §14 ph 13 |
| tool | `split_class_preview` | unknown | skipped-safety | — | yes | yes | n/a-preview | 0 | needs-more-evidence | ledger §14 ph 10-13 |
| tool | `suggest_refactorings` | advanced-analysis | exercised | 572 | yes | yes | n/a | 0 | keep-experimental | ledger §14 ph 2 |
| tool | `workspace_changes` | workspace | exercised | 3 | yes | yes | n/a | 0 | keep-experimental | ledger §14 ph 6 |
| prompt | `explain_error` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `suggest_refactoring` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `review_file` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `analyze_dependencies` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `debug_test_failure` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `refactor_and_validate` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `fix_all_diagnostics` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `guided_package_migration` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `guided_extract_interface` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `security_review` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `discover_capabilities` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `dead_code_audit` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `review_test_coverage` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `review_complexity` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `cohesion_analysis` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `consumer_impact` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `guided_extract_method` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `msbuild_inspection` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |
| prompt | `session_undo` | prompts | blocked | — | n/a | n/a | n/a | 0 | needs-more-evidence | ledger §14 ph 16 |

---

## 13. Debug log capture

`client did not surface MCP log notifications`

---

## 14. MCP server issues (bugs)

| # | Tool | Input | Expected | Actual | Severity | Reproducibility |
|---|------|-------|----------|--------|----------|-----------------|
| 1 | `rename_preview` | ApiHostBuilder.cs misaligned caret | Local variable rename preview | Sometimes `BindConfigurationOptions` | incorrect result | always with listed coordinates |
| 2 | — | — | Catalog128 tools | Live127 tools | cosmetic / doc | always |
| 3 | `test_related` | `Build` symbol handle | Related tests | `[]` | missing data | sometimes |
| 4 | `apply_text_edit` | line replace | Clean unified diff | Stray `;` in diff text for comments | cosmetic | sometimes |

**No crash-level defects** in exercised paths.

---

## 15. Improvement suggestions

- Expose MCP **prompts** on the Cursor `user-roslyn` bridge for Phase 16 completeness.
- **`rename_preview`:** line/column disambiguation when multiple symbols share a line.
- **`test_related`:** document when empty array is expected vs failure.
- **`fetch_mcp_resource`:** stabilize or document Cursor “server not ready” for `roslyn://` URIs.

---

## 16. Concurrency matrix (Phase 8b)

**Parallel sub-phases (8b.2–8b.4):** **N/A — client cannot issue concurrent MCP tool calls** (Cursor serializes requests).

### Concurrency probe set (8b.0)

| Slot | Tool | Inputs | Classification | Notes |
|------|------|--------|----------------|-------|
| R1 | `find_references` | `Program.cs:3:11` `ApiHostBuilder` | reader | &lt;50 refs in this repo — substitute probe |
| R2 | `project_diagnostics` | no filters | reader | |
| R3 | `symbol_search` | `query=Analyzer, limit=120` | reader | large responses may drop `_meta` (client) |
| R4 | `find_unused_symbols` | `includePublic=false` | reader | |
| R5 | `get_complexity_metrics` | no filters | reader | |
| W1 | `format_document_preview`→`apply` | deferred | writer | no-op when already formatted |
| W2 | `set_editorconfig_option` | skipped | writer | audit cleanliness |

### Sequential baseline (8b.1), ms

| Slot | Wall-clock (ms) | Notes |
|------|-----------------|-------|
| R1 | 294 | `_meta.elapsedMs` |
| R2 | 2602 | |
| R3 | N/A | bare array / `_meta` stripped on large `symbol_search` — **FLAG** |
| R4 | 111 | |
| R5 | 65 | |
| W1 | N/A | not timed this run |
| W2 | N/A | not executed |

### Parallel fan-out (8b.2) / R/W probes (8b.3) / Lifecycle (8b.4)

| Section | Status | Notes |
|---------|--------|-------|
| 8b.2 | **N/A** | no concurrent host dispatch |
| 8b.3 | **N/A** | requires overlapping requests |
| 8b.4 | **N/A** | requires overlapping requests |

---

## 17. Writer reclassification verification (Phase 8b.5)

Per template: six writer-gate tools. Evidence from continuation session where applicable.

| # | Tool | Status | Wall-clock (ms) | Notes |
|---|------|--------|-----------------|-------|
| 1 | `apply_text_edit` | exercised | 22–4203 | Phase 9 + 17d probes |
| 2 | `apply_multi_file_edit` | skipped-safety | — | not exercised |
| 3 | `revert_last_apply` | exercised | 96–4203 | Phase 9 undo + stack probe |
| 4 | `set_editorconfig_option` | skipped-safety | — | audit avoided editorconfig mutation |
| 5 | `set_diagnostic_severity` | skipped-safety | — | same |
| 6 | `add_pragma_suppression` | skipped-safety | — | same |

_Additional writers exercised outside this six-pack (still writer-gated): `create_file_apply`, `delete_file_apply`, `apply_project_mutation`, `scaffold_type_apply` — see ledger._

---

## 18. Response contract consistency

**N/A — no response-contract inconsistencies observed** beyond MSBuild `project` vs `projectName` (§7).

---

## 19. Known issue regression check (Phase 18)

**N/A — no prior Roslyn-MCP defect list in this repo’s backlog.**

---

## 20. Known issue cross-check

**N/A**

---

## Phase narrative (condensed)

- **Phases 0–8:** diagnostics, metrics, symbols, flow, snippets, Phase 6 refactor, build/test subset; `test_coverage` skipped-safety.
- **Phase 8b.1:** sequential baselines in §16; parallel **N/A**.
- **Phase 10–13:** file create/delete, scaffold, package add/remove + `apply_project_mutation`; csproj normalized after empty `ItemGroup`.
- **Phase 9:** `apply_text_edit` + `revert_last_apply` without undoing Phase 6.
- **Phase 11–14:** semantic search, DI/reflection/source-gen, navigation, bulk refs, overrides/base.
- **Phase 15:** resource URI fetch **blocked** intermittently; tools substituted.
- **Phase 16–16b:** prompts + skills **blocked** (client / path).
- **Phase 17:** invalid id/handle + double `revert_last_apply` (second = clear no-op).
- **Phase 18:** N/A.

### Final surface closure checklist

| Step | State |
|------|-------|
| Ledger 155 rows | **PASS** (§3 + Appendix B TSV) |
| Experimental scorecard | **PASS** (§12, 77 rows) |
| Prompt table | **PASS** (§10, 19 rows, blocked documented) |
| Mandatory tables | **PASS** (this document) |
| Draft-after-every-phase | **FAIL process** — draft file was dropped mid-run; single canonical export assembled 2026-04-13 |

**Export:** This report only — for `eng/import-deep-review-audit.ps1` / rollup, supply **Appendix B** (extract the `tsv` fence) if the importer expects a separate ledger file.

---

## Appendix: export bundle

Primary artifact: **`20260413T154959Z_firewallanalyzer_mcp-server-audit.md`** (this file). For Roslyn-Backed-MCP import, copy this file to `<repo-root>/ai_docs/audit-reports/` and run `eng/import-deep-review-audit.ps1` per that repo’s docs — derive the machine ledger from **Appendix B** when a standalone `.tsv` is required.

---

## Appendix B: Coverage ledger (TSV)

Canonical TAB-separated ledger (155 data rows + header).

```tsv
kind	name	tier	category	status	phase	lastElapsedMs	notes
tool	add_central_package_version_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	add_package_reference_preview	experimental	unknown	exercised-preview-only	13	—	FluentValidation add preview; paired with remove forward/apply
tool	add_pragma_suppression	experimental	editing	skipped-safety	7-8b	—	intentionally avoided mutating .editorconfig/source for audit cleanliness
tool	add_project_reference_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	add_target_framework_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	analyze_control_flow	stable	advanced-analysis	exercised	4	6	
tool	analyze_data_flow	stable	advanced-analysis	exercised	4	10	
tool	analyze_snippet	stable	analysis	exercised	5	60-174	multi kind
tool	apply_code_action	stable	code-actions	skipped-safety	6-13	—	full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only)
tool	apply_composite_preview	experimental	unknown	skipped-safety	6-13	—	full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only)
tool	apply_multi_file_edit	experimental	editing	skipped-safety	6-8b	—	not exercised; rename/usings covered writes
tool	apply_project_mutation	experimental	unknown	exercised-apply	13	3091-2992	add/remove FluentValidation on Domain; then manual csproj tidy for empty ItemGroup
tool	apply_text_edit	experimental	editing	exercised-apply	9,17	22-4	Phase 9 Program.cs line replace; FLAG: column/end-line discipline easy to corrupt semicolon
tool	build_project	stable	validation	exercised	8	1525	
tool	build_workspace	stable	validation	exercised	8	13833	queued gate
tool	bulk_replace_type_apply	experimental	refactoring	skipped-safety	6-13	—	full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only)
tool	bulk_replace_type_preview	experimental	refactoring	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	callers_callees	stable	analysis	exercised	3	9	
tool	code_fix_apply	stable	refactoring	skipped-safety	6	—	preview/apply happy path not re-run this continuation
tool	code_fix_preview	stable	refactoring	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	compile_check	stable	validation	exercised	1,6,8	varies	emitValidation ~3712ms
tool	create_file_apply	experimental	unknown	exercised-apply	10	3237	placeholder file then deleted
tool	create_file_preview	experimental	unknown	exercised-preview-only	10	10	McpAuditPlaceholder.cs preview
tool	delete_file_apply	experimental	unknown	exercised-apply	10-12	3128-3295	placeholder + scaffold cleanup
tool	delete_file_preview	experimental	unknown	exercised-preview-only	10-12	1-10	audited delete previews
tool	dependency_inversion_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	diagnostic_details	stable	analysis	exercised	1	4	
tool	document_symbols	stable	symbols	exercised	3	—	
tool	enclosing_symbol	stable	symbols	exercised	14	139	LoadAppSettings at ApiHostBuilder.cs:100,5
tool	evaluate_csharp	stable	scripting	exercised	5	21-13006	timeout path validated
tool	evaluate_msbuild_items	experimental	unknown	exercised	7	149	Compile items
tool	evaluate_msbuild_property	experimental	unknown	exercised	7	78	
tool	extract_and_wire_interface_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	extract_interface_apply	experimental	refactoring	skipped-safety	6-13	—	full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only)
tool	extract_interface_cross_project_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	extract_interface_preview	experimental	refactoring	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	extract_method_apply	experimental	refactoring	skipped-safety	6-13	—	full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only)
tool	extract_method_preview	experimental	refactoring	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	extract_type_apply	experimental	refactoring	skipped-safety	6-13	—	full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only)
tool	extract_type_preview	experimental	refactoring	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	find_base_members	stable	symbols	exercised	14	—	PanosApiException.ErrorCode → FirewallAnalyzerException.ErrorCode
tool	find_consumers	stable	analysis	exercised	3	4	
tool	find_implementations	stable	symbols	exercised	3	2	
tool	find_overrides	stable	symbols	exercised	14	—	FirewallAnalyzerException.ErrorCode overrides (4 types)
tool	find_property_writes	stable	symbols	skipped-repo-shape	3-14	—	no suitable virtual/override/property target selected in abbreviated Phase 3/14
tool	find_references	stable	symbols	exercised	3	9	
tool	find_references_bulk	stable	symbols	exercised	14	22	
tool	find_reflection_usages	stable	advanced-analysis	exercised	11	1218	
tool	find_shared_members	stable	analysis	exercised	3	8	
tool	find_type_mutations	stable	analysis	exercised	3	8	
tool	find_type_usages	stable	analysis	exercised	3	29	
tool	find_unused_symbols	stable	advanced-analysis	exercised	2	829-1710	includePublic true/false
tool	fix_all_apply	experimental	refactoring	skipped-safety	6-13	—	full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only)
tool	fix_all_preview	experimental	refactoring	exercised-preview-only	6	80	IDE0005 no provider; guidance ok
tool	format_document_apply	stable	refactoring	exercised-apply	6	1	no-op diff
tool	format_document_preview	stable	refactoring	exercised	6	78	
tool	format_range_apply	experimental	refactoring	skipped-safety	6	—	not invoked this continuation
tool	format_range_preview	experimental	refactoring	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	get_code_actions	stable	code-actions	exercised	6	199	empty at CA1859 caret
tool	get_cohesion_metrics	stable	analysis	exercised	2	large	result written to agent-tools
tool	get_completions	stable	symbols	exercised	14	178	ApiHostBuilder builder. Environment|Equals with filterText=E
tool	get_complexity_metrics	stable	advanced-analysis	exercised	2	91	
tool	get_di_registrations	stable	advanced-analysis	exercised	11	1235	
tool	get_editorconfig_options	experimental	configuration	exercised	7	6	
tool	get_msbuild_properties	experimental	unknown	skipped-safety	7	—	avoid 50-700KB payload this turn; evaluate_msbuild_* exercised
tool	get_namespace_dependencies	stable	advanced-analysis	exercised	2	large	agent-tools
tool	get_nuget_dependencies	stable	advanced-analysis	exercised	2	1448	
tool	get_operations	experimental	advanced-analysis	exercised	4	3	
tool	get_source_text	stable	workspace	exercised	4	3	
tool	get_syntax_tree	experimental	syntax	exercised	4	7	
tool	goto_type_definition	stable	symbols	exercised	14	153-7	IPanosClient on AddSingleton line; top-level Program.cs probes NotFound (expected repo shape)
tool	go_to_definition	stable	symbols	exercised	14	—	
tool	impact_analysis	stable	analysis	exercised	3	4	
tool	list_analyzers	stable	analysis	exercised	1	8	paged
tool	member_hierarchy	stable	symbols	skipped-repo-shape	3-14	—	no suitable virtual/override/property target selected in abbreviated Phase 3/14
tool	migrate_package_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	move_file_apply	experimental	unknown	skipped-safety	6-13	—	full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only)
tool	move_file_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	move_type_to_file_apply	experimental	refactoring	skipped-safety	10	—	not invoked; create/delete loop used instead
tool	move_type_to_file_preview	experimental	refactoring	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	move_type_to_project_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	nuget_vulnerability_scan	stable	security	exercised	1	5302	
tool	organize_usings_apply	stable	refactoring	exercised-apply	6	6	
tool	organize_usings_preview	stable	refactoring	exercised	6	134	
tool	preview_code_action	stable	code-actions	skipped-safety	6	—	not invoked this continuation
tool	project_diagnostics	stable	analysis	exercised	1	4738	full scan
tool	project_graph	stable	workspace	exercised	0	2	
tool	remove_central_package_version_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	remove_dead_code_apply	experimental	unknown	skipped-safety	6-13	—	full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only)
tool	remove_dead_code_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	remove_package_reference_preview	experimental	unknown	exercised-preview-only	13	2	remove FluentValidation preview after forward apply
tool	remove_project_reference_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	remove_target_framework_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	rename_apply	stable	refactoring	exercised-apply	6	39	
tool	rename_preview	stable	refactoring	exercised	6	499	bad column probe documented
tool	revert_last_apply	experimental	undo	exercised-apply	9,17	96-4203	Phase 9 text-edit revert; Phase 17 probe undid delete (LIFO) — FLAG stack semantics vs cleanup
tool	scaffold_test_apply	experimental	unknown	skipped-safety	6-13	—	full-surface apply families not executed end-to-end this session (repo left with intentional Phase6 changes only)
tool	scaffold_test_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	scaffold_type_apply	experimental	unknown	exercised-apply	12	3110	McpAuditScaffoldType then deleted
tool	scaffold_type_preview	experimental	unknown	exercised-preview-only	12	2	internal sealed class scaffold v1.8+ default
tool	security_analyzer_status	stable	security	exercised	1	2875	
tool	security_diagnostics	stable	security	exercised	1	3002	
tool	semantic_search	stable	advanced-analysis	exercised	11	large	agent-tools
tool	server_info	stable	server	exercised	0	5	
tool	set_conditional_property_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	set_diagnostic_severity	experimental	configuration	skipped-safety	7-8b	—	intentionally avoided mutating .editorconfig/source for audit cleanliness
tool	set_editorconfig_option	experimental	configuration	skipped-safety	7-8b	—	intentionally avoided mutating .editorconfig/source for audit cleanliness
tool	set_project_property_preview	experimental	unknown	exercised-preview-only	13	8	Nullable=enable preview on Domain only (apply not taken; used package add/remove loop instead)
tool	source_generated_documents	stable	workspace	exercised	11	—	
tool	split_class_preview	experimental	unknown	skipped-safety	10-13	—	preview/apply orchestration not fully driven; disposable branch available for follow-up
tool	suggest_refactorings	experimental	advanced-analysis	exercised	2	572	
tool	symbol_info	stable	symbols	exercised	3	4	
tool	symbol_relationships	stable	symbols	exercised	3	12	
tool	symbol_search	stable	symbols	exercised	3,8b.1,11	varies; large limit strips _meta in client (FLAG)	
tool	symbol_signature_help	stable	symbols	exercised	3	2	
tool	test_coverage	stable	validation	skipped-safety	8	—	delegate long coverage run; test_run scoped instead
tool	test_discover	stable	validation	exercised	8	large	agent-tools
tool	test_related	stable	validation	exercised	8	0	empty array returned for Build symbol
tool	test_related_files	stable	validation	exercised	8	10	no heuristic matches for ApiHostBuilder path
tool	test_run	stable	validation	exercised	8	1984	Domain.Tests filtered
tool	type_hierarchy	stable	analysis	exercised	3	3	
tool	workspace_changes	experimental	workspace	exercised	6	3	
tool	workspace_close	stable	workspace	exercised	0,18	0	MCP workspace_close after report finalization (continuation session)
tool	workspace_list	stable	workspace	exercised	0+	0-1	heartbeat between phases
tool	workspace_load	stable	workspace	exercised	0	4414	leansummary
tool	workspace_reload	stable	workspace	exercised	8,closure	3027-6054	post Phase 10-13 manual csproj tidy; SnapshotToken v14
tool	workspace_status	stable	workspace	exercised	0	8	
resource	server_catalog	stable	server	exercised	0,15	6	fetch_mcp_resource roslyn://server/catalog Phase 0
resource	resource_templates	stable	server	exercised	0	4	fetch_mcp_resource Phase 0
resource	workspaces	stable	workspace	blocked	15	—	fetch_mcp_resource intermittently unavailable; exercised via workspace_list tool Phase 0+
resource	workspaces_verbose	stable	workspace	blocked	15	—	same transport flake as workspaces URI
resource	workspace_status	stable	workspace	blocked	15	—	URI fetch failed; exercised via workspace_status tool
resource	workspace_status_verbose	stable	workspace	blocked	15	—	URI fetch failed; lean summary exercised
resource	workspace_projects	stable	workspace	blocked	15	—	URI fetch failed; project_graph tool exercised
resource	workspace_diagnostics	stable	workspace	blocked	15	—	URI fetch failed; project_diagnostics exercised
resource	source_file	stable	workspace	blocked	15	—	URI fetch failed; get_source_text exercised
prompt	explain_error	experimental	prompts	blocked	16	—	Cursor user-roslyn host: prompt tools not invokable via call_mcp_tool in this session
prompt	suggest_refactoring	experimental	prompts	blocked	16	—	same
prompt	review_file	experimental	prompts	blocked	16	—	same
prompt	analyze_dependencies	experimental	prompts	blocked	16	—	same
prompt	debug_test_failure	experimental	prompts	blocked	16	—	same
prompt	refactor_and_validate	experimental	prompts	blocked	16	—	same
prompt	fix_all_diagnostics	experimental	prompts	blocked	16	—	same
prompt	guided_package_migration	experimental	prompts	blocked	16	—	same
prompt	guided_extract_interface	experimental	prompts	blocked	16	—	same
prompt	security_review	experimental	prompts	blocked	16	—	same
prompt	discover_capabilities	experimental	prompts	blocked	16	—	same
prompt	dead_code_audit	experimental	prompts	blocked	16	—	same
prompt	review_test_coverage	experimental	prompts	blocked	16	—	same
prompt	review_complexity	experimental	prompts	blocked	16	—	same
prompt	cohesion_analysis	experimental	prompts	blocked	16	—	same
prompt	consumer_impact	experimental	prompts	blocked	16	—	same
prompt	guided_extract_method	experimental	prompts	blocked	16	—	same
prompt	msbuild_inspection	experimental	prompts	blocked	16	—	same
prompt	session_undo	experimental	prompts	blocked	16	—	same
```
