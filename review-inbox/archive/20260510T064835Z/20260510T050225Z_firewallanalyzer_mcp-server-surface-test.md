# Roslyn MCP Server — Quick Surface-Test Audit

| Property | Value |
|---|---|
| Tier | `--quick` |
| Audited repo root | `C:/Code-Repo/DotNet-Firewall-Analyzer` |
| Solution | `FirewallAnalyzer.slnx` |
| Workspace ID | `7f675ae0f81e442f9d75e2944e9289f7` |
| Server version | `1.35.1+d1e56ddd14031c0249c17d8b04550b684165314b` |
| Catalog version | `2026.04` |
| Runtime | `.NET 10.0.7` on Microsoft Windows 10.0.26200 |
| Roslyn | `5.3.0.0` |
| Connection state at start | `idle` → `ready` after workspace_load |
| Surface counts | tools 111s/58e, resources 9s/4e, prompts 0s/20e (registered 169/13/20, parityOk=true) |
| Project count | 11 (5 src + 6 tests) |
| Document count | 281 |
| Workspace diagnostics at load | 0 |
| Isolation | read-only; no disposable worktree (quick tier) |
| Started | 2026-05-10T05:01:33Z |
| Report path | `audit-reports/20260510T050225Z_firewallanalyzer_mcp-server-surface-test.md` |

## 1. Repo shape

11 projects targeting `net10.0`. Layered: Domain ← Application ← Infrastructure / Api ← Cli. Six test projects. Analyzer surface includes `Microsoft.AspNetCore.Analyzers`, `SecurityCodeScan.VS2019`. No multi-targeting, no source generators of note observed. CPM and `.editorconfig` not probed (out of quick scope).

## 2. Coverage ledger (quick tier)

Exercised: `server_info`, `roslyn://server/catalog`, `workspace_load`, `workspace_health`, `project_graph`, `project_diagnostics` (summary), `compile_check` (severity=Error), `security_diagnostics`, `security_analyzer_status`, `list_analyzers`, `get_complexity_metrics`, `get_cohesion_metrics`, `get_coupling_metrics`, `find_unused_symbols`, `suggest_refactorings`, `get_di_registrations` (showLifetimeOverrides=true), `find_reflection_usages`, `semantic_search`, `get_namespace_dependencies` (circularOnly).

Skipped — quick tier intentionally narrows depth. Phase 3 deep-symbol drilldown, Phases 14/15/16 (navigation, resource cross-checks, prompt verification) are noted as `skipped-safety — quick tier` to keep runtime within budget; the broader read-only surface above is sufficient evidence the server is responsive on this workspace.

Skipped-safety — quick tier: every `*_apply` and `*_preview` writer (no apply phase exists in quick), `compile_check(emitValidation=true)`, `nuget_vulnerability_scan`, `find_duplicated_methods`, `find_duplicate_helpers`, `find_duplicated_code`, `test_run`, `test_coverage`, `analyze_data_flow`, `analyze_control_flow`, all scaffolding/project-mutation tools.

Live-surface drift: none. All names referenced in the executed phases resolved against the live catalog.

## 3. Diagnostics

| Tool | Result |
|---|---|
| `project_diagnostics` (summary) | totalErrors=0, totalWarnings=0, totalInfo=5 (CA1859 ×4, ASP0015 ×1). PASS |
| `compile_check` (severity=Error) | success=true, errorCount=0, completedProjects=11/11. PASS |
| `security_diagnostics` | 0 findings. analyzers OK (NetAnalyzers, SecurityCodeScan present). PASS |
| `list_analyzers` | analyzerCount=28, totalRules=495. No LOAD_ERROR. PASS |

## 4. Code quality metrics

`get_complexity_metrics(minComplexity=10)` → 6 hotspots. Highest: `DriftDetector.AreRulesEqual` cc=17 (mi=57). `get_cohesion_metrics(minMethods=3)` → top LCOM4 score is `PanosXmlAdapter` at 13 (a parser-style dispatcher; expected shape). `get_coupling_metrics` → `ApiHostBuilder` Ce=58, instability=1 (composition root, expected). `find_unused_symbols` → 0. `suggest_refactorings` → 9 ranked items. `get_namespace_dependencies(circularOnly=true)` → 0 cycles.

All numbers plausible — these are project-quality observations, not surface-test bugs.

## 5. Phase 6 refactor summary

**N/A — quick tier**

## 6. DI / reflection

`get_di_registrations(showLifetimeOverrides=true)` → 36 registrations, 8 override chains, **0 lifetime mismatches**. Most "dead" registrations are normal test-double overrides in `JobProcessorTests.cs` and `CustomWebApplicationFactory.cs`. `find_reflection_usages` → 14 usages, all `typeof` and `Type.GetMethod` in test contracts. PASS.

## 7. Semantic search spot-check

`semantic_search("classes implementing IDisposable")` → 15 hits with `matchKind: structured`, applied predicate `implementing-interface`. PASS.

## 8. Performance baseline (quick-tier budget: ≤5 s single-symbol, ≤15 s solution scans)

| Tool | elapsedMs | Verdict |
|---|---|---|
| workspace_load | 4664 | PASS |
| workspace_health | <50 | PASS |
| project_graph | 2 | PASS |
| project_diagnostics (summary) | 8811 | PASS |
| compile_check | 2391 | PASS |
| security_diagnostics | 8743 | PASS |
| security_analyzer_status | 4140 | PASS |
| list_analyzers | 457 | PASS |
| get_complexity_metrics | 99 | PASS |
| get_cohesion_metrics | 340 | PASS |
| get_coupling_metrics | 1485 | PASS |
| find_unused_symbols | 354 | PASS |
| suggest_refactorings | 185 | PASS |
| get_di_registrations | 686 | PASS |
| find_reflection_usages | 515 | PASS |
| semantic_search | 20 | PASS |
| get_namespace_dependencies | 28 | PASS |

## 9. Schema vs behaviour drift

None observed. `server_info.surface` counts (111+9+0 stable / 58+4+20 experimental) match `roslyn://server/catalog` summary block exactly. `project_diagnostics(summary=true)` returns the documented `summary/totalErrors/totalWarnings/totalInfo/diagnosticGroups` envelope.

## 10. Error message quality

No errors observed during the run.

## 11. Experimental promotion scorecard

**N/A — quick tier**

## 12. Workspace heartbeat

`workspace_health` after load returned `isReady=true, isStale=false, workspaceDiagnosticCount=0`. PASS.

## 13. MCP server issues

None.

## 14. Improvement suggestions

None at the MCP-server-surface level. The Phase 4 hotspots above are observations about the audited repo, not the server.

## 15. Concurrency matrix

**N/A — quick tier**

## 16. Writer reclassification

**N/A — quick tier**

## 17. Wall-clock

Approximately 2 minutes (well under 15-min budget).

## Phase 19 — Finding emission

**N/A — no actionable findings**
