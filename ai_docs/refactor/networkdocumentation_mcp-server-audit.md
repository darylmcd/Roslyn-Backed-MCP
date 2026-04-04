# MCP Server Audit Report

**Report path note:** Saved under `DotNet-Network-Documentation` per prompt fallback (Roslyn-Backed-MCP repo not this workspace). Intended for maintainers: copy alongside server releases or merge into `ai_docs/reports/mcp-server-audit-report.md` if consolidating.

## Header

- **Date:** 2026-04-04
- **Audited solution:** `c:\Code-Repo\DotNet-Network-Documentation\NetworkDocumentation.sln`
- **Workspace id:** `36d9530937e843179b537c247be70096` (session closed at end of audit)
- **Server:** `roslyn-mcp` **1.6.0+e0ca703807a8634baf0781efa5aed532a1f0e77e` (from `server_info`)
- **Roslyn / .NET:** Roslyn **5.3.0.0**; runtime **.NET 10.0.5**; OS Windows 10.0.26200
- **Scale:** **8** projects, **363** documents (`workspace_load` / `workspace_reload` after edits)
- **Catalog:** `2026.03` — `server_info.surface`: **56** stable + **67** experimental tools (**123** total), **7** stable resources, **16** experimental prompts (`roslyn://server/catalog` **Summary** matches)

## Tool coverage

| Category | Status | Notes |
|----------|--------|--------|
| Server | exercised | `server_info` |
| Workspace | exercised | `workspace_load`, `workspace_list`, `workspace_status`, `workspace_reload`, `project_graph`, `workspace_close` |
| Symbols | partial | `symbol_search`, `symbol_info`, `document_symbols`, `find_references`, `find_consumers`, `type_hierarchy`, `impact_analysis`; not all Phase 3 tools (e.g. `callers_callees`, `member_hierarchy`, `find_shared_members` on same type) invoked for brevity |
| Analysis | exercised | `project_diagnostics`, `compile_check` (with/without `emitValidation`), `find_type_mutations`, `find_type_usages`, `diagnostic_details` N/A (zero diagnostics) |
| Advanced Analysis | exercised | `get_complexity_metrics`, `get_cohesion_metrics`, `find_unused_symbols` (×2), `get_namespace_dependencies`, `get_nuget_dependencies`, `semantic_search` |
| Consumer & Cohesion | exercised | `find_consumers`, `get_cohesion_metrics` |
| Security | exercised | `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan` |
| Flow | exercised | `analyze_data_flow`, `analyze_control_flow` on `ToDeviceInfoRow` region; `get_operations` / `get_syntax_tree` not called (time scope) |
| Syntax & Operations | skipped | `get_syntax_tree`, `get_operations` — not run this pass |
| Compilation | exercised | `compile_check` |
| Analyzer Info | exercised | `list_analyzers` (paginated; `hasMore` true at limit 50) |
| Snippet & Scripting | exercised | `analyze_snippet` (expression), `evaluate_csharp` (sum + runtime error); infinite-loop timeout not exercised |
| Refactoring (rename/format/organize) | exercised | `format_document_preview`/`apply`, `organize_usings_preview`/`apply` |
| Code Actions & Fixes | skipped | `get_code_actions` / `code_fix_*` — no diagnostics to target |
| Fix All | skipped | No diagnostic IDs to batch-fix |
| Interface/Type extraction | skipped | Preview-only risk; not required for clean solution |
| Type movement | skipped | Phase 10 previews not fully run |
| Bulk refactor | skipped | — |
| Cross-project refactor | skipped | Preview-only in prompt |
| Orchestration | skipped | — |
| Dead code | partial | `find_unused_symbols` only; `remove_dead_code_*` not applied |
| Text edits | skipped | — |
| File ops | skipped | Preview-only phases deferred |
| Project mutation (previews) | skipped | — |
| Scaffolding (previews) | skipped | — |
| Build & Test | exercised | `build_workspace`, `test_discover`, `test_run` (filtered + MCP), local `dotnet build` / `dotnet test` |
| EditorConfig | skipped | — |
| MSBuild evaluation | skipped | — |
| Suppression & severity | skipped | — |
| Undo | exercised | `revert_last_apply` after audit-only `format_document_apply` |
| Resources (MCP resources) | exercised | `roslyn://server/catalog` via `fetch_mcp_resource` |
| Prompts | skipped | MCP prompt templates not invoked via prompt API this pass |
| Boundary & Negative Testing | partial | Bad `workspaceId` → actionable error; empty `symbol_search` → `[]`; stale token / double revert not exercised |
| Regression Verification | partial | Compared against `ai_docs/reports/mcp-server-audit-report.md` (2026-04-03) |

## Verified tools (working)

- `workspace_load` — 8 projects, 363 documents, no workspace diagnostics
- `workspace_list` / `workspace_status` — consistent with load
- `server_info` — `workspaceCount: 1` matched `workspace_list` after load (prior report noted mismatch **sometimes**)
- `project_graph` — aligned with solution structure
- `project_diagnostics` / `compile_check` — 0 issues; `emitValidation=true` ~2.1s, no extra findings
- `security_*` / `nuget_vulnerability_scan` — structured output; 0 CVEs (transitive not included per response)
- `list_analyzers` — 33 analyzers, 832 rules total; pagination works
- `get_complexity_metrics` / `get_cohesion_metrics` — plausible metrics for this solution
- `find_unused_symbols` — results include confidence; test types flagged with `includePublic=true` expected noise
- `get_namespace_dependencies` / `get_nuget_dependencies` — large structured output
- `symbol_search` / `symbol_info` / `document_symbols` / `type_hierarchy` — coherent for `NetworkInventory`
- `find_references` — 166 total for type; classifications present
- `find_consumers` — 48 consumers summarized
- `find_type_mutations` — **2** mutating members (`AddDevice`, `SetDeviceRegionAndArea`) with external callers (see regression)
- `find_type_usages` — string bucket keys (e.g. `methodParameter`) in this build
- `impact_analysis` — large structured output (file spill in agent log)
- `analyze_data_flow` — structured variable roles for `ToDeviceInfoRow` region
- `analyze_snippet` / `evaluate_csharp` — expression valid; `Sum()` → 55; runtime parse error surfaced as message
- `organize_usings_apply` — removed unused `using NetworkDocumentation.Core.CiscoEol;` from `PipelineOrchestrator.cs`
- `format_document_preview`/`apply` — applied formatting to `PipelineOrchestrator.cs` for undo test
- `revert_last_apply` — reverted format; reported operation name and timestamp
- `workspace_reload` — snapshot advanced (version 6 after edits)
- `build_workspace` — exit 0, 0 warnings/errors
- `test_discover` / `test_run` — structured TRX path; filtered run 11 passed
- `fetch_mcp_resource` `roslyn://server/catalog` — JSON **Summary** matches tool/resource/prompt counts
- `workspace_close` — success

## Phase 6 refactor summary

- **Target repo:** DotNet-Network-Documentation (this repo)
- **Scope:** **6e** — `organize_usings_preview` / `organize_usings_apply` on `PipelineOrchestrator.cs` (removed unused import). **6e** — `format_document_preview` / `format_document_apply` used only for **Phase 9** undo verification; **reverted** via `revert_last_apply`. Sub-steps 6a–6d, 6f–6i not run (no diagnostics / scope).
- **Changes:**
  - `PipelineOrchestrator.cs` — dropped unused `using NetworkDocumentation.Core.CiscoEol;` (Roslyn organize usings)
- **MCP tools:** `organize_usings_preview` → `organize_usings_apply`; `format_document_preview` → `format_document_apply` → `revert_last_apply` (undoes format only)
- **Verification:** `compile_check` Success after organize; after revert Success; `build_workspace` Success; MCP `test_run` (Cli-related filter) 11/11 passed; local `dotnet build` + `dotnet test --filter "Category!=Playwright"` **8041 passed**, 9 skipped (known graph/Excel skips), 0 failed
- **Optional commit / PR:** not created unless requested

## MCP server issues (bugs)

### 1. `semantic_search` — signature-style query returns empty

| Field | Detail |
|--------|--------|
| Tool | `semantic_search` |
| Input | `query`: `"async methods returning Task<bool>"`, `limit`: 8 |
| Expected | Either ranked matches or explicit “no semantic matches” guidance |
| Actual | `count`: 0, empty `results` (matches prior `ai_docs/reports/mcp-server-audit-report.md` finding #5) |
| Severity | Missing data / weak relevance for natural-language signature queries |
| Reproducibility | Always in this solution for this query style |

### 2. `analyze_control_flow` — `EndPointIsReachable` false for method region ending in `return`

| Field | Detail |
|--------|--------|
| Tool | `analyze_control_flow` |
| Input | `InventoryProjection.cs` lines 114–170 (`ToDeviceInfoRow` body) |
| Expected | End of region reachable if return is valid exit |
| Actual | `EndPointIsReachable`: **false** despite `StartPointIsReachable`: true and a return at line 123 |
| Severity | Incorrect or misleading (may be definition of “end point” vs exit nodes) |
| Reproducibility | Always for this range |

**No new crashes**, **no malformed JSON**, **no server hang** observed this pass.

## Improvement suggestions

- `semantic_search` — Document supported query shapes; return a warning when the query looks like a C# signature only (prior repo notes in `ai_docs/reports/mcp-server-audit-report.md`).
- `analyze_control_flow` — Clarify in schema/description what “end point” means vs `return` exits, or align `EndPointIsReachable` with intuitive “normal completion” of a region.
- `nuget_vulnerability_scan` — Response notes `IncludesTransitive: false`; document how to opt into transitive if/when supported.
- `list_analyzers` — Expose a way to request total count without paging (or document pagination contract), when agents need rule totals only.

## Performance observations

- `compile_check` with `emitValidation: true` ~**2135 ms** vs ~**123 ms** without — consistent with schema note (expected).
- `nuget_vulnerability_scan` ~**6 s** for 8 projects — acceptable; note in report.
- `get_namespace_dependencies` large payload (~70KB agent log) — acceptable for full solution.
- Remaining tools in this pass under **~5 s** for single operations.

## Backlog regression check

Cross-reference: prior point-in-time findings in `ai_docs/reports/mcp-server-audit-report.md` (2026-04-03). This repo’s `ai_docs/backlog.md` has **no** AUDIT-* MCP rows.

| Prior finding | Summary | Status (2026-04-04) |
|---------------|---------|---------------------|
| Prior #1 `server_info.workspaceCount` vs `workspace_list` | Count mismatch | **candidate for closure** — `server_info` showed `workspaceCount: 1` matching one session |
| Prior #2 `find_type_mutations` / `NetworkInventory` | 0 mutations | **candidate for closure** — now reports **2** mutating members with callers |
| Prior #3 `find_type_usages` classifications | Numeric vs string | **improved** — `usagesByClassification` uses string keys and per-entry `Classification` strings |
| Prior #4 `get_complexity_metrics` `MaxNestingDepth` | 0 for deep methods | **still reproduces** for some methods — e.g. `ApplyFromJson` CC 21 reports `MaxNestingDepth` **1**; `ToDeviceInfoRow` CC 18 reports **1** (not 0 in this sample — still verify metric definition vs nested `?:` / LINQ) |
| Prior #5 `semantic_search` | Task<bool> query | **still reproduces** — 0 results |

## Known backlog cross-check

- No `AUDIT-xx` rows in `ai_docs/backlog.md` for Roslyn MCP. Prior consolidated report: `ai_docs/reports/mcp-server-audit-report.md`.
- **New observation:** None beyond sections above; `find_type_mutations` behavior **no longer matches** prior #2 — treat as **server fix or environment delta**, not duplicate backlog row here.

---

## Completion

- Single audit file written: `ai_docs/refactor/networkdocumentation_mcp-server-audit.md`
- Workspace session closed: `workspace_close` on audited id after phases
