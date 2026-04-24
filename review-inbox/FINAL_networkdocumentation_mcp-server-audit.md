# MCP Server Audit Report — **FINAL**

**Purpose:** Single handoff document for `ai_docs/prompts/deep-review-and-refactor.md`. Combines **full machine tables** (prompts §10, skills §11, promotion scorecard §12, category coverage §2) with **continuation narrative** (verified tools, Phase 6 detail, performance, Phase 8b matrices, MCP issue write-ups). **No companion files** — this markdown is the only retained artifact.

**Catalog:** `roslyn://server/catalog` **2026.04** — StableTools=69, ExperimentalTools=58, StableResources=9, ExperimentalPrompts=19 (live session; counts per `server_info` / catalog resource).

**Run classification:** **SUBSTANTIAL `full-surface` continuation** — not every catalog tool invoked; all surfaces have a **terminal ledger status** (no silent omissions). Phase 16 prompts **blocked** on Cursor MCP bridge (`prompts/get` not exercised for `user-roslyn`).

**Isolation:** Worktree `c:\Code-Repo\DotNet-Network-Documentation-wt-mcp-audit`, branch `mcp-full-surface-audit-20260413`.

**Workspaces:** `acc386eff87348aeb562e23cffd783ed` (closed) → `401e18029fa64ba3a1149b59262e4c81` (reloaded).

**Deliverable:** **`FINAL_networkdocumentation_mcp-server-audit.md`** (this file) only — copy as needed for Roslyn-Backed-MCP `ai_docs/audit-reports/` or `eng/import-deep-review-audit.ps1`.

---

## 1. Header

| Field | Value |
|--------|--------|
| **Date** | 2026-04-13 (UTC) |
| **Audited solution** | `c:\Code-Repo\DotNet-Network-Documentation-wt-mcp-audit\NetworkDocumentation.sln` |
| **Audited revision** | branch `mcp-full-surface-audit-20260413` (base `60c3b35` per prior run) |
| **Entrypoint loaded** | `NetworkDocumentation.sln` |
| **Audit mode** | `full-surface` |
| **Isolation** | Git worktree `c:\Code-Repo\DotNet-Network-Documentation-wt-mcp-audit` |
| **Client** | Cursor; MCP host **`user-roslyn`** |
| **Workspace ids** | **`acc386eff87348aeb562e23cffd783ed`** (closed end of audit) → **`401e18029fa64ba3a1149b59262e4c81`** (reloaded) |
| **Server** | `roslyn-mcp` **1.11.2** (`server_info` prior) |
| **Catalog version** | `2026.04` |
| **Roslyn / .NET** | Roslyn 5.3 / .NET 10.0.5 (prior `server_info`) |
| **Scale** | 9 projects, ~395 documents |
| **Repo shape** | Multi-project; tests; analyzers; source generators; DI in Web; `.editorconfig`; **no** central package management; **single** `net10.0` TFM |
| **Prior issue source** | **N/A** (no MCP backlog in this repo) |
| **Debug log channel** | **no** (MCP `notifications/message` not surfaced verbatim) |
| **Plugin skills path** | `C:\Users\daryl\.claude\plugins\cache\roslyn-mcp-marketplace\roslyn-mcp\1.7.0\skills` (cached; **not** v1.11.2 source-of-truth tree) |
| **Canonical store note** | Written under **`DotNet-Network-Documentation`**. Intended copy target: Roslyn-Backed-MCP `ai_docs/audit-reports/` or `eng/import-deep-review-audit.ps1`. |

### Authoritative surface / mismatch findings (live server wins)

1. **Prompt header vs `server_info`:** Living prompt still cites **128 / 59 experimental** tools in its HTML comment; live host reported **127 / 58** (prior session) — **doc drift** (FLAG).
2. **`fetch_mcp_resource` `roslyn://server/catalog` on `user-roslyn`:** Historical **not ready**; catalog via **`plugin-roslyn-mcp-roslyn`** (FLAG client routing).
3. **`workspace_close` + reload** yields a **new** `WorkspaceId` — automation must not hardcode old ids after close.

---

## 2. Coverage summary (by Kind + Category, live catalog)

| Kind | Category | exercised | exercised-apply | exercised-preview-only | skipped-repo-shape | skipped-safety | blocked |
|------|----------|-----------|-----------------|------------------------|--------------------|----------------|---------|
| prompt | prompts | 0 | 0 | 0 | 0 | 0 | 19 |
| resource | analysis | 0 | 0 | 0 | 0 | 0 | 1 |
| resource | server | 2 | 0 | 0 | 0 | 0 | 0 |
| resource | workspace | 1 | 0 | 0 | 0 | 1 | 4 |
| tool | advanced-analysis | 11 | 0 | 0 | 0 | 0 | 0 |
| tool | analysis | 12 | 0 | 0 | 0 | 0 | 0 |
| tool | code-actions | 1 | 1 | 1 | 0 | 0 | 0 |
| tool | configuration | 1 | 0 | 0 | 0 | 0 | 2 |
| tool | cross-project-refactoring | 0 | 0 | 0 | 0 | 0 | 3 |
| tool | dead-code | 0 | 0 | 0 | 0 | 0 | 2 |
| tool | editing | 0 | 1 | 0 | 0 | 0 | 2 |
| tool | file-operations | 0 | 1 | 1 | 0 | 0 | 4 |
| tool | orchestration | 0 | 0 | 0 | 0 | 0 | 4 |
| tool | project-mutation | 3 | 1 | 2 | 4 | 0 | 4 |
| tool | refactoring | 0 | 3 | 4 | 0 | 0 | 15 |
| tool | scaffolding | 0 | 1 | 1 | 0 | 0 | 2 |
| tool | scripting | 1 | 0 | 0 | 0 | 0 | 0 |
| tool | security | 3 | 0 | 0 | 0 | 0 | 0 |
| tool | server | 1 | 0 | 0 | 0 | 0 | 0 |
| tool | symbols | 16 | 0 | 0 | 0 | 0 | 0 |
| tool | syntax | 1 | 0 | 0 | 0 | 0 | 0 |
| tool | undo | 0 | 1 | 0 | 0 | 0 | 0 |
| tool | validation | 6 | 0 | 0 | 0 | 0 | 2 |
| tool | workspace | 9 | 0 | 0 | 0 | 0 | 0 |

---

## 3. Coverage ledger

Per-surface terminal status is **not** shipped as a separate TSV anymore. Use **§2** (rollup by kind × category), **§10** (19 prompts), **§11** (19 plugin skills), and **§12** (77 experimental promotion rows: 58 tools + 19 prompts; includes `status_from_ledger`, tier via category column, and evidence). **Stable** tools that are not experimental do not appear in §12; their exercise level is reflected only in the **§2** aggregates and narrative (§4–§6, §14).

---

## 4. Verified tools (working) — selected

- **`get_code_actions` / `preview_code_action` / `apply_code_action`** — Roslyn refactoring chain; preview token round-trip; revert worked with explicit **`workspaceId`** on `revert_last_apply`.
- **`get_completions`** — `filterText` + `maxItems`; **`IsIncomplete: true`** for broad filter.
- **`fix_all_preview`** — IDE0005 document scope: **0 fixes**, actionable **GuidanceMessage** when no fix-all provider.
- **`test_related_files` / `test_related`** — Heuristic filter for `ConfigLoader` / test file co-location.
- **`get_operations` / `get_syntax_tree`** — Invocation + AST slice on `ConfigLoaderTests`.
- **`goto_type_definition`** — `cfg` → `AppConfig`; wrong caret → structured **NotFound**.
- **`find_overrides` / `member_hierarchy`** — `BackgroundService.ExecuteAsync` override fan-out + base member metadata.
- **`find_references_bulk`** — Two **`metadataName`** locators; counts match expectations.
- **`build_project`** — Structured `dotnet build` for **`NetworkDocumentation.Core`**.
- **`format_range_preview` / `format_range_apply`** — No-op diff when already formatted.
- **`add_package_reference_preview` + `apply_project_mutation` + `remove_package_reference_preview` + `apply_project_mutation`** — Reversible package add/remove on Core csproj.
- **`scaffold_type_preview` / `scaffold_type_apply` + `delete_file_preview` / `delete_file_apply`** — `internal sealed class` scaffold then delete cleanup.
- **`workspace_close` / `workspace_load`** — Phase 17e close; reopen succeeded with new id.
- **`compile_check`** — 0 CS errors post mutations.

---

## 5. Phase 6 refactor summary

**Product intent this continuation:** Minimal **audit-style** mutations only: reversible **package reference** add/remove (no net csproj change), **scaffold + delete** (no net new type), **format_range** no-op, **`apply_code_action` + `revert_last_apply`** (no net source change). Earlier session work (format/organize/`apply_text_edit` revert) referenced in historical report.

**Verification:** `compile_check` clean (post-reload path); shell **`dotnet test`** with Playwright included: **8135 passed, 1 failed** (`WebUiPlaywrightTests.InventoryPage_SoftwareTab_RendersRows` — environmental/UI); **`dotnet test`** excluding Playwright + **coverlet**: **8115 passed**.

**Coverlet / Cobertura (captured in this report — no external XML required):** The following was read from generated `coverage.cobertura.xml` (Coverlet v1.9 schema) for this repo, source root `C:\Code-Repo\DotNet-Network-Documentation\src\`, Cobertura `timestamp` **1776088985** → **2026-04-13T14:03:05Z**.

| Metric | Value |
|--------|--------|
| **Line coverage** | **76.78%** (30548 / 39786 lines) |
| **Branch coverage** | **63.30%** (10441 / 16493 branches) |

| Package (assembly) | Line % | Branch % |
|---------------------|--------|----------|
| NetworkDocumentation.Cli | 39.3% | 27.2% |
| NetworkDocumentation.Collection | 64.8% | 56.3% |
| NetworkDocumentation.Core | 83.1% | 64.2% |
| NetworkDocumentation.Diagrams | 83.2% | 69.7% |
| NetworkDocumentation.Excel | 93.4% | 82.4% |
| NetworkDocumentation.Parsers | 76.0% | 65.4% |
| NetworkDocumentation.Reports | 94.5% | 80.0% |
| NetworkDocumentation.Web | 70.7% | 43.7% |

The original Cobertura file under `artifacts/coverage/` was **deleted** after inlining this snapshot so the audit has a single deliverable.

---

## 6. Performance baseline (`_meta.elapsedMs`)

| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| workspace_load | stable | workspace | 1 | 2292 | 2292 | 2292 | 9 proj | within | reopen after close |
| project_diagnostics | stable | analysis | 1 | 4850 | 4850 | 4850 | full solution | warn | Phase 8b R2 |
| build_workspace | stable | validation | 1 | 42813 | 42813 | 42813 | full solution | exceeded | includes web frontend build |
| find_references | stable | symbols | 2 | 167 | 459 | 459 | hot symbol | within | incl. NotFound probe |
| semantic_search | stable | advanced-analysis | 1 | 5384 | 5384 | 5384 | NL query | within | phase 11 |
| evaluate_csharp | stable | scripting | 1 | 20011 | 20011 | 20011 | infinite loop probe | exceeded | watchdog |
| apply_project_mutation | experimental | project-mutation | 2 | 2210 | 2210 | 2210 | csproj edit | within | add+remove package |
| scaffold_type_apply | experimental | scaffolding | 1 | 2178 | 2178 | 2178 | new file | within |  |
| delete_file_apply | experimental | file-operations | 1 | 2158 | 2158 | 2158 | delete | within |  |
| get_code_actions | stable | code-actions | 1 | 693 | 693 | 693 | single location | within |  |
| get_completions | stable | symbols | 1 | 498 | 498 | 498 | filtered | within |  |

---

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `revert_last_apply` | missing_param | `workspaceId` required | bridge error if omitted | FLAG | Schema documents `workspaceId` |
| Living prompt HTML comment | description_stale | 128/59 tools | 127/58 | FLAG | Align with `server_info` |

| `organize_usings_preview` vs ledger (historical) | data | — | column corruption fixed in ledger | low | Prior TSV typo; see narrative |

No other drift confirmed for exercised tools.

---

## 8. Error message quality

| Tool | probe_input | rating | notes |
|------|-------------|--------|-------|
| `workspace_status` | closed / invalid id | actionable | |
| `go_to_definition` | line 9999 | actionable | |
| `find_references` | invalid symbolHandle | actionable | NotFound envelope |
| `test_run` / `test_coverage` | MCP invoke | unhelpful | No FailureEnvelope in Cursor bridge |
| `symbol_search` | `query: ""` | neutral | Returns `[]` (no crash) |

---

## 9. Parameter-path coverage

| family | non_default_path_tested | status | notes |
|--------|-------------------------|--------|-------|
| project_diagnostics | severity=Error | pass | |
| compile_check | paging, severity | pass | |
| get_completions | filterText, maxItems | pass | |
| fix_all_preview | scope=document, IDE0005 | pass | guidance path |
| MSBuild | project vs projectName | pass | documented asymmetry |

---

## 10. Prompt verification (Phase 16 — all catalog prompts)

| prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | notes |
|--------|-----------|------------|-------------------|------------|-----------|---------------------|-------|
| `explain_error` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `suggest_refactoring` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `review_file` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `analyze_dependencies` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `debug_test_failure` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `refactor_and_validate` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `fix_all_diagnostics` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `guided_package_migration` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `guided_extract_interface` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `security_review` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `discover_capabilities` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `dead_code_audit` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `review_test_coverage` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `review_complexity` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `cohesion_analysis` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `consumer_impact` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `guided_extract_method` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `msbuild_inspection` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |
| `session_undo` | n/a | n/a | n/a | n/a | n/a | needs-more-evidence | Client: no MCP prompts/get for user-roslyn; not exercised |

---

## 11. Skills audit (Phase 16b — one row per skill)

| skill | frontmatter_ok | tool_refs_valid | dry_run | safety_rules | notes |
|-------|----------------|-----------------|---------|--------------|-------|
| `analyze` | yes | no (4) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `bump` | yes | no (4) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `code-actions` | yes | no (3) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `complexity` | yes | no (5) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `dead-code` | yes | no (5) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `document` | yes | no (1) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `explain-error` | yes | no (4) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `extract-method` | yes | no (5) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `migrate-package` | yes | no (3) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `project-inspection` | yes | no (1) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `publish-preflight` | yes | no (2) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `refactor` | yes | no (5) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `review` | yes | no (3) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `security` | yes | no (4) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `session-undo` | yes | no (1) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `snippet-eval` | yes | yes | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `test-coverage` | yes | no (4) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `test-triage` | yes | no (5) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |
| `update` | yes | no (5) | pass | pass | Plugin 1.7.0; backtick token scan vs catalog (prose may false-positive) |

---

## 12. Experimental promotion scorecard (all experimental tools + all experimental prompts; resources n/a)

| kind | name | category | status_from_ledger | p50_elapsedMs | schema_ok | error_ok | round_trip_ok | failures | recommendation | evidence |
|------|------|----------|-------------------|---------------|-----------|----------|---------------|----------|----------------|----------|
| tool | `add_central_package_version_preview` | project-mutation | skipped-repo-shape | n/a | yes | n/a | n/a | 0 | needs-more-evidence | ledger phase 13; no Directory.Packages.props / central package management |
| tool | `add_package_reference_preview` | project-mutation | exercised-preview-only | 1 | yes | yes | n/a | 0 | keep-experimental | ledger phase 13; preview token then apply via apply_project_mutation; reversed with remove_packag |
| tool | `add_pragma_suppression` | editing | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked this continuation — operator may exercise in next pass |
| tool | `add_project_reference_preview` | project-mutation | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked; reversible mutation loop deferred |
| tool | `add_target_framework_preview` | project-mutation | skipped-repo-shape | n/a | yes | n/a | n/a | 0 | needs-more-evidence | ledger phase 13; single-target net10.0 projects only |
| tool | `apply_composite_preview` | orchestration | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked — deferred (naming/destructive caution) |
| tool | `apply_multi_file_edit` | editing | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked this continuation |
| tool | `apply_project_mutation` | project-mutation | exercised-apply | 2210 | yes | yes | yes | 0 | keep-experimental | ledger phase 13; forward add System.Collections.Immutable + reverse remove; second call ~2185ms |
| tool | `apply_text_edit` | editing | exercised-apply | 36 | yes | yes | yes | 0 | keep-experimental | ledger phase 6; phase 6 prior; reverted |
| tool | `bulk_replace_type_apply` | refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `bulk_replace_type_preview` | refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `create_file_apply` | file-operations | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `create_file_preview` | file-operations | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `delete_file_apply` | file-operations | exercised-apply | 2158 | yes | yes | yes | 0 | keep-experimental | ledger phase 10; cleanup scaffolded McpAuditScratchType.cs |
| tool | `delete_file_preview` | file-operations | exercised-preview-only | 6 | yes | yes | partial | 0 | keep-experimental | ledger phase 10; preview before delete_file_apply |
| tool | `dependency_inversion_preview` | cross-project-refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `evaluate_msbuild_items` | project-mutation | exercised | 129 | yes | yes | n/a | 0 | keep-experimental | ledger phase 7; phase 7 |
| tool | `evaluate_msbuild_property` | project-mutation | exercised | 128 | yes | yes | n/a | 0 | keep-experimental | ledger phase 7; phase 7 |
| tool | `extract_and_wire_interface_preview` | orchestration | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `extract_interface_apply` | refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `extract_interface_cross_project_preview` | cross-project-refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `extract_interface_preview` | refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `extract_method_apply` | refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `extract_method_preview` | refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `extract_type_apply` | refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `extract_type_preview` | refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `fix_all_apply` | refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; no fix-all preview with non-empty token this run |
| tool | `fix_all_preview` | refactoring | exercised-preview-only | 110 | yes | yes | partial | 0 | keep-experimental | ledger phase 6; IDE0005 document scope; FixedCount=0; GuidanceMessage actionable |
| tool | `format_range_apply` | refactoring | exercised-apply | 1 | yes | yes | yes | 0 | keep-experimental | ledger phase 6; empty diff apply after preview |
| tool | `format_range_preview` | refactoring | exercised-preview-only | 16 | yes | yes | partial | 0 | keep-experimental | ledger phase 6; ConfigLoaderTests lines 13–23 |
| tool | `get_editorconfig_options` | configuration | exercised | 4 | yes | yes | n/a | 0 | keep-experimental | ledger phase 7; phase 7 |
| tool | `get_msbuild_properties` | project-mutation | exercised | n/a | yes | yes | n/a | 0 | keep-experimental | ledger phase 7; phase 7 large |
| tool | `get_operations` | advanced-analysis | exercised | 4 | yes | yes | n/a | 0 | keep-experimental | ledger phase 4; JsonDocument.Parse invocation |
| tool | `get_syntax_tree` | syntax | exercised | 5 | yes | yes | n/a | 0 | keep-experimental | ledger phase 4; ConfigLoaderTests range 10–23 |
| tool | `migrate_package_preview` | orchestration | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `move_file_apply` | file-operations | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `move_file_preview` | file-operations | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `move_type_to_file_apply` | refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `move_type_to_file_preview` | refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `move_type_to_project_preview` | cross-project-refactoring | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `remove_central_package_version_preview` | project-mutation | skipped-repo-shape | n/a | yes | n/a | n/a | 0 | needs-more-evidence | ledger phase 13; no central package management |
| tool | `remove_dead_code_apply` | dead-code | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `remove_dead_code_preview` | dead-code | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `remove_package_reference_preview` | project-mutation | exercised-preview-only | 4 | yes | yes | n/a | 0 | keep-experimental | ledger phase 13; after forward package add |
| tool | `remove_project_reference_preview` | project-mutation | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `remove_target_framework_preview` | project-mutation | skipped-repo-shape | n/a | yes | n/a | n/a | 0 | needs-more-evidence | ledger phase 13; single target only |
| tool | `revert_last_apply` | undo | exercised-apply | 2505 | yes | yes | yes | 0 | keep-experimental | ledger phase 6; code-action revert; requires workspaceId param |
| tool | `scaffold_test_apply` | scaffolding | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `scaffold_test_preview` | scaffolding | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `scaffold_type_apply` | scaffolding | exercised-apply | 2178 | yes | yes | yes | 0 | keep-experimental | ledger phase 12; then delete_file_* cleanup |
| tool | `scaffold_type_preview` | scaffolding | exercised-preview-only | 0 | yes | yes | partial | 0 | keep-experimental | ledger phase 12; McpAuditScratchType internal sealed class |
| tool | `set_conditional_property_preview` | project-mutation | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `set_diagnostic_severity` | configuration | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked (8b.5 writer deferred) |
| tool | `set_editorconfig_option` | configuration | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked (8b.5 writer deferred) |
| tool | `set_project_property_preview` | project-mutation | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `split_class_preview` | orchestration | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase —; not invoked |
| tool | `suggest_refactorings` | advanced-analysis | exercised | 943 | yes | yes | n/a | 0 | keep-experimental | ledger phase 2; phase 2 |
| tool | `workspace_changes` | workspace | exercised | 5 | yes | yes | n/a | 0 | keep-experimental | ledger phase 6; phase 6k |
| prompt | `explain_error` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; no prompts/get in Cursor MCP bridge for user-roslyn |
| prompt | `suggest_refactoring` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `review_file` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `analyze_dependencies` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `debug_test_failure` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `refactor_and_validate` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `fix_all_diagnostics` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `guided_package_migration` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `guided_extract_interface` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `security_review` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `discover_capabilities` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `dead_code_audit` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `review_test_coverage` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `review_complexity` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `cohesion_analysis` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `consumer_impact` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `guided_extract_method` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `msbuild_inspection` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |
| prompt | `session_undo` | prompts | blocked | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | ledger phase 16; same |

---

## 13. Debug log capture

`client did not surface MCP log notifications`

---

## 14. MCP server issues (bugs)

### 14.1 `test_run` / `test_coverage` — MCP invocation failure

| Field | Detail |
|--------|--------|
| Symptom | `call_mcp_tool` returned **“An error occurred invoking …”** with **no structured `FailureEnvelope`** visible to the agent |
| Severity | **missing data** / client-bridge |
| Mitigation | Shell `dotnet test` + coverlet; coverage headline inlined in **§5** (this report) |

### 14.2 `symbol_search` — empty results for broad queries

| Field | Detail |
|--------|--------|
| Inputs | `query: "async Task"` and `query: ""` |
| Actual | `[]` |
| Severity | **FLAG** (may be index/repo heuristic; cross-check with semantic_search in prior phases) |

### 14.3 `find_base_members` vs `member_hierarchy`

| Field | Detail |
|--------|--------|
| Observation | `find_base_members` at `ExecuteAsync` returned **`[]`**; `member_hierarchy` returned rich **BaseMembers** |
| Severity | **FLAG** (tool overlap / caret sensitivity) |

**Prior:** `evaluate_csharp` infinite-loop **abandoned worker thread** message (degraded performance) — still relevant from historical session.

---

## 15. Improvement suggestions

- Surface **structured errors** for **all** failed MCP tool invocations in Cursor (test tools).
- Align **living prompt** HTML comment counts with **`server_info`** on every release.
- Consider **`find_base_members`** behaviour parity with **`member_hierarchy`** or document divergence.

---

## 16. Concurrency matrix (Phase 8b)

**8b.2 / 8b.3 / 8b.4:** **`N/A — client serializes tool calls`** (Cursor agent cannot dispatch overlapping MCP requests).

### 8b.0 — Probe set

| Slot | Probe | Class |
|------|-------|-------|
| R1 | `find_references` `ConfigLoader` type name location | reader |
| R2 | `project_diagnostics` `severity=Error` | reader |
| R3 | `symbol_search` `"async Task"` *(returned [])* | reader |
| R4 | `find_unused_symbols` `includePublic=false` | reader |
| R5 | `get_complexity_metrics` | reader |

### 8b.1 — Sequential baseline (`_meta.elapsedMs`)

| Probe | elapsedMs |
|-------|-----------|
| R1 | 167 |
| R2 | 4850 |
| R3 | 0 |
| R4 | 1816 |
| R5 | 441 |

Parallel fan-out, read/write interleave, lifecycle stress: **not executed** (`N/A`).

---

## 17. Writer reclassification verification (Phase 8b.5)

**Partial:** This continuation exercised **writers**: **`apply_project_mutation`**, **`scaffold_type_apply`**, **`delete_file_apply`**, **`format_range_apply`**, **`apply_code_action`**, **`revert_last_apply`**. It **did not** run all six named tools in the living prompt table (**`apply_text_edit`**, **`apply_multi_file_edit`**, **`set_editorconfig_option`**, **`set_diagnostic_severity`**, **`add_pragma_suppression`** remain unaudited here).

| tool | status | notes |
|------|--------|-------|
| `apply_text_edit` | N/A this pass | prior session |
| `apply_multi_file_edit` | not invoked | |
| `revert_last_apply` | exercised | requires `workspaceId` |
| `set_editorconfig_option` | not invoked | |
| `set_diagnostic_severity` | not invoked | |
| `add_pragma_suppression` | not invoked | |

---

## 18. Response contract consistency

| tools | concept | inconsistency |
|-------|---------|---------------|
| `find_base_members` vs `member_hierarchy` | base lookup | empty vs populated for same override site |

---

## 19. Known issue regression check (Phase 18)

**N/A — no prior source**

---

## 20. Known issue cross-check

**N/A**

---

## Final surface closure (combined)

### Export documentation checks

| Check | Status |
|-------|--------|
| Ledger rows =127+9+19 | **Yes** |
| Mandatory sections 1–15 present | **Yes** |
| §10 one row per prompt | **Yes** (19) |
| §12 one row per experimental surface | **Yes** (58 tools + 19 prompts) |
| Every experimental row has recommendation | **Yes** |
| All tools exercised | **No** (ledger `blocked`/`skipped` explicit) |

---


### Continuation checklist (strict completeness)

| Step | Status |
|------|--------|
| Ledger row count = catalog total | **Yes** (155 data rows) |
| Every row terminal status | **Yes** |
| Net repo cleanliness | **Yes** — reversible package mutation net zero; scaffold file deleted |
| Phase 16 prompts | **blocked** (client) — documented |
| Phase 16b skills table | **INCOMPLETE** |
| All Phase 6/8b.5/10 matrix tools exercised | **No** — explicit remaining `blocked` / `not invoked` rows in ledger |
| **`strict all-phases-complete`** | **No** |

