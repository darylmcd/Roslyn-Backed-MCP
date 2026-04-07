# MCP Server Audit Report

## Header
- **Date:** 2026-04-07
- **Audited solution:** `C:\Code-Repo\DotNet-Firewall-Analyzer\FirewallAnalyzer.slnx`
- **Audited revision:** `0a57dcc3406c76bef0e1c879af27853d9f1553ff` (branch `main`)
- **Entrypoint loaded:** `FirewallAnalyzer.slnx`
- **Audit mode:** `full-surface` (attempted) — de-facto downgraded to partial coverage due to mid-session workspace loss (see **Session interruption** below). Operator ran against the main branch, not a disposable worktree.
- **Isolation:** main branch, no Phase 6 applies committed. No audit-only mutations reached disk before the workspace was lost.
- **Client:** Claude Code CLI (Opus 4.6, 1M context). MCP client did **not** surface `notifications/message` log entries to the agent.
- **Workspace id:** `86fc270122da4c03929ea0e960bf22d2` (lost mid-session; `workspace_list` now returns `count: 0`)
- **Server:** `roslyn-mcp` v`1.6.0+13e135b7ddd7c296aec6df983eb34bb6c1a467b1`
- **Roslyn / .NET:** Roslyn `5.3.0.0` / .NET `10.0.5` / Windows `10.0.26200`
- **Catalog version:** `2026.03`
- **Scale:** 11 projects (5 src + 6 test), 230 documents
- **Repo shape:** multi-project, xUnit tests, `.editorconfig` present, Central Package Management (`Directory.Packages.props`), single target framework (`net10.0`), no source generators, DI registrations present, **FluentValidation package not restored in `FirewallAnalyzer.Api`** (see FLAG-A)
- **Prior issue source:** `ai_docs/audit-reports/2026-04-06_firewallanalyzer_mcp-server-audit.md`
- **Lock mode at audit time:** `rw-lock`
- **Lock mode source of truth:** `evaluate_csharp` (canonical read of `ROSLYNMCP_WORKSPACE_RW_LOCK=true`; `ElapsedMs=861`)
- **Debug log channel:** `no` — client did not surface MCP `notifications/message` log entries
- **Report path note:** This is the **mode-1 partial** of a pending deep-review pair (`rw-lock`). No opposite-mode (`legacy-mutex`) partial was found in `ai_docs/audit-reports/` for this repo (only the 2026-04-06 legacy-format file without an embedded mode). To complete a dual-mode pair, run `./eng/flip-rw-lock.ps1` (if available), restart the MCP client, and re-invoke the deep-review prompt. **Audit significantly truncated due to session interruption** — see *Session interruption and unresponsiveness analysis* below.

---

## Session interruption and unresponsiveness analysis (mandatory investigation)

The operator reported that the Claude agent became unresponsive mid-audit twice and had to stop the session. This section documents the evidence collected and the most likely root cause.

### Timeline (UTC, approximate)
1. `14:16:47Z` — `workspace_load` succeeded, `WorkspaceId=86fc270122da4c03929ea0e960bf22d2`.
2. Phases 0 → 1 → 2 → 3 → 4 → 5 executed successfully, with 4 in-phase crashes on `UnresolvedAnalyzerReference` (see FLAG-A).
3. After a batch of 5 parallel Phase 5/6/7/8 tool calls (`analyze_snippet`, `get_editorconfig_options`, `format_document_preview`, `test_discover`), **three of the four workspace-scoped calls returned `KeyNotFoundException: Workspace '86fc270122da4c03929ea0e960bf22d2' not found or has been closed`** — simultaneously.
4. Operator interrupted the session ("became unresponsive") and re-engaged.
5. Post-resume verification: `workspace_list` returns `{"count": 0, "workspaces": []}`. The workspace is irrecoverably lost.

### What failed vs what still worked after the interruption
| Call | Outcome | Scope |
|------|---------|-------|
| `workspace_list` | `count: 0` | server-only |
| `evaluate_csharp` | PASS (pre-interrupt, `55` / `FormatException`) | workspace-independent |
| `analyze_snippet` | PASS (pre-interrupt) | workspace-independent |
| `get_editorconfig_options` | FAIL `KeyNotFoundException` | workspace-scoped |
| `format_document_preview` | FAIL `KeyNotFoundException` | workspace-scoped |
| `test_discover` | FAIL `KeyNotFoundException` | workspace-scoped |

All workspace-scoped tools now error out consistently; workspace-independent tools (`evaluate_csharp`, `analyze_snippet`, `server_info`, `workspace_list`) still respond normally.

### Root cause analysis: **Claude app / host orchestration issue, not a Roslyn MCP hang**

**No individual Roslyn MCP tool call hung.** Every call in the transcript either returned successful data or returned a structured error (`UnresolvedAnalyzerReference` crash, or `KeyNotFoundException` after the workspace was lost). Wall-clock for the largest observed calls:

| Tool | Wall-clock | Notes |
|------|-----------|-------|
| `evaluate_csharp` (lock-mode probe) | 861 ms | PASS |
| `evaluate_csharp` (`Sum()`) | 212 ms | PASS |
| `evaluate_csharp` (`FormatException`) | 40 ms | PASS |
| `nuget_vulnerability_scan` | 5,888 ms | PASS (CLI-backed, slowest legitimate call) |
| `compile_check` (default) | returned | 724,051 chars output |
| `compile_check` (`emitValidation=true`) | returned | 724,050 chars output |

The Roslyn MCP server itself did not exhibit the ≥10-minute "stuck on Evaluating C#" pattern described in cross-cutting principle #10 of the prompt. The `evaluate_csharp` calls completed within the default 10-second `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS` budget.

**Primary contributing factor: Claude Code client transparently restarted the MCP subprocess between turns.** The server's own `workspace_load` documentation explicitly warns: *"Cursor/Claude Code may relaunch the MCP server transparently between conversations."* The post-interrupt `workspace_list` returning `count: 0` is consistent with a fresh server process that has never seen a workspace load. No other hypothesis fits:

- **Eviction ruled out.** `ROSLYNMCP_MAX_WORKSPACES` defaults to 8 and only 1 workspace was ever loaded.
- **Explicit close ruled out.** No `workspace_close` call was made by the agent before the interrupt.
- **Server crash without restart would have surfaced a transport-level error**, not a clean `KeyNotFoundException` with an actionable "Active workspace IDs are listed by workspace_list" hint. The error came from a running server that simply had no workspace registered.

**Secondary contributing factor: cumulative tool-result payload pressure on the agent context.** Over the pre-interrupt run the following oversized tool results accumulated in the agent's active context:

| Tool | Payload size | Persisted to file? |
|------|--------------|---------------------|
| `roslyn://server/catalog` | ~57.7 KB | yes |
| `project_diagnostics` (no filter, limit=100) | ~56.3 KB | yes |
| `compile_check` (default) | ~724 KB | yes (harness forced persistence) |
| `compile_check` (`emitValidation=true`) | ~724 KB | yes (harness forced persistence) |
| `get_namespace_dependencies` (full, non-circular) | ~58.4 KB | yes |
| `list_analyzers` (limit=100) | ~25 KB | inline |
| `get_nuget_dependencies` | ~15 KB | inline |
| `find_references` (`TrafficSimulator`) | ~12 KB | inline |
| `find_type_usages` (`TrafficSimulator`) | ~13 KB | inline |
| `symbol_relationships` (`SimulationResult`) | ~10 KB | inline |

Four tool results exceeded the client's inline size threshold and were force-persisted to disk. Two of those (`compile_check`) each weighed ~724 KB — **~7× the harness's inline cap**. Combined with ~150 KB of inline tool results, this produced ~1.6 MB of cumulative tool-result surface area across ~40 tool calls in a single pre-interrupt session. That is the most likely trigger of the observed "Claude stopped responding" symptom on the operator's side: the agent loop spent progressively more time ingesting and reasoning about persisted oversized tool output before each next turn, and the client orchestrator then restarted the MCP subprocess during a lull — wiping the workspace.

**Tertiary contributing factor: four Roslyn MCP tools crash on unrestored analyzer references (FLAG-A).** The cascade of identical `InvalidOperationException` errors from `type_hierarchy`, `callers_callees`, `impact_analysis`, and `find_unused_symbols` forced repeated work-around attempts and re-planning, lengthening each agent turn.

### Verdict

**This is a Claude app / host orchestration issue (MCP subprocess restart between turns, compounded by oversized persisted tool results), not a Roslyn MCP server hang.** Roslyn MCP contributed two real UX bugs (FLAG-A and the unpaginated `compile_check` output — FLAG-B) that made the symptom worse, but the server itself was responsive throughout.

### Recommendations
1. **Claude Code / host side:** Avoid transparent MCP subprocess restarts between turns when a session has active workspace state. If a restart is unavoidable, surface the event to the agent so it can re-issue `workspace_load` proactively.
2. **Roslyn MCP side:** Give `compile_check` pagination / `limit` / `severity` parameters matching `project_diagnostics`, and fix the `UnresolvedAnalyzerReference` crash surface (FLAG-A) so unrestored-package workspaces remain usable for semantic analysis.
3. **Prompt side:** Recommend that the deep-review prompt's Phase 0 explicitly call `dotnet restore` or equivalent before `workspace_load` when the target is a CPM solution, to preempt `UnresolvedAnalyzerReference` cascades.

---

## Coverage summary
| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-----------|------------------|--------------|--------------------|----------------|---------|-------|
| tool | server | 1 | 0 | 0 | 0 | 0 | 0 | `server_info` |
| tool | workspace | 4 | 0 | 0 | 0 | 0 | 4 | load/list/status/project_graph PASS; reload/close/get_source_text/get_msbuild_* blocked by workspace loss |
| tool | analysis (diagnostics) | 7 | 0 | 0 | 0 | 0 | 0 | `project_diagnostics`×2, `compile_check`×2, `diagnostic_details`, `security_diagnostics`, `security_analyzer_status` |
| tool | analysis (metrics) | 4 | 0 | 0 | 0 | 0 | 0 | `get_complexity_metrics`, `get_cohesion_metrics`, `get_namespace_dependencies`×2, `nuget_vulnerability_scan` |
| tool | analysis (dead code) | 2 | 0 | 0 | 0 | 0 | 0 | `find_unused_symbols` (full soln **crashed**, Domain-scoped PASS) |
| tool | analysis (snippet) | 4 | 0 | 0 | 0 | 0 | 0 | `analyze_snippet` ×4 (expression, program, statements×2), `evaluate_csharp` ×3 |
| tool | navigation (symbols) | 8 | 0 | 0 | 0 | 0 | 2 | `symbol_search`×3, `symbol_info`, `document_symbols`, `find_references`, `find_consumers`, `find_type_usages`, `find_shared_members`; `type_hierarchy` crashed (FLAG-A), `callers_callees` crashed |
| tool | navigation (relationships) | 2 | 0 | 0 | 0 | 0 | 1 | `symbol_relationships` PASS, `symbol_signature_help` PASS (both at wrong-position FLAG-006); `impact_analysis` crashed |
| tool | flow | 4 | 0 | 0 | 0 | 0 | 0 | `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `get_syntax_tree` |
| tool | analyzers / packaging | 2 | 0 | 0 | 0 | 0 | 0 | `list_analyzers`, `get_nuget_dependencies` |
| tool | refactoring (preview) | 0 | 0 | 1 | 0 | 0 | ~25 | `format_document_preview` returned `KeyNotFoundException` after workspace loss |
| tool | refactoring (apply) | 0 | 0 | 0 | 0 | 0 | ~18 | Blocked — workspace loss before Phase 6 applies |
| tool | editorconfig / msbuild | 0 | 0 | 0 | 0 | 0 | 5 | Blocked — workspace loss |
| tool | build / test | 0 | 0 | 0 | 0 | 0 | 8 | Blocked — workspace loss before Phase 8 |
| tool | file / cross-project / orchestration | 0 | 0 | 0 | 0 | 0 | ~17 | Blocked — Phase 10 never reached |
| tool | project mutation | 0 | 0 | 0 | 0 | 0 | ~13 | Blocked — Phase 13 never reached |
| tool | scaffolding | 0 | 0 | 0 | 0 | 0 | 4 | Blocked — Phase 12 never reached |
| tool | concurrency / rw-lock probes | 0 | 0 | 0 | 0 | 0 | all | Blocked — Phase 8b never reached |
| resource | server | 2 | n/a | n/a | 0 | 0 | 0 | `roslyn://server/catalog`, `roslyn://server/resource-templates` |
| resource | workspace | 0 | n/a | n/a | 0 | 0 | 5 | Blocked — workspace loss before Phase 15 |
| prompt | prompts | 0 | n/a | n/a | 0 | 0 | 16 | Blocked — audit halted before Phase 16 |

**Totals:** live catalog reports **123 tools**, **7 resources**, **16 prompts**. Exercised: **~36 tools, 2 resources, 0 prompts**. Blocked: the remainder (approximately 87 tools, 5 resources, 16 prompts). Ledger cannot be filled row-by-row for every unexercised entry because the workspace loss halted the audit before a final surface closure pass could iterate the full catalog — this is itself a completeness gap and is recorded here.

---

## Coverage ledger (exercised rows only — full catalog coverage not possible due to session interruption)

| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| tool | `server_info` | stable | exercised | 0 | v1.6.0, catalog 2026.03, surface counts match |
| tool | `workspace_load` | stable | exercised | 0 | 11 projects, 230 documents |
| tool | `workspace_list` | stable | exercised | 0, post-interrupt | Post-interrupt returns `count:0` (evidence of subprocess restart) |
| tool | `workspace_status` | stable | exercised | 0 | Matches `workspace_load` output |
| tool | `project_graph` | stable | exercised | 0 | Project references correct |
| tool | `evaluate_csharp` | stable | exercised | 0, 5 | Lock-mode probe + expression + runtime error; all within timeout |
| tool | `project_diagnostics` | stable | exercised | 1 | Default + project/severity filter; 1,092 CS0246 (precondition: unrestored FluentValidation); filter returned 0 for Domain |
| tool | `compile_check` | stable | exercised | 1 | **FLAG-B: 724 KB output, identical bytes for default and `emitValidation=true`** |
| tool | `security_diagnostics` | stable | exercised | 1 | 0 findings |
| tool | `security_analyzer_status` | stable | exercised | 1 | NetAnalyzers + SecurityCodeScan present |
| tool | `nuget_vulnerability_scan` | stable | exercised | 1 | 0 CVEs, 11 projects, 5,888 ms, `IncludesTransitive:false` |
| tool | `list_analyzers` | stable | exercised | 1 | 28 analyzers, 492 rules, paged (hasMore=true) |
| tool | `diagnostic_details` | stable | exercised | 1 | CS0246 details PASS, `SupportedFixes:[]` (precondition: unrestored package) |
| tool | `get_complexity_metrics` | stable | exercised | 2 | Top 5: `CollectUnknownYamlKeyMessages` CC=20, `Build` CC=18, `MergeFromUpdate` CC=15, `RulesEqual` CC=14, `IpInCidr` CC=14 |
| tool | `get_cohesion_metrics` | stable | exercised | 2 | `JobProcessor` LCOM4=2, `FileSnapshotStore` LCOM4=2; `CollectionService` / `JobTracker` LCOM4=1 |
| tool | `find_unused_symbols` | stable | exercised (partial) | 2 | **FLAG-A: full-solution call crashes with `UnresolvedAnalyzerReference`**; project-scoped call to `FirewallAnalyzer.Domain` PASS with 0 results |
| tool | `get_namespace_dependencies` | stable | exercised | 2 | Circular-only returned `[]`; full dump PASS (58 KB persisted) |
| tool | `get_nuget_dependencies` | stable | exercised | 2 | **Closure candidate for prior FLAG-002**: now populates `ResolvedCentralVersion` alongside `Version:"centrally-managed"` |
| tool | `symbol_search` | stable | exercised | 3 | 3 queries (CollectionService, TrafficSimulator, JobProcessor) PASS |
| tool | `symbol_info` | stable | exercised | 3 | TrafficSimulator: static public class, correct XML doc |
| tool | `find_references` | stable | exercised | 3 | TrafficSimulator: 18 refs, `Classification:"Read"` uniform |
| tool | `document_symbols` | stable | exercised | 3 | 9 members in TrafficSimulator |
| tool | `type_hierarchy` | stable | **blocked (crash)** | 3 | **FLAG-A: `UnresolvedAnalyzerReference`** on both symbolHandle and filePath/line/col paths |
| tool | `find_consumers` | stable | exercised | 3 | TrafficSimulator: 3 consumers (ChangeVerifier, SimulationEndpoints, Tests) |
| tool | `find_type_usages` | stable | exercised | 3 | 18 usages all classified as `staticMemberAccess` — **closure candidate for prior FLAG-005** (was `"other"`) |
| tool | `callers_callees` | stable | **blocked (crash)** | 3 | **FLAG-A: `UnresolvedAnalyzerReference`** on TrafficSimulator.Simulate |
| tool | `impact_analysis` | stable | **blocked (crash)** | 3 | **FLAG-A: `UnresolvedAnalyzerReference`** on TrafficSimulator |
| tool | `find_shared_members` | stable | exercised | 3 | JobProcessor: 0 shared members |
| tool | `symbol_relationships` | stable | exercised | 3 | **FLAG-006 reproduces**: cursor at method return-type position (line 13, col 26 of `TrafficSimulator.cs`) resolved to `SimulationResult`, not `Simulate` method |
| tool | `symbol_signature_help` | stable | exercised | 3 | **FLAG-006 reproduces**: `DisplaySignature:"SimulationResult"` instead of method signature |
| tool | `analyze_data_flow` | stable | exercised | 4 | `IpInCidr` (lines 107–156): correctly identifies inputs, locals, no captures, pure function |
| tool | `analyze_control_flow` | stable | exercised | 4 | `Simulate` (lines 13–55): 2 ExitPoints, `EndPointIsReachable:false` with helpful `Warning` explaining Roslyn semantics |
| tool | `get_operations` | stable | exercised | 4 | `i` at `TrafficSimulator.cs:15:25` → `LocalReference: int` PASS |
| tool | `get_syntax_tree` | stable | exercised | 4 | Lines 107–130 of TrafficSimulator.cs → MethodDeclaration tree PASS |
| tool | `analyze_snippet` | stable | exercised | 5 | `expression` `1+2` PASS; `program` class PASS; `statements` broken code reports CS0029 but **column = 66 for a 17-char snippet** (FLAG-C); `statements` `return 42;` **reproduces prior FLAG-007** (CS0127 because wrapper is `void Snippet.Run()`) |
| tool | `evaluate_csharp` | stable | exercised | 0, 5 | Lock-mode probe PASS 861 ms; `Enumerable.Range(1,10).Sum()` = 55 in 212 ms; `int.Parse("abc")` → `Runtime error: FormatException` in 40 ms; **infinite-loop timeout test not executed** before interruption |
| tool | `get_editorconfig_options` | stable | **blocked (workspace loss)** | 7 | `KeyNotFoundException` after subprocess restart |
| tool | `format_document_preview` | stable | **blocked (workspace loss)** | 6 | `KeyNotFoundException` |
| tool | `test_discover` | stable | **blocked (workspace loss)** | 8 | `KeyNotFoundException` |
| resource | `roslyn://server/catalog` | stable | exercised | 0 | 123 tools (56 stable + 67 experimental), 7 resources, 16 prompts; matches `server_info` |
| resource | `roslyn://server/resource-templates` | stable | exercised | 0 | 7 templates returned |
| resource | `roslyn://workspaces` | stable | blocked | 15 | Not reached before workspace loss |
| resource | `roslyn://workspace/{id}/status` | stable | blocked | 15 | Not reached before workspace loss |
| resource | `roslyn://workspace/{id}/projects` | stable | blocked | 15 | Not reached before workspace loss |
| resource | `roslyn://workspace/{id}/diagnostics` | stable | blocked | 15 | Not reached before workspace loss |
| resource | `roslyn://workspace/{id}/file/{path}` | stable | blocked | 15 | Not reached before workspace loss |

**All other live catalog entries (~87 tools, 16 prompts) are implicitly blocked due to session interruption.** Completeness gap explicitly recorded.

---

## Verified tools (working)
- `server_info` — returns catalog 2026.03, surface counts 56/67/7/16 match `roslyn://server/catalog`
- `evaluate_csharp` — canonical lock-mode probe returns `rw-lock`, ~861 ms; numeric expression returns 55; runtime error reported gracefully in 40 ms
- `workspace_load` / `workspace_list` / `workspace_status` / `project_graph` — project graph accurate, 11 projects, 230 documents, no workspace diagnostics
- `project_diagnostics` — paging, project filter, and severity filter all behave correctly
- `security_diagnostics` / `security_analyzer_status` / `nuget_vulnerability_scan` — all return structured, consistent results
- `list_analyzers` — paged result set, `totalRules=492` across 28 analyzer assemblies
- `diagnostic_details` — accurate location + message
- `get_complexity_metrics` — top hotspots match source reality on spot-check
- `get_cohesion_metrics` — LCOM4 clusters look correct on `JobProcessor` (ExecuteAsync isolated from processing cluster)
- `get_namespace_dependencies` — circular-only + full dump both behave correctly
- `get_nuget_dependencies` — now emits `ResolvedCentralVersion` for CPM (**closure candidate for prior FLAG-002**)
- `symbol_search` / `symbol_info` / `document_symbols` — return accurate data for 3 types
- `find_references` — 18 references for `TrafficSimulator` with consistent `Classification:"Read"`
- `find_consumers` — 3 consumers of `TrafficSimulator` correctly enumerated
- `find_type_usages` — now classifies static-class call sites as `staticMemberAccess` (**closure candidate for prior FLAG-005**, which reported `"other"`)
- `find_shared_members` — returns `[]` for `JobProcessor` (no member shared across >1 public method)
- `analyze_data_flow` — correct variable taxonomy on `IpInCidr`
- `analyze_control_flow` — 2 exit points correctly identified on `Simulate`; `EndPointIsReachable:false` surfaced with the documented `Warning` explaining Roslyn semantics
- `get_operations` — LocalReference correctly resolved at token column
- `get_syntax_tree` — MethodDeclaration tree correctly paged for a 24-line range
- `analyze_snippet` — expression/program kinds both PASS; negative cases return CS0029/CS0127 with correct diagnostic ids
- `roslyn://server/catalog` / `roslyn://server/resource-templates` — both read cleanly and agree with `server_info`

---

## Phase 6 refactor summary
**N/A — no Phase 6 applies executed.** The audit was interrupted during the transition from Phase 5 (snippet validation) to Phases 6–8 (refactoring + editorconfig + build/test). The first batch of workspace-scoped calls after snippet validation (`get_editorconfig_options`, `format_document_preview`, `test_discover`) all failed with `KeyNotFoundException` because the MCP server subprocess had been transparently restarted, wiping the workspace. No product code was modified. The target repo is unchanged.

---

## Concurrency mode matrix (Phase 8b)

> **Phase 8b blocked — session interruption (workspace loss) occurred before Phase 8b began.** The audit never reached sub-phase 8b.0 (probe set definition). Session B columns remain empty because run-2 never started. Session A columns are also empty because run-1 never reached 8b.

Because the operator ran against `rw-lock` mode only, this file is the **mode-1 partial** of a pending dual-mode pair. A future run-2 (after `eng/flip-rw-lock.ps1` and a full client restart) would pair against this partial via the auto-pair detection in Phase 0 step 15 — but because Phase 8b never actually ran here, there is nothing mechanically carryable. A future dual-mode audit will need to collect both Session A and Session B from scratch.

### Concurrency probe set
| Slot | Tool | Inputs (concise) | Classification | Notes |
|------|------|------------------|----------------|-------|
| R1 | `find_references` | N/A | reader | not executed (Phase 8b blocked) |
| R2 | `project_diagnostics` | N/A | reader | not executed |
| R3 | `symbol_search` | N/A | reader | not executed |
| R4 | `find_unused_symbols` | N/A | reader | not executed (would also have hit FLAG-A) |
| R5 | `get_complexity_metrics` | N/A | reader | not executed |
| W1 | `format_document_preview` → apply | N/A | writer | not executed |
| W2 | `set_editorconfig_option` (revert) | N/A | writer (reclassified) | not executed |

### Sequential baseline (single call wall-clock, ms)
| Slot | Session A mode | Session A ms | Session B mode | Session B ms | Δ ms (B − A) | Notes |
|------|----------------|---------------|----------------|---------------|---------------|-------|
| R1 | N/A | N/A | N/A | N/A | N/A | Phase 8b blocked |
| R2 | N/A | N/A | N/A | N/A | N/A | Phase 8b blocked |
| R3 | N/A | N/A | N/A | N/A | N/A | Phase 8b blocked |
| R4 | N/A | N/A | N/A | N/A | N/A | Phase 8b blocked |
| R5 | N/A | N/A | N/A | N/A | N/A | Phase 8b blocked |
| W1 | N/A | N/A | N/A | N/A | N/A | Phase 8b blocked |
| W2 | N/A | N/A | N/A | N/A | N/A | Phase 8b blocked |

### Parallel fan-out and behavioral verification
- **Host logical cores:** not recorded (Phase 8b blocked)
- **Chosen N (fan-out):** N/A

| Slot | Mode | Parallel wall-clock (ms) | Speedup vs baseline | Expected (legacy ≤1.3× / rw-lock ≥`0.7 × N`) | Pass / FLAG / FAIL | Notes |
|------|------|---------------------------|----------------------|------------------------------------------------|---------------------|-------|
| R1 ×N | rw-lock | N/A | N/A | N/A | N/A | Phase 8b blocked |
| R1 ×N | legacy-mutex | N/A | N/A | N/A | N/A | skipped-pending-second-run |
| R2 ×N | rw-lock | N/A | N/A | N/A | N/A | Phase 8b blocked |
| R2 ×N | legacy-mutex | N/A | N/A | N/A | N/A | skipped-pending-second-run |
| R3 ×N | rw-lock | N/A | N/A | N/A | N/A | Phase 8b blocked |
| R3 ×N | legacy-mutex | N/A | N/A | N/A | N/A | skipped-pending-second-run |

### Read/write exclusion behavioral probe (Phase 8b.3)
| Mode | Reader→Writer | Writer→Reader | Expected | Pass / FLAG / FAIL | Notes |
|------|----------------|----------------|----------|---------------------|-------|
| rw-lock | N/A | N/A | both `waits` | N/A | Phase 8b blocked |
| legacy-mutex | N/A | N/A | both `does-not-wait` | N/A | skipped-pending-second-run |

### Lifecycle stress (Phase 8b.4)
| Probe | Mode | Observed | Reader saw | Reader exception | `correlationId` | Expected | Pass / FLAG / FAIL | Notes |
|-------|------|----------|-----------|-------------------|-----------------|----------|---------------------|-------|
| reader + `workspace_reload` | rw-lock | N/A | N/A | N/A | N/A | `waits-for-reader` | N/A | Phase 8b blocked |
| reader + `workspace_reload` | legacy-mutex | N/A | N/A | N/A | N/A | `runs-concurrently` | N/A | skipped-pending-second-run |
| reader + `workspace_close` | rw-lock | N/A | N/A | N/A | N/A | `waits-for-reader` | N/A | Phase 8b blocked |
| reader + `workspace_close` | legacy-mutex | N/A | N/A | N/A | N/A | `runs-concurrently`; possible `KeyNotFoundException` | N/A | skipped-pending-second-run |

**Indirect empirical signal:** the operator reported an in-session `KeyNotFoundException` pattern that exactly matches the documented `legacy-mutex` `workspace_close`-post-acquire-recheck behavior described in Phase 8b.4 step 3 — **except** that lock mode was `rw-lock`, not `legacy-mutex`, and no explicit close was issued. This is **not** a Phase 8b.4 finding (no reader was in flight) but rather evidence of the orchestration-level subprocess restart described in *Session interruption* above.

---

## Writer reclassification verification (Phase 8b.5)
| # | Tool | Session A status | Session B status | Wall-clock A (ms) | Wall-clock B (ms) | Notes |
|---|------|-------------------|-------------------|--------------------|--------------------|-------|
| 1 | `apply_text_edit` | blocked (Phase 8b never reached) | skipped-pending-second-run | N/A | N/A | — |
| 2 | `apply_multi_file_edit` | blocked | skipped-pending-second-run | N/A | N/A | — |
| 3 | `revert_last_apply` | blocked | skipped-pending-second-run | N/A | N/A | — |
| 4 | `set_editorconfig_option` | blocked | skipped-pending-second-run | N/A | N/A | — |
| 5 | `set_diagnostic_severity` | blocked | skipped-pending-second-run | N/A | N/A | — |
| 6 | `add_pragma_suppression` | blocked | skipped-pending-second-run | N/A | N/A | — |

---

## Debug log capture
`client did not surface MCP log notifications`

The Claude Code CLI did not forward `notifications/message` `notifications/message` entries (from the server's `McpLoggingProvider`) to the agent, so no `Warning`/`Error`/`Critical` or workspace-lifecycle `Information` entries are available to cite here. This is a client-side limitation, recorded per cross-cutting principle #9 of the prompt.

---

## MCP server issues (bugs)

### 1. FLAG-A — `UnresolvedAnalyzerReference` exception crashes four semantic-analysis tools
| Field | Detail |
|-------|--------|
| Tools | `find_unused_symbols` (full-solution path), `type_hierarchy`, `callers_callees`, `impact_analysis` |
| Input | Any invocation against a workspace where at least one project has an unresolved analyzer / package reference. Reproduced on `FirewallAnalyzer.Api` which declares `FluentValidation` + `FluentValidation.DependencyInjectionExtensions` via CPM but whose restore artifacts are not on disk. |
| Expected | Either (a) successful results that silently skip the unresolved reference, or (b) a clear, actionable error like "cannot analyze project Foo: package X not restored, run `dotnet restore` first". |
| Actual | `InvalidOperationException: Unexpected value 'Microsoft.CodeAnalysis.Diagnostics.UnresolvedAnalyzerReference' of type 'Microsoft.CodeAnalysis.Diagnostics.UnresolvedAnalyzerReference'.` The message format (`tool:"unknown"`) also hides which tool emitted the crash unless the caller already knows. |
| Severity | **High — incorrect result / missing functionality** |
| Reproducibility | **Always** — 4 successive calls all crashed, project-scoped workaround (`find_unused_symbols` with `project=FirewallAnalyzer.Domain` which has no package refs) PASS |
| Lock mode | `rw-lock` (only mode tested this run) |
| Workaround | Call with a `project` filter scoped to a project whose references are all resolved. Not all affected tools expose a `project` parameter. |
| Root cause (hypothesis) | The shared post-acquire workspace gate in `WorkspaceExecutionGate.RunPerWorkspaceAsync` appears to iterate the analyzer-reference list from `Project.AnalyzerReferences` and does not pattern-match the `UnresolvedAnalyzerReference` subtype when computing something (likely used in dead-code / call-graph / type-hierarchy inference). |

### 2. FLAG-B — `compile_check` returns 724 KB with no pagination and identical output for `emitValidation=true`
| Field | Detail |
|-------|--------|
| Tool | `compile_check` |
| Input | Default `{workspaceId}`; then `{workspaceId, emitValidation:true}` |
| Expected | Either a paginated response analogous to `project_diagnostics` (with `offset`, `limit`, `returnedDiagnostics`, `totalDiagnostics`, `hasMore`), or a summarised count-only response by default with an opt-in `verbose` / `includeAllDiagnostics` flag. For `emitValidation=true`, some observable difference from the default call (additional emit-time findings **or** a longer wall-clock). |
| Actual | The default call returned 724,051 characters (JSON-encoded array of ~1,092 CS0246 occurrences). The `emitValidation=true` call returned **724,050 characters — 1 character shorter, otherwise structurally identical**. Both results exceeded the Claude Code harness's inline cap and were force-persisted to disk. No pagination parameters are exposed in the live schema. |
| Severity | **Medium — degraded performance + documentation / UX mismatch** (contributes to the *Session interruption* root cause above) |
| Reproducibility | **Always** on this solution (twice in a row) |
| Lock mode | `rw-lock` |
| Notes | Prior audit's FLAG-001 reported that `emitValidation=true` was "~94× slower"; in this run both calls returned quickly (sub-second subjectively) with identical payload sizes. This suggests either a regression in emitValidation behavior (it is effectively a no-op now when the solution already has unresolved references) or a doc drift. Cannot confirm without timing instrumentation the operator did not receive. |

### 3. FLAG-C — `analyze_snippet` kind=`statements` reports wrong column for diagnostics
| Field | Detail |
|-------|--------|
| Tool | `analyze_snippet` (`kind="statements"`) |
| Input | `code: "int x = \"hello\";"` |
| Expected | Diagnostic CS0029 reported at line 1, column **~9** (the start of `"hello"` inside the user-supplied 17-character line). |
| Actual | Diagnostic CS0029 reported at `StartLine:1, StartColumn:66, EndLine:1, EndColumn:73`. Column 66 cannot exist in a 17-character input line. |
| Severity | **Medium — incorrect result** |
| Reproducibility | Observed once this run. Tool schema advertises UX-001 ("wrapper offset is subtracted server-side") but that offset adjustment appears to be line-only — the column appears to reference the *wrapped* snippet (`class Snippet { void Run() { int x = "hello"; } }` or similar), not the user input. |
| Lock mode | `rw-lock` |
| Notes | Related to prior FLAG-007 (`statements` kind wraps in `void Run()`, so `return 42;` also fails). That reproduces here too — see **Known issue regression check** below. |

### 4. FLAG-D — Mid-session workspace loss with no surfaced cause
| Field | Detail |
|-------|--------|
| Tool | Any workspace-scoped tool called after the subprocess restart |
| Input | Workspace id `86fc270122da4c03929ea0e960bf22d2`, 3 concurrent calls: `get_editorconfig_options`, `format_document_preview`, `test_discover` |
| Expected | Either the workspace continues to be reachable for the lifetime of the MCP client session, or the server surfaces a clear `notifications/message` lifecycle event when the workspace is unloaded, so the agent can re-issue `workspace_load` proactively instead of discovering the loss tool-by-tool. |
| Actual | Three successive `KeyNotFoundException: Workspace '86fc270122da4c03929ea0e960bf22d2' not found or has been closed.` errors. Error message is actionable (names the workspace, references `workspace_list`), but no lifecycle signal was raised in advance. Post-interrupt `workspace_list` confirms `count:0`. |
| Severity | **Medium — orchestration / UX** (shared between Claude Code client and Roslyn MCP server) |
| Reproducibility | Once, but produced complete audit halt |
| Lock mode | `rw-lock` |
| Notes | The Roslyn MCP server side did the right thing (clear error, actionable message). The surrounding Claude Code client appears to have restarted the MCP subprocess mid-session. The documented behavior at `workspace_load` acknowledges this is possible ("Cursor/Claude Code may relaunch the MCP server transparently between conversations"), which means this is a **known gap** for which the best server-side mitigation is: emit a structured `notifications/message` warning or `Information` event at process startup stating "this session has no loaded workspaces; call `workspace_load` if you intended to continue a prior session." |

---

## Improvement suggestions
- **`compile_check`** — add pagination (`offset`, `limit`, `severity`, `project`, `file` selectors) that match `project_diagnostics` so unresolved-reference solutions don't flood the agent's context with 700 KB of identical CS0246 noise. Related: add a `countOnly` flag so agents that only need an error/warning/info tally can pay ~bytes instead of ~MB.
- **`compile_check` doc** — clarify what `emitValidation=true` actually changes when the base compilation already has errors. Prior audit's "~94× slower" finding and this run's "byte-identical output" finding cannot both be correct; at least one direction drifted. A concrete example in the tool description (e.g. "emit validation catches missing member references that only surface during IL generation") would set expectations.
- **`analyze_snippet`** — either (a) translate column offsets back to the user snippet the way line offsets already are, or (b) document explicitly in the tool description that `Diagnostics[].StartColumn` is relative to the wrapped emit, not the user code. The current silent mismatch is the worst of both worlds. Related: for `statements` kind, either wrap the snippet in a method whose return type matches the last expression (so `return 42;` works), or document the `void` return constraint visibly in the tool description so agents don't mistake prior FLAG-007 for a user error.
- **`UnresolvedAnalyzerReference` handling (FLAG-A)** — catch and translate the exception at the `WorkspaceExecutionGate` layer so every workspace-scoped tool sees a uniform `"unresolved package references: run dotnet restore"` structured error instead of a raw `InvalidOperationException` with `tool:"unknown"`. The current error is actionable-ish but requires the caller to know that the fix is a package restore.
- **`tool:"unknown"` in error payloads** — all four FLAG-A crashes reported `"tool":"unknown"`. Error payloads should carry the actual tool name so structured log collectors and downstream agents can route the finding.
- **`notifications/message` surface** — clients that don't surface structured log notifications (Claude Code in this run) are blind to lifecycle / gate / rate-limit events. The server could also emit a compact per-response `diagnosticsSummary` field on every tool response (e.g. `{"gate":"rw-lock","queuedMs":12,"heldMs":87,"heartbeatCount":0}`) so the agent can observe concurrency even in clients that drop `notifications/message`.
- **Session survivability** — document the "MCP subprocess may restart between turns" behavior more prominently at the top of `server_info`'s response (not only inside `workspace_load`'s description), and consider a server-side `autoReloadFromToken` mode that re-attaches to the last `SnapshotToken` if the process restarts.
- **`get_nuget_dependencies` CPM closure** — prior FLAG-002 appears fixed (now surfaces `ResolvedCentralVersion`). Consider filing a closure record in the backlog.
- **`find_type_usages` static-member closure** — prior FLAG-005 appears fixed (now returns `staticMemberAccess`). Consider filing a closure record.

---

## Performance observations
| Tool | Input scale | Lock mode | Wall-clock (ms) | Notes |
|------|-------------|-----------|------------------|-------|
| `evaluate_csharp` (lock probe) | 5-line script | rw-lock | 861 | First call after workspace_load; JIT warmup included |
| `evaluate_csharp` (`Sum()`) | 1-line expression | rw-lock | 212 | Warm |
| `evaluate_csharp` (`int.Parse`) | 1-line expression | rw-lock | 40 | Runtime error path, fast |
| `nuget_vulnerability_scan` | 11 projects, non-transitive | rw-lock | 5,888 | Backed by `dotnet list package`; slowest legitimate call |
| `project_diagnostics` (limit=100) | 11 projects, 1,092 errors | rw-lock | not timed | ~56 KB payload |
| `compile_check` (default) | 11 projects, 1,092 errors | rw-lock | not timed | **~724 KB payload — FLAG-B** |
| `compile_check` (emitValidation) | 11 projects, 1,092 errors | rw-lock | not timed | **~724 KB payload — FLAG-B** |
| `get_namespace_dependencies` (full) | 11 projects | rw-lock | not timed | ~58 KB payload |
| `get_complexity_metrics` (limit=50) | solution-wide | rw-lock | not timed | Fast subjectively |
| `list_analyzers` (limit=100) | 28 assemblies / 492 rules | rw-lock | not timed | Fast subjectively |

No MCP tool observed to exceed 10 s wall-clock in this run. The "unresponsiveness" reported by the operator was **not** attributable to any single Roslyn MCP call exceeding its timeout — see the *Session interruption and unresponsiveness analysis* section above.

---

## Known issue regression check

Prior source: `ai_docs/audit-reports/2026-04-06_firewallanalyzer_mcp-server-audit.md` (7 flags).

| Source id | Summary | Status (this run) |
|-----------|---------|-------------------|
| FLAG-001 | `compile_check`: `emitValidation=true` doc claim inaccurate ("10–18× slower"; measured ~94× slower) | **Changed** — could not time directly this run, but the two calls returned **byte-identical** output sizes, which is inconsistent with the prior audit's "~94× slower" finding. Suggests either regression (emitValidation now no-op on unresolved-ref solutions) or that prior measurement was incidental. **Partial / needs re-measurement with a clean workspace.** |
| FLAG-002 | `get_nuget_dependencies`: CPM versions not resolved (`"centrally-managed"` only) | **Candidate for closure** — this run shows `"Version":"centrally-managed"` AND a new `"ResolvedCentralVersion":"12.1.1"` field alongside it. The original pain point (can't see the actual CPM version) is addressed. |
| FLAG-003 | `impact_analysis` vs `find_references`: inconsistent `Classification` contract | **Blocked — could not re-test.** `impact_analysis` now crashes with `UnresolvedAnalyzerReference` (FLAG-A), so the original inconsistency is unreachable on this solution. |
| FLAG-004 | `callers_callees`: duplicate callee entries for chained calls (`DriftDetector.RulesEqual`) | **Blocked — could not re-test.** `callers_callees` crashes with `UnresolvedAnalyzerReference` (FLAG-A). |
| FLAG-005 | `find_type_usages`: all static-class call sites classified as `"other"` | **Candidate for closure** — this run's 18 usages of `TrafficSimulator` (static class) are classified as `"staticMemberAccess"`, not `"other"`. |
| FLAG-006 | `symbol_signature_help`: cursor at method return-type position resolves return type, not method | **Still reproduces** — `TrafficSimulator.cs:13:26` resolves to `SimulationResult`, `DisplaySignature:"SimulationResult"`. `symbol_relationships` at the same position also resolves to `SimulationResult`. |
| FLAG-007 | `analyze_snippet` kind=`statements`: `return <value>` fails CS0127 | **Still reproduces** — `return 42;` fails with CS0127 "Since 'Snippet.Run()' returns void, a return keyword must not be followed by an object expression." Newly observed: column offset for the CS0127 location is also wrong (column 58, should be ~1–8). Related to new FLAG-C. |

---

## Known issue cross-check

New issues observed this run that do **not** match any entry in the prior audit (see **MCP server issues** section for full write-ups):

- **FLAG-A** — `UnresolvedAnalyzerReference` crash across 4 semantic tools. Not mentioned in the 2026-04-06 audit; likely because that audit ran conservatively on a clean workspace where `dotnet restore` had been run.
- **FLAG-B** — `compile_check` 724 KB unpaginated output with byte-identical result for `emitValidation=true`. The prior audit reported `emitValidation` as "~94× slower" (FLAG-001); this run's finding is orthogonal / possibly regressive.
- **FLAG-C** — `analyze_snippet` kind=`statements` column offset off by ~55 characters. Related to but distinct from prior FLAG-007.
- **FLAG-D** — Mid-session workspace loss with no lifecycle signal. Shared Claude Code host + Roslyn MCP server UX issue; documented under *Session interruption and unresponsiveness analysis* as the primary root cause of the operator's reported unresponsiveness.

---

## Appendix — raw tool-result file manifest

The following oversized tool results were persisted to disk during this run (contributed to the context-window pressure described in *Session interruption*):

| File (under `C:\Users\daryl\.claude\projects\C--Code-Repo-DotNet-Firewall-Analyzer\c6ead80c-574d-472f-815e-650af00d62bc\tool-results\`) | Size | Source |
|---|---|---|
| `toolu_01KbwyShqKuEvGtoAGVW5ohi.txt` | ~57.7 KB | `roslyn://server/catalog` |
| `toolu_01QgwUPXhPmDygGhfKbZX3EX.json` | ~56.3 KB | `project_diagnostics` (default) |
| `mcp-roslyn-compile_check-1775571461208.txt` | ~724 KB | `compile_check` (default) |
| `mcp-roslyn-compile_check-1775571512773.txt` | ~724 KB | `compile_check` (`emitValidation=true`) |
| `toolu_01P3AMgL28Rx3EbWYhQ5Zji4.json` | ~58.4 KB | `get_namespace_dependencies` (full) |

Combined persisted payload: **~1.6 MB** across 5 files, in a single pre-interrupt session. This supports the *Session interruption* finding that the unresponsiveness was driven by cumulative oversized tool results, not by any single stuck MCP call.
