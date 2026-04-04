# MCP Server Audit Report

## Header

- **Date:** 2026-04-04
- **Audited solution:** `c:\Code-Repo\IT-Chat-Bot\ITChatBot.sln`
- **Workspace id:** `cfd6094faf5d4954b81e2ea0a0bbf7f8` (closed at end of audit)
- **Server:** `roslyn-mcp` **1.6.0+e0ca703807a8634baf0781efa5aed532a1f0e77e** (`server_info`)
- **Roslyn / .NET:** Roslyn **5.3.0.0**; runtime **.NET 10.0.5** (Windows 10.0.26200)
- **Scale:** **32** projects, **736** documents (`workspace_load`)
- **Report path note:** Audit run from IT-Chat-Bot workspace. If the canonical Roslyn-Backed-MCP repo is elsewhere, copy this file to `<Roslyn-Backed-MCP-root>/ai_docs/refactor/itchatbot_mcp-server-audit.md` for backlog review.

## Tool coverage

| Category | Status | Notes |
|----------|--------|--------|
| Server | exercised | `server_info` |
| Workspace | exercised | `workspace_load`, `workspace_list`, `workspace_status`, `workspace_reload`, `project_graph`, `workspace_close` |
| Symbols | partial | `symbol_search`, `symbol_info`, `document_symbols`, `find_references` (incl. invalid handle), `type_hierarchy`; not all Phase 3 tools (e.g. `find_consumers`, `impact_analysis`, `callers_callees`, `find_type_usages`, …) |
| Analysis | partial | `project_diagnostics`, `diagnostic_details`, `type_hierarchy`; skipped deep pass on all Phase 3 analysis tools |
| Advanced Analysis | partial | `get_complexity_metrics`, `find_unused_symbols`, `get_namespace_dependencies`, `semantic_search`; `get_cohesion_metrics` returned large output (exercised); `get_nuget_dependencies` not called |
| Consumer & Cohesion | skipped | Time — not invoked |
| Security | exercised | `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan` |
| Flow | partial | `analyze_data_flow`, `analyze_control_flow`; `get_operations`, `get_syntax_tree` not called |
| Syntax & Operations | skipped | — |
| Compilation | exercised | `compile_check` (solution + project filter) |
| Analyzer Info | partial | `list_analyzers` (first page; `hasMore: true`) |
| Snippet & Scripting | partial | `analyze_snippet` (expression + program), `evaluate_csharp` (success + runtime error); infinite-loop timeout not run |
| Refactoring (rename/format/organize) | partial | `organize_usings_preview` / `organize_usings_apply`, `format_document_preview` / `format_document_apply` (empty diff) |
| Code Actions & Fixes | skipped | — |
| Fix All | exercised (failure path) | `fix_all_preview` for `IDE0005` — no provider |
| Interface/Type extraction | skipped | Preview-only risk; not exercised |
| Type movement | skipped | — |
| Bulk refactor | skipped | — |
| Cross-project refactor | skipped | Preview-only phases not fully run |
| Orchestration | skipped | — |
| Dead code | partial | `find_unused_symbols` only |
| Text edits | skipped | — |
| File ops | skipped | Preview-only |
| Project mutation (previews) | skipped | — |
| Scaffolding (previews) | skipped | — |
| Build & Test | partial | `workspace_reload`, `test_run` (`ITChatBot.Retrieval.Tests`); `build_workspace`, `build_project`, `test_discover`, `test_related*`, `test_coverage` not run |
| EditorConfig | skipped | — |
| MSBuild evaluation | skipped | — |
| Suppression & severity | skipped | — |
| Undo | exercised | `revert_last_apply` ×2 (success + no-op) |
| Resources (MCP resources) | exercised | `roslyn://server/catalog` via resource fetch |
| Prompts | skipped | MCP prompts (`explain_error`, etc.) not invocable via this client’s tool surface in this session; catalog lists 16 experimental prompts |
| Boundary & Negative Testing | partial | Bad `workspaceId` (actionable error); bad `symbol_handle` for `find_references` |
| Regression Verification | N/A | `ai_docs/backlog.md` has **no** Roslyn MCP `AUDIT-*` rows; see **Backlog regression check** |

## Verified tools (working)

- `workspace_load` — Loaded `ITChatBot.sln`; reported 32 projects / 736 documents; surfaced `WORKSPACE_FAILURE` diagnostics for `ITChatBot.Channels.csproj` (package pruning messages).
- `workspace_list` / `workspace_status` — Session listed; status matched load metadata.
- `project_graph` — Project list consistent with load payload.
- `server_info` — Reported **56** stable + **67** experimental tools, **7** resources, **16** experimental prompts; `catalogVersion` **2026.03** (aligns with `roslyn://server/catalog` **Summary**).
- `project_diagnostics` — Returned paginated diagnostics; totals: 3 workspace + compiler/analyzer mix (`TotalErrors: 3` counts workspace failures).
- `compile_check` — **0** errors for full solution (2 CS0618 warnings); **0** errors for `ITChatBot.Retrieval` after Phase 6 apply.
- `security_diagnostics` / `security_analyzer_status` — NetAnalyzers + SecurityCodeScan present; **0** security findings.
- `nuget_vulnerability_scan` — **0** CVEs; **32** projects scanned; `ScanDurationMs` **~11091**.
- `list_analyzers` — **34** analyzers, **663** rules (paginated; first page returned).
- `diagnostic_details` — Returned help links and descriptions for CS0618 and CA1002.
- `get_complexity_metrics` — Top methods include `HandleSlackPostAsync` CC=21, `TryValidate` nesting depth 4, etc.
- `find_unused_symbols` (`includePublic: false`) — **1** high-confidence unused type (`ChatBotDbContextModelSnapshot` — EF migration artifact; expected false positive class).
- `get_namespace_dependencies` (`circularOnly: true`) — **No** circular namespace cycles.
- `symbol_search` / `symbol_info` / `document_symbols` / `type_hierarchy` / `find_references` — Coherent results for `RetrievalExecutor`.
- `analyze_data_flow` — Rich variable/capture lists for `ExecuteAsync` region.
- `analyze_snippet` — Valid expression and small program analyzed.
- `evaluate_csharp` — `Enumerable.Range(1,10).Sum()` → **55**; runtime error surfaced as structured failure.
- `organize_usings_preview` / `organize_usings_apply` — Produced diff removing unused `using ITChatBot.Adapters.Abstractions.Evidence;` from `RetrievalExecutor.cs`; apply succeeded.
- `format_document_preview` / `format_document_apply` — Preview token issued; **no** textual diff (`AppliedFiles: []` on apply).
- `test_run` — `ITChatBot.Retrieval.Tests`: **139** passed, **0** failed.
- `semantic_search` — Returned plausible `async` methods for query `"async methods returning Task<bool>"`.
- `revert_last_apply` — First call reverted **Format document** (no disk change); second call reported **no operation to revert** (clear message).
- `workspace_close` — Session closed successfully.
- `roslyn://server/catalog` — JSON inventory matches `server_info` surface counts (**123** tools, **7** resources, **16** prompts).

## Phase 6 refactor summary

- **Target repo:** `c:\Code-Repo\IT-Chat-Bot` (same as audited solution).
- **Scope:** **6e** (`organize_usings_preview` / `organize_usings_apply`) on `RetrievalExecutor.cs`. **6a** (`fix_all_preview` `IDE0005`) attempted — **not applied** (no fix provider; server directed to `organize_usings_preview`). Other 6b–6d, 6f–6i not executed in this pass.
- **Changes:**
  - `src/retrieval/Execution/RetrievalExecutor.cs` — removed unused `using ITChatBot.Adapters.Abstractions.Evidence;` (via `organize_usings_apply`).
- **Verification:**
  - `compile_check` (`project`: `ITChatBot.Retrieval`): **Success**, **0** errors.
  - `test_run` (`projectName`: `ITChatBot.Retrieval.Tests`): **139** passed, **0** failed.
- **Optional:** No commit/PR created in this session.

## MCP server issues (bugs)

### 1. Workspace diagnostics: pruning messages surfaced as `Error` / `WORKSPACE_FAILURE`

| Field | Detail |
|--------|--------|
| Tool | `workspace_load`, `workspace_status`, `project_diagnostics` |
| Input | Load `ITChatBot.sln` |
| Expected | Pruning hints from SDK should be **warning/info**-style workspace messages, or suppressed from Roslyn workspace errors if they do not block compilation. |
| Actual | Three `WORKSPACE_FAILURE` entries for `ITChatBot.Channels.csproj` (“PackageReference X will not be pruned…”) with `Severity: Error`. |
| Severity | incorrect result / error-message-quality |
| Reproducibility | always (this repo + SDK) |

### 2. `project_diagnostics` “TotalErrors” vs `compile_check` CS error count

| Field | Detail |
|--------|--------|
| Tool | `project_diagnostics` vs `compile_check` |
| Input | Same workspace |
| Expected | Clear distinction: workspace load failures vs **compiler** error count; or aligned totals when comparing tools. |
| Actual | `project_diagnostics` reports `TotalErrors: 3` (workspace failures); `compile_check` reports **0** CS errors — easy to misread as “3 CS errors” when paging diagnostics. |
| Severity | missing data / cosmetic (documentation + aggregation) |
| Reproducibility | always |

### 3. `fix_all_preview` for `IDE0005` — no provider

| Field | Detail |
|--------|--------|
| Tool | `fix_all_preview` |
| Input | `diagnosticId`: `IDE0005`, `scope`: `document`, `RetrievalExecutor.cs` |
| Expected | Either preview of unused-using removal or a single clear reason **without** suggesting a conflicting path. |
| Actual | `InvalidOperationException`: no code fix provider; message suggests `organize_usings_preview` — **actionable** and helpful. |
| Severity | not a crash — **workflow gap** between Fix All and IDE-style hidden diagnostics |
| Reproducibility | always (for this diagnostic id in this workspace) |

### 4. `find_references` with invalid `symbolHandle`

| Field | Detail |
|--------|--------|
| Tool | `find_references` |
| Input | `symbolHandle`: `invalid-handle-xyz` |
| Expected | Actionable error (bad token) or validation failure. |
| Actual | **0** references, `totalCount: 0` — indistinguishable from a genuinely unused symbol. |
| Severity | incorrect result / weak validation |
| Reproducibility | always |

### 5. `analyze_control_flow` `EndPointIsReachable` for `ExecuteAsync` body region

| Field | Detail |
|--------|--------|
| Tool | `analyze_control_flow` |
| Input | `RetrievalExecutor.cs` lines **33–91** (`ExecuteAsync`) |
| Expected | For async methods with awaits, reachability may be partial; tool should document limitation or set `Warning`. |
| Actual | `EndPointIsReachable: false` despite `StartPointIsReachable: true` and exit/return points listed — may confuse consumers comparing to `analyze_data_flow`. |
| Severity | incorrect result **or** missing caveat (needs product clarification) |
| Reproducibility | always (this method region) |

## Improvement suggestions

- **`fix_all_preview` + IDE0005:** Surface a dedicated hint in-tool: “IDE0005 may require `organize_usings_preview` / `organize_usings_apply` when no FixAll provider is registered.”
- **`find_references`:** Validate base64/handle format before returning empty success.
- **`project_diagnostics`:** Split counters: `WorkspaceFailureCount` vs `CompilerErrorCount` vs `AnalyzerWarningCount` to match mental model of `compile_check`.
- **`list_analyzers`:** Expose total pages or recommend `offset`/`limit` in description (already paginates).
- **Catalog vs MCP tool list:** Local MCP descriptors enumerate **123** tools; `server_info` reports stable/experimental split — keep client UIs aligned with `Summary` in `roslyn://server/catalog`.

## Performance observations

- `nuget_vulnerability_scan` — **32** projects, **~11 s** wall-clock (`ScanDurationMs: 11091`) — acceptable but worth noting for CI-sized solutions.
- `compile_check` (full solution) — **~70 ms** (`ElapsedMs` in one call) — fast.
- `list_analyzers` / `get_cohesion_metrics` — Large payloads; ensure clients stream or paginate cohesion output.

## Backlog regression check

| AUDIT-ID | Summary | Status |
|----------|---------|--------|
| — | `ai_docs/backlog.md` contains **no** Roslyn MCP `AUDIT-*` items (open work is `queue-*` / RR-* only). | **N/A — no MCP regression rows to re-test** |

## Known backlog cross-check

- No new Roslyn MCP issues were matched to `AUDIT-*` rows in this repo’s backlog (none listed). Full write-ups for **new** behavior are in **MCP server issues (bugs)** above.

---

*Generated by executing `ai_docs/prompts/deep-review-and-refactor.md` against the live `user-roslyn-mcp` session (representative phases; not every appendix tool invoked).*
