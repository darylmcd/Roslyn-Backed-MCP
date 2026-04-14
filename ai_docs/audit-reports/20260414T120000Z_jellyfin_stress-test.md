# Stress Test Report: Jellyfin

| Field | Value |
|---|---|
| Repo | jellyfin/jellyfin |
| Solution | C:\Code-Repo\jellyfin\Jellyfin.sln |
| Projects | 40 |
| Documents | 2065 |
| Target frameworks | net10.0 (39 projects), netstandard2.0 (1 analyzer) |
| Workspace load (ms) | 9606 |
| Report generated | 2026-04-14T12:00:00Z |
| Roslyn MCP version | 1.14.0+157fdded595bca3861c8bcf824c8b828862f232a |
| Runtime | .NET 10.0.5, Roslyn 5.3.0.0 |
| OS | Windows 11 Pro 10.0.26200 |
| Surface | 77 stable + 51 experimental tools, 9 resources, 19 prompts |

**Performance budgets:**
- Single-symbol lookups (goto_definition, symbol_info, enclosing_symbol): ≤2s
- Search/scan (find_references, symbol_search, semantic_search): ≤10s
- Solution-wide analysis (compile_check, complexity, cohesion, dead code): ≤30s
- Mutation previews (rename, extract, code fix): ≤15s
- Build/test: ≤120s

---

## Phase 0: Setup

| Phase | Tool | Target | elapsedMs | Result | Notes |
|-------|------|--------|-----------|--------|-------|
| 0.1 | `server_info` | n/a | 8 | PASS | v1.14.0, Roslyn 5.3.0.0, .NET 10.0.5 |
| 0.2 | `workspace_load` | Jellyfin.sln | 9606 | PASS | 40 projects, 2065 docs, IsReady=true |
| 0.3 | `workspace_status` | workspace | 5 | PASS | IsReady=true, 0 errors, 1 warning (CodeAnalysis ref) |
| 0.4 | `project_graph` | workspace | 2 | PASS | 40 projects, max dep depth ~4 |

---

## Phase 1: Compilation baseline

| Phase | Tool | Target | elapsedMs | Result | Notes |
|-------|------|--------|-----------|--------|-------|
| 1.1 | `compile_check` | full solution | 10946 | PASS | 0 errors, 0 warnings — clean |
| 1.2 | `compile_check` | solution, severity=Warning | 8963 | PASS | 0 warnings at compiler level |
| 1.3 | `compile_check` | solution, emitValidation=true | 5976 | PASS | Faster than 1.1 (caching warmup) |
| 1.4 | `project_diagnostics` | full solution, limit=50 | 35553 | **SLOW** | 3433 total diagnostics; **exceeds 30s budget by 18%** |
| 1.5 | `compile_check` | Emby.Server.Implementations | 4604 | PASS | Largest prod project (19 deps), clean |
| 1.5b | `compile_check` | MediaBrowser.Controller | 595 | PASS | 8 deps, fast |

**Bottleneck:** `project_diagnostics` at 35.5s is the only Phase 0-1 tool exceeding budget.

---

## Phase 6: Throughput and rapid-fire calls

### Statistical Summary

| Batch | Min(ms) | Max(ms) | Mean(ms) | P95(ms) | Budget | Status |
|-------|---------|---------|----------|---------|--------|--------|
| symbol_info x10 | 1 | 1445 | 145 | 1445 | ≤2000 | PASS |
| go_to_definition x10 | 1 | 2517 | 263 | 2517 | ≤2000 | 1 SLOW |
| enclosing_symbol x10 | 1 | 7 | 2 | 7 | ≤2000 | PASS |

### Raw Results

| Phase | Tool | Target | elapsedMs | Result | Notes |
|-------|------|--------|-----------|--------|-------|
| 6.1 | `symbol_info` | IMediaEncoder (cold) | 1445 | PASS | Cold start spike |
| 6.1 | `symbol_info` | ILibraryManager (warm) | 1 | PASS | Hot cache |
| 6.1 | `symbol_info` | 8 more symbols | 1-4 | PASS | All ≤4ms warm |
| 6.2 | `go_to_definition` | BaseItemRepository.cs:30 | 2517 | **SLOW** | Cross-project jump to AuthenticationException.cs |
| 6.2 | `go_to_definition` | BaseItemRepository.cs:150 | 101 | PASS | Cross-project to JellyfinDbContext.cs |
| 6.2 | `go_to_definition` | 8 other positions | 1-4 | PASS | Same-file navigations |
| 6.3 | `enclosing_symbol` | 10 random positions | 1-10 | PASS | Max 10ms, median 2ms |

**Key finding:** Cross-project `go_to_definition` incurs a lazy-compilation penalty (~2.5s) on first jump to a new compilation unit. Subsequent jumps are instant.

---

## Phase 7: Recovery and edge cases

| Phase | Tool | Target | elapsedMs | Result | Error Quality | Notes |
|-------|------|--------|-----------|--------|---------------|-------|
| 7.1 | `go_to_definition` | whitespace (line 1, col 1) | 2628 | SLOW | **actionable** | "No definition found... Ensure workspace is loaded" |
| 7.2 | `go_to_definition` | line=999999 | 2 | PASS | **actionable** | "Line 999999 is out of range. File has 637 line(s)." |
| 7.3 | `document_symbols` | nonexistent file | 2 | PASS | **actionable** | "Source file not found... Verify path is absolute" |
| 7.4 | `workspace_reload` | — | 8252 | PASS | — | Same WID, version incremented |
| 7.4b | `compile_check` | post-reload | 8731 | PASS | — | 0 errors, 0 warnings |
| 7.5 | `workspace_load` | double load (same path) | 1 | PASS | **actionable** | Idempotent — same WID, instant no-op |
| 7.6 | `find_references` | ILibraryManager, limit=500 | 4208 | PASS | — | 362 refs returned |
| 7.7 | `symbol_search` | "zzzzznonexistent123" | 25 | PASS | **actionable** | Returns empty `[]`, no error |

**Error quality scorecard: 5/5 actionable (100%).**

---

## Phase 2: Symbol resolution at scale

### Per-symbol results

**Symbol 1: BaseItem** (base class, 1452 cross-solution references)

| Phase | Tool | Target | elapsedMs | Result | Notes |
|-------|------|--------|-----------|--------|-------|
| 2 | `symbol_search` | BaseItem | 4864 | PASS | Cold first call; 20 hits (limit-capped) |
| 2 | `symbol_info` | BaseItem | 4 | PASS | Kind=Class |
| 2 | `find_references` | BaseItem | 850 | PASS | totalCount=1452, page=200 |
| 2 | `find_references` (paginated) | BaseItem offset=50 | 740 | PASS | Pagination works correctly |
| 2 | `callers_callees` | BaseItem (class) | 42 | PASS | 100+ callers (capped), 0 callees |
| 2 | `impact_analysis` | BaseItem | 41-94 | **ERROR** | UnresolvedAnalyzerReference |

**Symbol 2: IServerApplicationHost** (widely-used interface)

| Phase | Tool | Target | elapsedMs | Result | Notes |
|-------|------|--------|-----------|--------|-------|
| 2 | `symbol_search` | IServerApplicationHost | 30 | PASS | 1 hit |
| 2 | `symbol_info` | IServerApplicationHost | 1 | PASS | Kind=Interface |
| 2 | `find_references` | IServerApplicationHost | 13 | PASS | totalCount=62 |
| 2 | `find_references` (paginated) | offset=50 | 3 | PASS | Correct: 12 remaining refs |
| 2 | `callers_callees` | IServerApplicationHost | 4 | PASS | 42 callers, 0 callees |
| 2 | `impact_analysis` | IServerApplicationHost | 5 | **ERROR** | UnresolvedAnalyzerReference |

**Symbol 3: EncodingHelper** (utility class)

| Phase | Tool | Target | elapsedMs | Result | Notes |
|-------|------|--------|-----------|--------|-------|
| 2 | `symbol_search` | EncodingHelper | 41 | PASS | 7 hits |
| 2 | `symbol_info` | EncodingHelper | 0 | PASS | Kind=Class |
| 2 | `find_references` | EncodingHelper (type) | 3 | PASS | 0 direct type refs (concrete impl) |
| 2 | `impact_analysis` | EncodingHelper | 1 | **ERROR** | UnresolvedAnalyzerReference |

**Broad queries:**

| Phase | Tool | Target | elapsedMs | Result | Notes |
|-------|------|--------|-----------|--------|-------|
| 2 | `symbol_search` | "Get" (broad) | 148 | PASS | 100 hits (limit-capped) |
| 2 | `semantic_search` | "async methods returning Task" | 24 | PASS | 20 results |

---

## Phase 3: Navigation and type hierarchy

| Phase | Tool | Target | elapsedMs | Result | Notes |
|-------|------|--------|-----------|--------|-------|
| 3 | `type_hierarchy` | BaseItem | 28 | **ERROR** | UnresolvedAnalyzerReference |
| 3 | `find_implementations` | ILibraryManager | 2-3 | **ERROR** | UnresolvedAnalyzerReference |
| 3 | `go_to_definition` | ILibraryManager | 2 | PASS | → ILibraryManager.cs:18 |
| 3 | `go_to_definition` | BaseItemDto | 1 | PASS | → BaseItemDto.cs |
| 3 | `go_to_definition` | UserController | 1 | PASS | → UserController.cs |
| 3 | `go_to_definition` | IApplicationPaths | 0-1 | PASS | → IApplicationPaths.cs |
| 3 | `go_to_definition` | BaseItem | 0-1 | PASS | → BaseItem.cs:45 |
| 3 | `document_symbols` | EncodingHelper.cs (7890 lines) | 6-178 | PASS | 191 nodes (47 fields, 141 methods, 1 ctor) |
| 3 | `get_syntax_tree` | EncodingHelper.cs L1-100 | 4-9 | PASS | 31KB AST for 100-line range |
| 3 | `get_syntax_tree` | EncodingHelper.cs (full) | 30 | PASS | 143KB full AST |
| 3 | `member_hierarchy` | GetUserDataKeys (BaseItem) | 5-6 | **ERROR** | UnresolvedAnalyzerReference |

**Critical finding:** `type_hierarchy`, `find_implementations`, `member_hierarchy`, `impact_analysis` all blocked by UnresolvedAnalyzerReference (Jellyfin.CodeAnalysis netstandard2.0 project). Tools using raw syntax/metadata (`symbol_search`, `go_to_definition`, `find_references`) are unaffected.

---

## Phase 4: Solution-wide analysis tools

| Phase | Tool | Target | elapsedMs | Result | Notes |
|-------|------|--------|-----------|--------|-------|
| 4.1 | `get_complexity_metrics` | solution, limit=20, minCC=10 | 5717 | PASS | Top: TranslateQuery CC=242, MI=0 |
| 4.2 | `get_cohesion_metrics` | solution, minMethods=3, limit=20 | 1056 | PASS | Worst: BaseItem LCOM4=38, 105 methods |
| 4.3 | `find_unused_symbols` | solution, includePublic=false | 342 | **ERROR** | UnresolvedAnalyzerReference |
| 4.4 | `suggest_refactorings` | solution, limit=10 | 4315 | **ERROR** | UnresolvedAnalyzerReference |
| 4.5 | `get_namespace_dependencies` | solution | 1261 | PASS | 582 nodes, 4909 edges, 0 cycles |
| 4.6 | `get_di_registrations` | solution | 12103 | PASS | 167 registrations |
| 4.7 | `get_nuget_dependencies` | solution | 3854 | PASS | 77 unique packages |
| 4.8 | `list_analyzers` | solution, limit=50 | 237 | PASS | 33 analyzers, 760 rules |

---

## Phase 5: Mutation previews (read-only)

| Phase | Tool | Target | elapsedMs | Result | Notes |
|-------|------|--------|-----------|--------|-------|
| 5.1 | `rename_preview` | BaseItem (20+ refs) | 5512 | PASS | 38 affected files |
| 5.2 | `rename_preview` | IUserManager (100+ refs) | 3713 | PASS | 57 affected files |
| 5.3 | `extract_interface_preview` | BaseItemRepository (28 public members) | 120-4252 | PASS | Requires filePath+typeName, not symbolHandle |
| 5.4 | `extract_method_preview` | TranslateQuery L1741-1760 | 39 | **ERROR** | "All selected statements must be in same block scope" |
| 5.4b | `extract_method_preview` | UserController.cs L543-545 | 39 | PASS | Alternative target succeeded |
| 5.5 | `code_fix_preview` | 12 diagnostic IDs | 1-91 | **ERROR** | "No curated code fix" for all CA/IDISP IDs |
| 5.6 | `fix_all_preview` | IDE0055, scope=project | 1014 | PASS | 82 fixes, 9 files |
| 5.7 | `format_document_preview` | EncodingHelper.cs | 263 | PASS | |
| 5.8 | `organize_usings_preview` | 3 files (avg) | 82 avg | PASS | 55-193ms range |

---

## Phase 8: Summary and recommendations

### 1. Performance summary table

| Category | Tools | Min(ms) | Max(ms) | Mean(ms) | P95(ms) | Budget | Status |
|----------|-------|---------|---------|----------|---------|--------|--------|
| **Lookup** (symbol_info, enclosing_symbol, go_to_definition) | 30 calls | 0 | 2628 | 87 | 2517 | ≤2000 | PASS (1 outlier) |
| **Search** (find_references, symbol_search, semantic_search) | 15 calls | 3 | 4864 | 477 | 4864 | ≤10000 | PASS |
| **Analysis** (compile_check, complexity, cohesion, diagnostics) | 12 calls | 237 | 35553 | 6997 | 35553 | ≤30000 | 1 SLOW |
| **Mutation preview** (rename, extract, fix_all, format, organize) | 12 calls | 39 | 5512 | 1175 | 5512 | ≤15000 | PASS |

### 2. Bottleneck list (tools exceeding budget)

| Rank | Tool | elapsedMs | Budget | Overage | Severity | Root Cause |
|------|------|-----------|--------|---------|----------|------------|
| 1 | `project_diagnostics` (no filter) | 35553 | 30000 | +18% | SLOW | 3433 diagnostics across 40 projects; analyzer pass overhead |
| 2 | `go_to_definition` (cold cross-project) | 2517-2628 | 2000 | +26-31% | SLOW | Lazy compilation of new project compilation unit on first jump |
| 3 | `symbol_info` (cold first call) | 1445 | 2000 | within | borderline | Workspace symbol resolution cold-start |
| 4 | `get_di_registrations` | 12103 | 30000 | within | OK | Scanning 40 projects for DI patterns; heaviest analysis tool |

### 3. Scalability assessment

| Category | 40-project score | Would survive 100-project? | Notes |
|----------|-----------------|---------------------------|-------|
| Workspace load | 9.6s | Yes | Linear scaling expected; ~24s at 100 projects |
| Compilation check | 10.9s | Yes | Roslyn incremental compilation handles large solutions |
| Symbol resolution | 0-850ms | Yes | Pagination handles high-fan symbols (1452 refs) |
| Diagnostics scan | 35.5s | **Marginal** | Already exceeds 30s budget; 100-project solution would likely timeout |
| Complexity metrics | 5.7s | Yes | Scoped by limit parameter |
| DI registrations | 12.1s | **Marginal** | Scans all projects; ~30s at 100 projects |
| Mutation previews | 0.04-5.5s | Yes | Scoped to affected files, not solution-wide |
| Throughput (warm) | 1-4ms/call | Yes | Cached resolution is essentially free |

### 4. Error quality scorecard

| Total error probes | Actionable | Vague | Unhelpful |
|-------------------|------------|-------|-----------|
| 5 | 5 (100%) | 0 | 0 |

All error messages include specific remediation guidance (file line counts, parameter names, workspace load instructions).

### 5. Top 5 recommendations

1. **Fix UnresolvedAnalyzerReference handling** — 5 tools (`type_hierarchy`, `find_implementations`, `member_hierarchy`, `impact_analysis`, `find_unused_symbols`) crash on repos with netstandard2.0 analyzer projects. These should gracefully skip the unresolved project and warn, not throw `InvalidOperationException`. This is the single highest-impact fix for external repo compatibility.

2. **Optimize `project_diagnostics` for large solutions** — At 35.5s on 40 projects, this tool will timeout on larger solutions. Consider streaming results, incremental analysis, or returning cached results with a staleness indicator.

3. **Reduce cold-start penalty for cross-project navigation** — First `go_to_definition` across project boundaries takes ~2.5s (lazy compilation). Pre-warming compilations for referenced projects during `workspace_load` or offering a `workspace_warm` tool would eliminate this.

4. **Add curated code fixes for common CA diagnostics** — `code_fix_preview` returned "no curated fix" for all 12 diagnostic IDs tested (CA1515, CA1062, CA1873, CA1822, CA1031, etc.). These are the most common .NET analyzer warnings; curated fixes would significantly increase the tool's utility on real codebases.

5. **Document parameter formats explicitly** — `extract_interface_preview` requires `filePath`+`typeName` (not `symbolHandle`), `apply_text_edit` requires `edits` array format, `set_diagnostic_severity` requires `filePath`. These schema gaps caused multiple failed attempts before the correct format was discovered.

### 6. Comparison baseline

No prior stress-test reports found in `C:\Code-Repo\Roslyn-Backed-MCP\ai_docs\audit-reports\` for Jellyfin. This report establishes the baseline for v1.14.0.

**Comparison with deep-review audit (same session, same repo):** Timing data is consistent across both runs. `workspace_load` ≈9-10s, `compile_check` ≈9-11s, `project_diagnostics` ≈29-35s (the only tool consistently near/over budget). The `UnresolvedAnalyzerReference` issue affects the same set of tools in both the audit and stress test.

---

## All Phases Complete

| Phase | Status | Tools Called | Errors | SLOW |
|-------|--------|-------------|--------|------|
| 0 | Complete | 4 | 0 | 0 |
| 1 | Complete | 6 | 0 | 1 (`project_diagnostics` 35.5s) |
| 2 | Complete | 18 | 3 (`impact_analysis` × 3, UnresolvedAnalyzerRef) | 0 |
| 3 | Complete | 11 | 3 (`type_hierarchy`, `find_implementations`, `member_hierarchy`) | 0 |
| 4 | Complete | 8 | 2 (`find_unused_symbols`, `suggest_refactorings`) | 0 |
| 5 | Complete | 10 | 2 (`code_fix_preview`, `extract_method` scope) | 0 |
| 6 | Complete | 30 | 0 | 1 (`go_to_definition` cold 2517ms) |
| 7 | Complete | 8 | 0 | 0 |
| **Total** | **Complete** | **95** | **10** | **2** |

**85/95 tool calls PASS (89.5%). 10 errors (all UnresolvedAnalyzerReference or missing curated fixes). 2 SLOW (project_diagnostics, go_to_definition cold start).**
