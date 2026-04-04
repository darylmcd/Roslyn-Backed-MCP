# MCP Server Audit Report

**Report path note:** This workspace is **DotNet-Firewall-Analyzer** (not the Roslyn-Backed-MCP repo). *Intended final path for server-repo backlog review:* `<Roslyn-Backed-MCP-root>/ai_docs/refactor/firewallanalyzer_mcp-server-audit.md` — copy this file there if that repo maintains canonical audit artifacts.

## Header

- **Date:** 2026-04-04
- **Audited solution:** `c:\Code-Repo\DotNet-Firewall-Analyzer\FirewallAnalyzer.slnx`
- **Workspace id:** `328380a5719d401ea19b3f3a549d1a3c` (session closed at end of audit)
- **Server:** `roslyn-mcp` **1.6.0+e0ca703807a8634baf0781efa5aed532a1f0e77e** (from `server_info`)
- **Roslyn / .NET:** Roslyn **5.3.0.0**, runtime **.NET 10.0.5**, OS Windows 10.0.26200
- **Scale:** **11** projects, **230** documents (from `workspace_load` / `workspace_status`)
- **Catalog (resource `roslyn://server/catalog`):** **123** tools (56 stable + 67 experimental), **7** stable resources, **16** experimental prompts (stable prompt count **0** in catalog summary)

## Tool coverage

| Category | Status | Notes |
|----------|--------|--------|
| Server | exercised | `server_info` |
| Workspace | exercised | `workspace_load`, `workspace_list`, `workspace_status`, `workspace_reload`, `project_graph`, `workspace_close` |
| Symbols | partial | `symbol_search`, `symbol_info`, `document_symbols`, `find_references`, `find_consumers` (not all navigation tools) |
| Analysis | partial | `type_hierarchy`, `project_diagnostics`, `compile_check` (incl. emitValidation); skipped `diagnostic_details` (no diagnostics) |
| Advanced Analysis | partial | `get_complexity_metrics`, `get_cohesion_metrics`, `find_unused_symbols`, `get_namespace_dependencies`, `get_nuget_dependencies`, `semantic_search`, `get_di_registrations`; skipped `find_reflection_usages`, full `semantic_search` cross-checks |
| Consumer & Cohesion | partial | `find_consumers`, `get_cohesion_metrics`; skipped `find_shared_members`, `find_type_mutations`, `find_type_usages`, `impact_analysis`, `callers_callees`, etc. |
| Security | exercised | `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan` |
| Flow | partial | `analyze_data_flow`, `analyze_control_flow`; skipped `get_operations`, `get_syntax_tree` |
| Syntax & Operations | skipped | — |
| Compilation | exercised | `compile_check` |
| Analyzer Info | partial | `list_analyzers` (first page); not all pages enumerated |
| Snippet & Scripting | partial | `analyze_snippet` (expression/program/error), `evaluate_csharp` (sum + runtime error); skipped **infinite-loop timeout** test (risk of long hang) |
| Refactoring (rename/format/organize) | partial | `format_document_preview` / `format_document_apply`, `organize_usings_preview` (empty changes) |
| Code Actions & Fixes | skipped | — |
| Fix All | skipped | No solution diagnostics to target |
| Interface/Type extraction | skipped | Preview-only policy / not needed for clean tree |
| Type movement | partial | `move_type_to_file_preview` only (Phase 10) |
| Bulk refactor | skipped | — |
| Cross-project refactor | skipped | Preview-only (not exercised in this run) |
| Orchestration | skipped | Preview-only |
| Dead code | skipped | `find_unused_symbols` with `includePublic=false` returned **0** hits |
| Text edits | exercised | `apply_text_edit` (temporary marker; see Phase 6) |
| File ops | partial | `move_type_to_file_preview` only |
| Project mutation (previews) | skipped | — |
| Scaffolding (previews) | skipped | — |
| Build & Test | exercised | `build_workspace`, `test_run` (filter `Category!=E2E`) |
| EditorConfig | skipped | — |
| MSBuild evaluation | skipped | — |
| Suppression & severity | skipped | — |
| Undo | exercised | `revert_last_apply` (with `workspaceId`) |
| Resources (MCP resources) | exercised | `roslyn://server/catalog` |
| Prompts | skipped | MCP **prompts** are catalogued but not invoked via this client’s tool surface in this session |
| Boundary & Negative Testing | partial | Invalid `workspaceId` on `workspace_status`; skipped fabricated handle / stale token matrix |
| Regression Verification | N/A | See **Backlog regression check** |

## Verified tools (working)

- `workspace_load` — Loaded `FirewallAnalyzer.slnx`; 11 projects, 230 documents; `WorkspaceDiagnostics` empty.
- `workspace_list` / `workspace_status` — Session visible; no stale flag.
- `project_graph` — Matches dependency expectations (Domain ← Application ← Infrastructure; Api references all three).
- `server_info` — Returns version, Roslyn, runtime, surface counts (`stable`/`experimental` split).
- `project_diagnostics` — **0** errors/warnings/info for full solution (limit 500).
- `compile_check` — **0** errors; **~21 ms** without emit; **~1547 ms** with `emitValidation: true` (schema notes slower path).
- `security_diagnostics` / `security_analyzer_status` — No findings; NetAnalyzers + SecurityCodeScan present.
- `nuget_vulnerability_scan` — **0** CVEs, 11 projects scanned, **~4167 ms**; `IncludesTransitive: false` noted.
- `list_analyzers` — **25** analyzer assemblies, **492** total rules (paginated; `hasMore: true` with limit 200).
- `get_complexity_metrics` — Returns plausible CC/nesting (e.g. `CollectUnknownYamlKeyMessages` CC 20, nesting 5).
- `get_cohesion_metrics` — High LCOM4 on test classes (expected); cohesive types like `JobTracker` LCOM4 **1**.
- `find_unused_symbols` — `includePublic=false` → **0**; `includePublic=true` → many **expected false positives** (middleware `InvokeAsync`, test types, DTOs) with confidence labels.
- `get_namespace_dependencies` — Large graph returned (output file).
- `get_nuget_dependencies` — Lists packages (central-managed versions).
- `symbol_search` / `symbol_info` / `document_symbols` — Consistent handles and member outline for `TrafficSimulator`.
- `find_references` — **18** refs for `TrafficSimulator` type; classifications and previews coherent.
- `find_consumers` — **3** consumer types for `TrafficSimulator`; summary string correct.
- `type_hierarchy` — Static class: no bases/derived (nulls) — expected.
- `analyze_data_flow` — Lists parameters, locals, reads/writes for `Simulate` body.
- `analyze_control_flow` — Reports entry reachability and return sites (see **issues** for endpoint reachability nuance).
- `analyze_snippet` — Expression/program valid; broken code returns CS diagnostics.
- `evaluate_csharp` — `Enumerable.Range(1,10).Sum()` → **55**; `int.Parse("abc")` → graceful runtime error string.
- `format_document_preview` / `format_document_apply` — Empty `Changes` for already-formatted file; apply reported success.
- `build_workspace` — **Exit 0**, 0 warnings/errors in MSBuild output.
- `test_run` — **Exit 0**; per-assembly passes logged (see **issues** for aggregate counts).
- `semantic_search` — Query “async methods returning Task<bool>” returned `TestConnectivityAsync` (plausible).
- `get_di_registrations` — **24** registrations with file/line/method (includes test projects).
- `apply_text_edit` — Applied single-line change with unified diff in response.
- `move_type_to_file_preview` — Valid multi-file diff for `PanoramaSettingsView` (preview only).
- `revert_last_apply` — With **`workspaceId`**, reverted last **Roslyn** apply (`Format document 'TrafficSimulator.cs'`).
- Resource `roslyn://server/catalog` — JSON inventory matches `server_info` surface summary (123 tools, 7 resources, 16 prompts).

## Phase 6 refactor summary

- **Target repo:** DotNet-Firewall-Analyzer (`FirewallAnalyzer.slnx`)
- **Scope:** **6e** partially — `format_document_preview`/`format_document_apply` on `TrafficSimulator.cs` (no textual changes). **6h** — `apply_text_edit` appended a temporary comment on `HasAny` for tool verification; **reverted in working tree** (not left in product code). **6a–6d, 6f, 6f-ii, 6g, 6i** not run — **no diagnostics** to fix-all/code-fix; no dead-code removal; no rename/extract.
- **Changes:** None intended to remain; `TrafficSimulator.cs` required a **manual fix** after `revert_last_apply` left a duplicate `HasAny` declaration when combined with prior `apply_text_edit` + disk edit (see issue **#6**).
- **Verification:** `dotnet build FirewallAnalyzer.slnx` — **PASS** after duplicate line removed. `build_workspace` — **PASS** during session before the conflict.
- **Optional commit / PR:** None (audit-only).

## MCP server issues (bugs)

### 1. `server_info.workspaceCount` lag vs `workspace_list`

| Field | Detail |
|--------|--------|
| Tool | `server_info` |
| Input | (none) immediately after `workspace_load` |
| Expected | `workspaceCount` reflects at least one loaded session |
| Actual | `workspaceCount`: **0** while `workspace_list` showed **1** session |
| Severity | cosmetic / documentation (schema already warns of brief lag) |
| Reproducibility | sometimes |

### 2. `test_run` aggregate totals vs per-assembly stdout

| Field | Detail |
|--------|--------|
| Tool | `test_run` |
| Input | `filter`: `Category!=E2E`, full solution |
| Expected | Structured `Total`/`Passed` reflect **all** assemblies’ tests |
| Actual | JSON showed e.g. `Total`: **126**, `Passed`: **126** while stdout listed **6+110+6+49+126** passed across assemblies |
| Severity | incorrect result or misleading aggregation for multi-project runs |
| Reproducibility | always (this run) |

### 3. `analyze_control_flow` — `EndPointIsReachable` for method-body range

| Field | Detail |
|--------|--------|
| Tool | `analyze_control_flow` |
| Input | `TrafficSimulator.Simulate` lines **13–55** |
| Expected | End of region interpretable as “method exit” or documented |
| Actual | `EndPointIsReachable`: **false** despite two return paths listed |
| Severity | cosmetic / semantic ambiguity (may be “selection end” vs “exit node”) |
| Reproducibility | always (this selection) |

### 4. `revert_last_apply` — parameter required; empty stack error from client

| Field | Detail |
|--------|--------|
| Tool | `revert_last_apply` |
| Input | Omitted `workspaceId` (first call) |
| Expected | Clear JSON error listing required `workspaceId` |
| Actual | Client reported generic invocation error (tool **does** document `workspaceId` as required) |
| Severity | error-message-quality (caller-side clarity) |
| Reproducibility | always if args omitted |

### 5. `apply_text_edit` not revertible by `revert_last_apply`

| Field | Detail |
|--------|--------|
| Tool | `revert_last_apply` / `apply_text_edit` |
| Input | Apply text edit, then `revert_last_apply` |
| Expected | Documented: only Roslyn preview/apply stack — **actual behavior matches schema** |
| Actual | Revert reverted prior **format** apply, not text edit (per schema: disk-direct not revertible) |
| Severity | cosmetic (documentation is correct; worth highlighting in audit) |
| Reproducibility | always |

### 6. Workspace vs disk inconsistency after `apply_text_edit` + `revert_last_apply`

| Field | Detail |
|--------|--------|
| Tools | `apply_text_edit`, manual file fix, `revert_last_apply` |
| Input | Text edit on `HasAny`; manual removal of audit comment on disk; then `revert_last_apply` (Roslyn format undo) |
| Expected | File remains syntactically valid |
| Actual | `TrafficSimulator.cs` contained **duplicate** `HasAny` method declaration lines (CS1525). Required manual deletion of the extra line to restore build. |
| Severity | **incorrect result** / state corruption risk when mixing **experimental text edits** with **Roslyn undo** and out-of-band disk edits |
| Reproducibility | sometimes (ordering-dependent) |

**No new crash or data-loss observed** beyond the above line-duplication incident (recovered by manual edit).

## Improvement suggestions

- **`test_run`:** Return per-assembly breakdown in JSON **or** document that `Total`/`Passed` reflect the last TRX aggregation / final assembly only.
- **`server_info`:** Consider bumping `workspaceCount` synchronously after `workspace_load` success **or** always recommending `workspace_list` first (schema already hints).
- **`nuget_vulnerability_scan`:** Surface `IncludesTransitive: false` prominently in tool description so clients do not assume full transitive CVE coverage.
- **Catalog vs prompts:** `server_info` reports `prompts.stable: 0`, `experimental: 16`; align naming with “all prompts are experimental tier” in consumer UIs.
- **`list_analyzers`:** Default limit 100 with 492 rules — consumers need pagination discipline (documented in schema).

## Performance observations

- `compile_check` with `emitValidation: true`: **~1547 ms** vs **~21 ms** without — **expected** per schema.
- `nuget_vulnerability_scan`: **~4167 ms** for 11 projects — acceptable; note for CI-style budgets.
- `list_analyzers` (200 rules): large payload — acceptable.
- All other exercised tools completed in under **5 s** for single operations.

## Backlog regression check

| AUDIT-ID | Summary | Status |
|----------|---------|--------|
| — | `ai_docs/backlog.md` in this repo has **no** open Roslyn-MCP / AUDIT rows | **N/A** |

No Roslyn MCP backlog items in **this** repository to re-test. Cross-check against **Roslyn-Backed-MCP** `ai_docs/backlog.md` when auditing that repo.

## Known backlog cross-check

- None in **DotNet-Firewall-Analyzer** backlog matching new server defects (this file’s issues are **new observations** for the MCP product team).

---

## Appendix: session tool call index (abbrev.)

Phases covered: **0–2** full; **3** partial (key type `TrafficSimulator`); **4** partial (data/control flow on `Simulate`); **5** partial (snippet/script, no infinite loop); **6** minimal (format + text edit probe); **8** build + test; **10** one cross-file preview; **9** `revert_last_apply` with `workspaceId`; **11** partial; **15** catalog resource; **16–17** partial/negative (invalid workspace); **workspace_close** at end.
