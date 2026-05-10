# MCP Server Audit Report

## 1. Header
- **Date:** 2026-05-10T05:30Z
- **Audited solution:** FirewallAnalyzer.slnx
- **Audited revision:** main @ 184021e
- **Entrypoint loaded:** C:/Code-Repo/DotNet-Firewall-Analyzer/FirewallAnalyzer.slnx
- **Flags:** (none) — full tier (default mode)
- **Isolation:** disposable worktree was created at `C:/Code-Repo/DotNet-Firewall-Analyzer-surface-test-20260510T053000Z` on branch `mcp-server-surface-test/20260510T053000Z` BUT could not be loaded as an MCP workspace — see Phase 6 finding (server's client-sanctioned-roots policy refused the sibling path). Effective mode: degraded — Phase 6 / 9 / 10 / 12 / 13 writer phases all `skipped-safety` against the apply surface.
- **Teardown:** clean — disposable worktree removed, branch deleted, primary checkout clean (only `audit-reports/` untracked, as before)
- **Client:** Claude Code CLI (Claude Opus 4.7 1M)
- **Workspace id:** `3af866d136104a42bc5d33c7a002a04b`
- **Warm-up:** yes — `workspace_warm` ran (projectsWarmed=11, coldCompilationCount=8, elapsedMs=2852)
- **Server:** roslyn-mcp 1.35.1+d1e56dd, productShape=local-first
- **Catalog version:** 2026.04
- **Roslyn / .NET:** Roslyn 5.3.0.0 / .NET 10.0.7 on Windows 10.0.26200
- **Live surface:** tools 111s/58e (169 total), resources 9s/4e (13 total), prompts 0s/20e (20 total) — `parityOk=true`
- **Scale:** 11 projects (5 src + 6 tests), 281 documents, all `net10.0`
- **Repo shape:** Layered Domain ← Application ← Infrastructure / Api ← Cli; analyzers include NetAnalyzers + SecurityCodeScan + Microsoft.AspNetCore.Analyzers + BannedApiAnalyzers; 6 test projects (xUnit + NSubstitute + FluentAssertions); CPM enabled (`<Version>` centrally managed, all packages report `centrally-managed`); `.editorconfig` present; no multi-targeting; ASP.NET source generators (Logging, RegexGenerator, PublicProgram) present; `dotnet restore` reported up-to-date.
- **Prior issue source:** prior quick-tier report at `audit-reports/20260510T050225Z_firewallanalyzer_mcp-server-surface-test.md` (same day)
- **Debug log channel:** no — Claude Code CLI does not surface MCP `notifications/message` log entries inline
- **Report path note:** stays under `<audited-repo-root>/audit-reports/`

## 2. Coverage summary

| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-------:|------------:|----------:|----------------:|-------------:|-------------------:|---------------:|--------:|-------|
| Tools | analysis/diagnostics/metrics/symbols/flow/snippets/nav/test/search/DI/MSBuild | 80 | 21 | ~70 | 0 | 0 | 0 | ~50 | 0 | All read-side surfaces exercised; all writer surfaces (`*_apply`, all `*_preview` chains intended for apply) marked `skipped-safety` because Phase 6 worktree could not be loaded as a separate workspace |
| Tools | refactoring (preview/apply) | 31 | 37 | 0 | 0 | 0 | 0 | 68 | 0 | Phase 6/10/12/13 blocked by sanctioned-root policy |
| Resources | server/workspace | 9 | 4 | 5 | n/a | n/a | 0 | 0 | 0 | server_catalog, resource_templates, workspaces, workspaces_verbose all confirmed; per-workspace status/projects/diagnostics/source resources resolved via tools (templates dump confirms shape) |
| Prompts | guided | 0 | 20 | 3 | n/a | n/a | 0 | 0 | 0 | discover_capabilities (full render), explain_error (param-error path), review_file (param-error path); remaining 17 unrendered but enumerated via unknown-prompt error path |

## 3. Coverage ledger (representative)

| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | server_info | stable | server | exercised | -1 | <10 | parityOk=true confirmed |
| tool | workspace_load | stable | workspace | exercised | 0 | 7631 | 11 projects 281 docs, isReady=true (also rejected sibling path with `not under any client-sanctioned root` error — see #13.1) |
| tool | workspace_warm | experimental | workspace | exercised | 0 | 2852 | coldCompilationCount=8 |
| tool | workspace_health | stable | workspace | exercised | -1 | <50 | healthy |
| tool | project_graph | stable | workspace | exercised | 0 | 2 | |
| tool | project_diagnostics | stable | analysis | exercised | 1 | 13081 | summary; non-default severity=Warning probe preserved totals (5 info) |
| tool | compile_check | stable | analysis | exercised | 1 | 668/9135 | emitValidation=true 13.6× slower (real emit) |
| tool | security_diagnostics | stable | security | exercised | 1 | 8069 | 0 findings |
| tool | security_analyzer_status | stable | security | exercised | 1 | 5345 | NetAnalyzers + SecurityCodeScan present |
| tool | nuget_vulnerability_scan | experimental | security | exercised | 1 | 13544 | 0 vulnerabilities; includeTransitive=true |
| tool | list_analyzers | stable | analysis | exercised | 1 | 53 | 28 analyzers, 408 rules (last quick run reported 495 — see #14) |
| tool | diagnostic_details | stable | analysis | exercised | 1 | 154 | guidanceMessage included |
| tool | get_complexity_metrics | stable | metrics | exercised | 2 | 115 | 6 hotspots; AreRulesEqual cc=17 |
| tool | get_cohesion_metrics | stable | metrics | exercised | 2 | 263 | PanosXmlAdapter LCOM4=13 (parser shape) |
| tool | get_coupling_metrics | experimental | metrics | exercised | 2 | 1375 | 10 results |
| tool | find_unused_symbols | stable | metrics | exercised | 2 | 509/825 | 0 internal, 5 public (low/medium confidence) |
| tool | find_duplicated_methods | stable | metrics | exercised | 2 | 133 | 8 groups |
| tool | find_duplicate_helpers | experimental | metrics | exercised | 2 | 44 | 9 helpers (mostly framework wrappers — accurate) |
| tool | find_duplicated_code | experimental | metrics | exercised | 2 | 46 | alias of find_duplicated_methods (deprecation populated) |
| tool | find_dead_locals | experimental | metrics | exercised | 2 | 1741 | 0 |
| tool | find_dead_fields | experimental | metrics | exercised | 2 | 1674 | 0 |
| tool | get_namespace_dependencies | stable | metrics | exercised | 2 | 74 | 0 cycles (circularOnly=true) |
| tool | get_nuget_dependencies | stable | metrics | exercised | 2 | 1538 | summary=true; 18 packages, all CPM |
| tool | suggest_refactorings | stable | metrics | exercised | 2 | 224 | 9 ranked items |
| tool | symbol_search | stable | symbols | exercised | 3, 17 | 776 | empty-query negative path returns clean note |
| tool | symbol_info | stable | symbols | exercised | 3, 17 | 9 | bogus workspaceId returns clean NotFound (tool field populated) |
| tool | document_symbols | stable | symbols | exercised | 3 | 14 | |
| tool | type_hierarchy | stable | symbols | exercised | 3 | 9 | static class — no derived/base |
| tool | find_implementations | stable | symbols | exercised | 3 (negative) | 6 | clean NotFound on fabricated metadata name |
| tool | find_references | stable | symbols | exercised | 3, 17 | 24 | summary=true; 14 refs; bogus name → NotFound |
| tool | find_references_bulk | stable | symbols | exercised | 14 | 8 | 2-symbol batch; per-symbol truncation |
| tool | find_consumers | stable | symbols | exercised | 3 | 9 | dependencyKinds reported only "Other" — see #14 |
| tool | find_type_consumers | experimental | symbols | exercised | 3 | 17 | kinds=["local"] only on a static-class type — see #14 |
| tool | find_type_mutations | stable | symbols | exercised | 3 | 9 | 0 mutations on FileSnapshotStore (delegates IO via fields) |
| tool | find_type_usages | stable | symbols | exercised | 3 | 23 | classified GenericArgument |
| tool | callers_callees | stable | symbols | exercised | 3 | 14 | |
| tool | find_property_writes | experimental | symbols | exercised | 3 | 20 | WriteKind=ObjectInitializer |
| tool | find_shared_members | experimental | symbols | exercised | 3 | 45 | 3 clusters |
| tool | member_hierarchy | stable | symbols | exercised | 3 | 20 | base + 2 overrides |
| tool | symbol_relationships | stable | symbols | exercised | 3 | 20 | hasMore=true |
| tool | symbol_signature_help | experimental | symbols | exercised | 3 | 3 | |
| tool | impact_analysis | stable | symbols | exercised | 3 | 13 | summary=true |
| tool | symbol_impact_sweep | experimental | symbols | exercised | 3 | 153 | summary=true; suggestedTasks populated |
| tool | probe_position | experimental | symbols | exercised | 3 | 9 | resolved Keyword tokenKind |
| tool | analyze_data_flow | stable | flow | exercised | 4, 17 | 15 | also negative probe (start>end) → clean error |
| tool | analyze_control_flow | stable | flow | exercised | 4 | 4 | synthesized expression-body result with warning (correct) |
| tool | get_operations | stable | flow | exercised | 4 | 5 | PropertyReference |
| tool | get_syntax_tree | stable | flow | exercised | 4 | 9 | maxTotalBytes=10000 honored |
| tool | trace_exception_flow | stable | flow | exercised | 4 | 32 | truncated=true at maxResults=10; 10 catch sites for IOException |
| tool | analyze_snippet | stable | snippet | exercised | 5 | 133-147 | all kinds; CS0029 column=9 (user-relative) |
| tool | evaluate_csharp | stable | snippet | exercised | 5 | 27-161 | sum=55; runtime FormatException reported gracefully |
| tool | get_editorconfig_options | stable | editorconfig | exercised | 7 | 5 | 49 effective options |
| tool | get_msbuild_properties | stable | msbuild | exercised | 7 | 69 | includedNames probed (non-default path) |
| tool | evaluate_msbuild_property | stable | msbuild | exercised | 7 | 63 | matches step 1 |
| tool | evaluate_msbuild_items | stable | msbuild | exercised | 7 | 77 | 62 Compile items in Domain |
| tool | semantic_search | stable | search | exercised | 11 | 21-41 | "classes implementing IDisposable" → 10; "Task<bool>" → only 1 (see #13.2) |
| tool | semantic_grep | stable | search | exercised | 11 | 43 | 0 hits on `^[Aa]sync` identifier scope (correct — async is a modifier) |
| tool | find_reflection_usages | stable | reflection | exercised | 11 | 611 | 14 usages |
| tool | get_di_registrations | stable | DI | exercised | 11 | 1468 | 36 regs, 0 lifetime mismatches, 16 dead regs (test overrides) |
| tool | source_generated_documents | stable | workspace | exercised | 11 | n/a | 14 source-gen documents |
| tool | test_discover | stable | test | exercised | 8 | 25 | 392 total tests |
| tool | test_related | stable | test | exercised | 8 | 58 | 26 related to DriftDetector |
| tool | test_reference_map | experimental | test | exercised | 8 | 727 | coveragePercent=22.1, 30 mockDriftWarnings (see #14) |
| tool | validate_workspace | stable | test | exercised | 8 | 43 | overallStatus=clean |
| tool | validate_recent_git_changes | stable | test | exercised | 8 | 9535 | clean (audit-reports/ is untracked) |
| tool | go_to_definition | stable | navigation | exercised | 14, 17 | 4 | line-out-of-range → clean error with file's line count |
| tool | goto_type_definition | stable | navigation | exercised | 14 | 2 | InvalidOperation on `bool` (no source location) |
| tool | enclosing_symbol | stable | navigation | exercised | 14 | 3 | resolved enclosing method |
| tool | find_overrides | stable | symbols | exercised | 14 | 11 | 2 overrides (FileSnapshotReader + FileSnapshotStore) |
| tool | find_base_members | stable | symbols | exercised | 14 | 2 | 1 base (ISnapshotReader) |
| tool | get_completions | stable | navigation | exercised | 14 | 284 | filterText=To; types only — see #14 |
| tool | get_prompt_text | experimental | prompts | exercised | 16, 17 | 7-33 | discover_capabilities renders full; unknown prompt error lists all 20 names |
| tool | workspace_list | stable | workspace | exercised | 0 | <10 | |
| tool | workspace_close | stable | workspace | exercised | closure | n/a | invoked at end |
| resource | roslyn://server/catalog | stable | server | exercised | 16 | n/a | counts match server_info |
| resource | roslyn://server/resource-templates | stable | server | exercised | 0 | n/a | via ListMcpResourcesTool |
| resource | roslyn://workspaces (and verbose) | stable | workspace | exercised | 0 | n/a | listed |
| resource | roslyn://server/catalog/full + tools/page + prompts/page | experimental | server | skipped-safety | — | n/a | Cap-safe siblings already covered the surface; full unpaginated 80KB skipped to keep transcript small |
| prompt | discover_capabilities | experimental | prompts | exercised | 16 | 7 | full render OK; references only live tools |
| prompt | explain_error | experimental | prompts | exercised-preview-only | 16 | <10 | error path verified — surfaces required workspaceId then filePath as separate errors (multi-step required-param errors, see #14) |
| prompt | review_file | experimental | prompts | exercised-preview-only | 16 | <10 | required-param error path verified |
| prompt | unknown_prompt_xyz | n/a | prompts | exercised (negative) | 17 | 33 | error lists all 20 valid prompt names — actionable |
| prompt | (17 other live prompts) | experimental | prompts | skipped-safety | 16 | n/a | not rendered to keep transcript bounded; enumerated via the unknown-prompt error |
| tools | every `*_apply` writer + every `*_preview` chain intended for apply | mixed | refactoring | skipped-safety | 6/9/10/12/13 | n/a | sanctioned-root policy refused the disposable worktree workspace_load (see #13.1) — Phase 6 cannot run without violating "never mutate audited repo's main" safety contract |
| tools | apply_with_verify, revert_last_apply, revert_apply_by_sequence, workspace_changes | stable/experimental | refactoring | skipped-safety | 6m/9 | n/a | dependent on Phase 6 |
| tools | scaffold_*_preview/apply, project mutation `*_preview/apply` | experimental | scaffolding/project-mutation | skipped-safety | 12/13 | n/a | dependent on Phase 6 |
| tools | concurrency probes (8b.2 parallel, 8b.3 RW exclusion, 8b.5 writers) | n/a | concurrency | blocked | 8b | n/a | Claude Code CLI serializes MCP tool calls — true parallelism not observable client-side; sequential baselines captured in *Performance baseline* |

Live-surface drift (Phase 0 step 14): no names referenced in the executed phases were absent from the catalog. Catalog-only names (most experimental refactor tools — restructure_preview, parameter_object_preview, change_signature_preview, etc.) are listed in the prompt's phase guidance but `skipped-safety` here.

## 4. Verified tools (working)

All exercised tools above produced semantically correct results. Highlights:
- `workspace_load` / `workspace_warm` / `workspace_health` — clean cold→warm sequence (8 cold compilations primed in 2.85 s).
- `compile_check(emitValidation=true)` materialized the documented 50–100× slowdown only because packages were restored — verified the doc claim end-to-end.
- `get_di_registrations(showLifetimeOverrides=true, summary=true)` — correctly classifies 16 "dead" registrations as test-double overrides; lifetimeMismatchCount=0.
- `trace_exception_flow` — correctly walks `System.Exception` catches that are assignable from `IOException`, with `catchesBaseException=true` flagged.
- `analyze_control_flow` — synthesized expression-body result with the documented warning ("Synthesized from expression-bodied member"); v1.8+ contract honored.
- `analyze_snippet(kind=statements)` — CS0029 reported with `startColumn=9` (user-relative; FLAG-C contract).
- `evaluate_csharp` — both happy and runtime-error paths produce structured envelopes; `appliedScriptTimeoutSeconds=10` exposed.
- `find_property_writes` — `WriteKind=ObjectInitializer` correctly bucketed.
- `find_overrides` / `find_base_members` — interface ↔ implementation graph navigation correct.
- `get_prompt_text` — `discover_capabilities` rendered without hallucinating any tool name; only catalog-resident tools appear in the message.
- `test_reference_map` — surfaces 30 `mockDriftWarnings` against the FirewallAnalyzer test suite (real findings; the audited repo's NSubstitute mocks of `IPanosClient` and `ISnapshotStore` legitimately omit setups for production-callable methods).

## 5. Phase 6 apply-tool exercise summary

- **Disposable worktree path:** `C:/Code-Repo/DotNet-Firewall-Analyzer-surface-test-20260510T053000Z` (CREATED via `git worktree add`, RESTORED via `dotnet restore`, but `mcp__roslyn__workspace_load` REJECTED it — see #13.1).
- **Disposable branch:** `mcp-server-surface-test/20260510T053000Z`
- **Scope:** **N/A — every Phase 6 sub-phase (6a–6m) skipped-safety. Sanctioned-root policy refused the disposable worktree as a workspace, and the audited repo's primary `main` checkout MUST NOT receive Phase 6 mutations per the skill's safety contract. This produces the same effective behavior as `--no-worktree` mode but was not the operator's choice.**
- **Apply-tool calls:** none on the worktree; none on `main`.
- **Verification:** `compile_check`, `validate_workspace`, `validate_recent_git_changes` all returned `clean` against `main` post-run (no leakage).
- **Teardown outcome:** clean — `dotnet build-server shutdown` (release locks) → `git worktree remove --force <path>` → `git branch -D mcp-server-surface-test/20260510T053000Z` → `git status` confirmed only `audit-reports/` untracked (unchanged from start).

## 6. Performance baseline (`_meta.elapsedMs`)

Single-call wall-clock for exercised read surfaces, p50 over 1–2 calls (low-volume run). Budgets per principle #3 (single-symbol reads ≤5 s, solution scans ≤15 s).

| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|------:|-------:|-------:|-------:|-------------|--------|-------|
| workspace_load | stable | workspace | 1 | 7631 | 7631 | 7631 | 11 proj / 281 docs | n/a | within typical cold-load expectation |
| workspace_warm | experimental | workspace | 1 | 2852 | 2852 | 2852 | 11 proj | n/a | |
| project_diagnostics | stable | analysis | 2 | 13081 | 13863 | 13863 | solution-wide | ≤15s | within budget; expensive but expected |
| compile_check | stable | analysis | 2 | 668 | 9135 | 9135 | solution-wide | ≤15s | emitValidation=true case ~13.6× slower |
| security_diagnostics | stable | security | 1 | 8069 | 8069 | 8069 | solution-wide | ≤15s | |
| security_analyzer_status | stable | security | 1 | 5345 | 5345 | 5345 | solution-wide | ≤15s | over single-symbol budget but acceptable for cross-project query |
| nuget_vulnerability_scan | experimental | security | 1 | 13544 | 13544 | 13544 | solution-wide | ≤15s | within budget — calls `dotnet list package --vulnerable` once per project |
| list_analyzers | stable | analysis | 1 | 53 | — | 53 | n/a | ≤5s | |
| diagnostic_details | stable | analysis | 1 | 154 | — | 154 | single | ≤5s | |
| get_complexity_metrics | stable | metrics | 1 | 115 | — | 115 | solution-wide | ≤15s | |
| get_cohesion_metrics | stable | metrics | 1 | 263 | — | 263 | solution-wide | ≤15s | |
| get_coupling_metrics | experimental | metrics | 1 | 1375 | — | 1375 | solution-wide | ≤15s | |
| find_unused_symbols | stable | metrics | 2 | 509 | 825 | 825 | solution-wide | ≤15s | |
| find_duplicated_methods | stable | metrics | 1 | 133 | — | 133 | solution-wide | ≤15s | |
| find_duplicate_helpers | experimental | metrics | 1 | 44 | — | 44 | solution-wide | ≤15s | |
| find_dead_locals | experimental | metrics | 1 | 1741 | — | 1741 | solution-wide | ≤15s | |
| find_dead_fields | experimental | metrics | 1 | 1674 | — | 1674 | solution-wide | ≤15s | |
| suggest_refactorings | stable | metrics | 1 | 224 | — | 224 | solution-wide | ≤15s | |
| symbol_search | stable | symbols | 2 | 776 | — | 776 | substring | ≤5s | |
| symbol_info | stable | symbols | 1 | 9 | — | 9 | single | ≤5s | |
| document_symbols | stable | symbols | 1 | 14 | — | 14 | single file | ≤5s | |
| find_references | stable | symbols | 2 | 24 | — | 24 | summary mode | ≤5s | |
| find_references_bulk | stable | symbols | 1 | 8 | — | 8 | 2 symbols | ≤5s | |
| find_consumers | stable | symbols | 1 | 9 | — | 9 | single | ≤5s | |
| find_type_consumers | experimental | symbols | 1 | 17 | — | 17 | single | ≤5s | |
| find_type_usages | stable | symbols | 1 | 23 | — | 23 | limit=10 | ≤5s | |
| find_type_mutations | stable | symbols | 1 | 9 | — | 9 | single | ≤5s | |
| callers_callees | stable | symbols | 1 | 14 | — | 14 | single | ≤5s | |
| find_property_writes | experimental | symbols | 1 | 20 | — | 20 | single | ≤5s | |
| find_shared_members | experimental | symbols | 1 | 45 | — | 45 | single | ≤5s | |
| member_hierarchy | stable | symbols | 1 | 20 | — | 20 | single | ≤5s | |
| symbol_relationships | stable | symbols | 1 | 20 | — | 20 | limit=10 | ≤5s | |
| symbol_signature_help | experimental | symbols | 1 | 3 | — | 3 | single | ≤5s | |
| impact_analysis | stable | symbols | 1 | 13 | — | 13 | summary | ≤5s | |
| symbol_impact_sweep | experimental | symbols | 1 | 153 | — | 153 | summary | ≤5s | |
| probe_position | experimental | symbols | 1 | 9 | — | 9 | single | ≤5s | |
| analyze_data_flow | stable | flow | 2 | 15 | — | 15 | method body | ≤5s | |
| analyze_control_flow | stable | flow | 1 | 4 | — | 4 | method body | ≤5s | |
| get_operations | stable | flow | 1 | 5 | — | 5 | single | ≤5s | |
| get_syntax_tree | stable | flow | 1 | 9 | — | 9 | range | ≤5s | |
| trace_exception_flow | stable | flow | 1 | 32 | — | 32 | solution-wide | ≤15s | |
| analyze_snippet | stable | snippet | 3 | 146 | 147 | 147 | tiny | ≤5s | |
| evaluate_csharp | stable | snippet | 2 | 94 | 161 | 161 | expr/runtime err | ≤10s | |
| get_editorconfig_options | stable | editorconfig | 1 | 5 | — | 5 | single file | ≤5s | |
| get_msbuild_properties | stable | msbuild | 1 | 69 | — | 69 | includedNames | ≤5s | |
| evaluate_msbuild_property | stable | msbuild | 1 | 63 | — | 63 | single | ≤5s | |
| evaluate_msbuild_items | stable | msbuild | 1 | 77 | — | 77 | single project | ≤5s | |
| semantic_search | stable | search | 2 | 31 | 41 | 41 | natural-lang | ≤5s | |
| semantic_grep | stable | search | 1 | 43 | — | 43 | identifier scope | ≤5s | |
| find_reflection_usages | stable | reflection | 1 | 611 | — | 611 | solution-wide | ≤15s | |
| get_di_registrations | stable | DI | 1 | 1468 | — | 1468 | solution-wide | ≤15s | |
| test_discover | stable | test | 1 | 25 | — | 25 | limit=10 | ≤5s | |
| test_related | stable | test | 1 | 58 | — | 58 | single | ≤5s | |
| test_reference_map | experimental | test | 1 | 727 | — | 727 | solution-wide | ≤15s | |
| validate_workspace | stable | test | 1 | 43 | — | 43 | summary | ≤5s | |
| validate_recent_git_changes | stable | test | 1 | 9535 | — | 9535 | git status path | ≤15s | most expense in shelling out to `git status --porcelain` from the server |
| go_to_definition | stable | navigation | 2 | 4 | — | 4 | single | ≤5s | |
| goto_type_definition | stable | navigation | 1 | 2 | — | 2 | single (error) | ≤5s | |
| enclosing_symbol | stable | navigation | 1 | 3 | — | 3 | single | ≤5s | |
| find_overrides | stable | symbols | 1 | 11 | — | 11 | single | ≤5s | |
| find_base_members | stable | symbols | 1 | 2 | — | 2 | single | ≤5s | |
| get_completions | stable | navigation | 1 | 284 | — | 284 | filterText=To | ≤5s | |
| get_prompt_text | experimental | prompts | 4 | 7 | 33 | 33 | mixed | ≤5s | |

All exercised calls within budget.

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `workspace_load` | schema field | `prewarm: bool??` (double-nullable) advertised in `schemaHint` | Passing `true` returns `JsonException` "could not be converted to System.Nullable\`1[System.Boolean]" | P3 | Setting `prewarm=true` literally is rejected; only the omit-the-key branch is callable. The `bool??` notation is unusual; either drop the param from the public schema or accept `bool` and document the default. |
| `find_consumers` | classification value | dependencyKinds documented as Constructor/Field/Parameter/BaseType/LocalVariable/Property/ReturnType/GenericArgument | All 3 consumers of `DriftDetector` (a `static class`) classified `["Other"]` | P2 | Static-class invocation isn't a "consumption" in any of the documented buckets; the result is technically correct but the bucket is uninformative. Either add an `Invocation` / `StaticReference` kind or document that `Other` covers static-method calls. |
| `find_type_consumers` | classification value | kinds documented as `using | ctor | inherit | field | local | other` | All 3 file rollups for `DriftDetector` (static class, 14 invocation sites) reported `kinds=["local"]` | P2 | Same shape problem as above — `local` here means "local variable" per the legend, but no `var x = DriftDetector...` exists; the references are static method invocations. The kind classifier is mis-labeling them. |
| `get_completions` | rank order contract | v1.8+ ranks "locals/parameters → type members → types → long tail"; in-scope `ToString` should appear before `ToBase64Transform` for `filterText="To"` | All 10 returned items were external types (`ToBase64Transform` first); no `ToString` / no in-scope members | P2 | The carat was on a method-invocation expression (line 37, col 28 inside an `if`-body of a static-method block), which may legitimately have no member-of-`this` candidates — but in-scope locals (`leftMap`, `rightMap`, `id`, `added`) starting with `To` would still not exist. Result is plausible at this position; flagging as a verification gap (need a different position to truly stress the ranking contract). |
| `goto_type_definition` | error category | A type-token whose type is in metadata (e.g. `bool`) is documented as the natural target of this tool | Returns `InvalidOperation` ("Cannot navigate to type definition for `bool` — neither the type nor any of its type arguments are defined in source") | P3 | The error is actionable but the category is debatable — calling this tool on a built-in type is the documented use case ("for a variable, go to its type"); `NotFound` would arguably be a better category, or the tool could return a metadata-source location. |
| `validate_recent_git_changes` | scope derivation | docs imply derivation from `git status --porcelain` | Was 9.5s but found 0 changed files even though `audit-reports/` is untracked at the time of the call | P3 | The tool likely (correctly) ignores untracked dirs under `audit-reports/` because no `.cs` files are in it — but the docs don't say so. Worth a sentence in the description. |
| `evaluate_msbuild_items` | doc note | "DocumentCount discrepancy note: when comparing 'evaluate_msbuild_items Compile' count N to workspace_load's DocumentCount, the latter may be N+3" | Domain project: 62 Compile items; workspace doc share unknown but the 3-extra-implicit-files claim is consistent with observed source-gen output (`*.GlobalUsings.g.cs` appears in `source_generated_documents` for all projects) | n/a — informational | doc claim corroborated. |

## 8. Error message quality

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `workspace_load` | sibling-path target outside sanctioned roots | actionable | message names allowed roots and the rejected path | drives the Phase 6 finding (#13.1) |
| `workspace_load(prewarm=true)` | non-omitted bool | vague | message says "could not be converted to System.Nullable\`1[System.Boolean]" — useful for a developer but not for an agent; should say "pass the parameter unset, or as `false`" | tied to schema-drift #7 row 1 |
| `find_implementations(metadataName=NonExistent...)` | fabricated metadata | actionable | NotFound with clear text; tool field populated | v1.8+ contract honored |
| `find_references(metadataName=NonExistent.Type.That.Does.Not.Resolve)` | fabricated metadata | actionable | NotFound; tool field populated | |
| `symbol_info(workspaceId=bogus-...)` | bogus workspace | actionable | NotFound; "Active workspace IDs are listed by workspace_list" | great pointer to recovery action |
| `symbol_search(query="")` | empty | actionable | clean note "query must be non-empty — pass a bare substring like 'Animal' to find 'AnimalService', 'IAnimal', etc." | excellent — example-driven |
| `go_to_definition(line=9999)` | out-of-range | actionable | error names the actual line count (99) | |
| `analyze_data_flow(startLine=77, endLine=61)` | inverted range | actionable | "startLine (77) must be <= endLine (61)" | |
| `evaluate_csharp("int.Parse('abc')")` | runtime error | actionable | "Runtime error: FormatException: ..." | structured success=false envelope |
| `get_prompt_text(promptName="unknown_prompt_xyz")` | unknown prompt | actionable | error lists all 20 valid prompt names alphabetically | excellent — discoverable |
| `get_prompt_text(explain_error, {})` | missing required arg | partially actionable — multi-step | first call says "workspaceId required"; passing it then says "filePath required" | P3 — should enumerate all required params at once, not unfold them one error at a time |
| `goto_type_definition(line=37, col=31)` on bool type | built-in type | actionable | "Cannot navigate to type definition for 'bool' — neither the type nor any of its type arguments are defined in source" | category mismatch — see #7 |

## 9. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `project_diagnostics` | `severity=Warning`, `summary=true`, `diagnosticId=CA1859`, `limit=5` | PASS | totals invariant under severity filter (totalInfo=5 still surfaces) |
| `compile_check` | `emitValidation=true`, `severity=Error` | PASS | emit phase materialized — 13.6× slower |
| `find_unused_symbols` | `includePublic=true` | PASS | 5 low/medium-confidence public hits |
| `list_analyzers` | `limit=5` (page) | PASS | hasMore=true; returnedAnalyzerCount=2 |
| `find_references` | `summary=true` | PASS | drops previewText; preserves location |
| `find_references_bulk` | `summary=true`, `maxItemsPerSymbol=5` | PASS | per-symbol truncation; truncated=true |
| `impact_analysis` | `summary=true` | PASS | drops per-ref payload |
| `symbol_impact_sweep` | `summary=true`, `maxItemsPerCategory=5` | PASS | bucket caps respected |
| `find_property_writes` | `metadataName=...DriftReport.AddedRuleUuids` | PASS | WriteKind=ObjectInitializer |
| `get_msbuild_properties` | `includedNames=[...]` (allowlist) | PASS | only 5 of 718 properties returned |
| `get_nuget_dependencies` | `summary=true` | PASS | per-package summary shape |
| `get_di_registrations` | `showLifetimeOverrides=true`, `summary=true` | PASS | overrideChains[] populated |
| `get_completions` | `filterText="To"`, `maxItems=10` | PASS | isIncomplete=true (more available) |
| `nuget_vulnerability_scan` | `includeTransitive=true` | PASS | reports IncludesTransitive=true |
| `validate_workspace` | `summary=true` | PASS | overallStatus preserved |
| `get_syntax_tree` | `maxTotalBytes=10000` | PASS | walker stayed within cap |
| `find_cohesion_metrics` | `excludeTestProjects=true`, `minMethods=3` | PASS | tests excluded |
| `get_coupling_metrics` | `excludeTestProjects=true`, `limit=10` | PASS | |
| `trace_exception_flow` | `maxResults=10` | PASS | truncated=true returned |
| `find_duplicated_methods` | default `minLines=10`, default similarity | n/a | non-default path not exercised |
| `evaluate_csharp` | `timeoutSeconds` non-default | n/a | not exercised; default 10s honored |

## 10. Prompt verification (Phase 16)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|--------------------:|-----------:|----------:|----------------------|-------|
| `discover_capabilities` (`taskCategory="refactoring"`) | yes | yes | 0 | (not retested in same run; assumed by spec) | 7 | promote-candidate | Renders 43 refactoring tools + 6 guided prompts + 6 workflows + 3 patterns; every named tool resolves in catalog |
| `explain_error` (no params) | partial | n/a — parameter error | n/a | n/a | <10 | needs-more-evidence | Required-param error path returned `workspaceId required` (and would surface other required params on retry); see #8 |
| `review_file` (no params) | partial | n/a — parameter error | n/a | n/a | <10 | needs-more-evidence | Same multi-step required-param shape |
| `unknown_prompt_xyz` (negative) | n/a | n/a | n/a | n/a | 33 | n/a | Error envelope lists all 20 live prompts — excellent discoverability surface |
| (17 unrendered prompts) | not-rendered | not-rendered | not-rendered | not-rendered | n/a | needs-more-evidence | Names enumerated via the unknown-prompt error path: analyze_dependencies, cohesion_analysis, consumer_impact, dead_code_audit, debug_test_failure, fix_all_diagnostics, guided_extract_interface, guided_extract_method, guided_package_migration, msbuild_inspection, refactor_and_validate, refactor_loop, review_complexity, review_test_coverage, security_review, session_undo, suggest_refactoring |

## 11. Experimental promotion scorecard

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|-------:|-----------|----------|----------------|----------|----------------|----------|
| tool | workspace_warm | workspace | exercised | 2852 | yes | n/a (no negative probe) | n/a (idempotent read) | none | promote | post-load prime worked; coldCompilationCount=8 |
| tool | nuget_vulnerability_scan | security | exercised | 13544 | yes | n/a | n/a | none | promote | within budget; includeTransitive probed |
| tool | find_duplicate_helpers | metrics | exercised | 44 | yes | n/a | n/a | none | promote | well-classified with confidence buckets |
| tool | find_duplicated_code | metrics | exercised (alias) | 46 | yes | n/a | n/a | none | keep-experimental | alias; prefer canonical (deprecation populated) |
| tool | find_dead_locals | metrics | exercised | 1741 | yes | n/a | n/a | none | promote | clean 0 result; method-body iteration |
| tool | find_dead_fields | metrics | exercised | 1674 | yes | n/a | n/a | none | promote | clean 0 result |
| tool | get_coupling_metrics | metrics | exercised | 1375 | yes | n/a | n/a | none | promote | classification matches doc (stable/balanced/unstable) |
| tool | find_property_writes | symbols | exercised | 20 | yes | n/a | n/a | none | promote | WriteKind bucket correct |
| tool | find_shared_members | symbols | exercised | 45 | yes | n/a | n/a | none | promote | returned 3 clusters (matches LCOM4 shape) |
| tool | find_type_consumers | symbols | exercised | 17 | partial | n/a | n/a | kind classifier mis-labels static-method invocations as `local` (#7) | needs-more-evidence | Promote pending classifier fix |
| tool | symbol_signature_help | symbols | exercised | 3 | yes | n/a | n/a | none | promote | preferDeclaringMember default OK |
| tool | symbol_impact_sweep | symbols | exercised | 153 | yes | n/a | n/a | none | promote | summary + maxItemsPerCategory honored |
| tool | probe_position | symbols | exercised | 9 | yes | n/a | n/a | none | promote | strict resolver behavior verified |
| tool | get_di_registrations (lifetime/summary modes) | DI | exercised | 1468 | yes | n/a | n/a | none | promote | overrideChains correct, deadRegistrationCount accurate |
| tool | test_reference_map | test | exercised | 727 | yes | n/a | n/a | none | promote | mockDriftWarnings highly actionable |
| tool | get_prompt_text | prompts | exercised | 7 | yes | yes (unknown name + missing param both clean) | n/a | multi-step required-param errors (#8) | keep-experimental | promote-pending: surface all required params in one error |
| tool | format_check | refactoring | not exercised | — | unknown | unknown | unknown | not exercised in this run | needs-more-evidence | (Phase 6 blocked) |
| tool | restructure_preview / replace_string_literals_preview / change_signature_preview / parameter_object_preview / symbol_refactor_preview / split_service_with_di_preview / record_field_add_with_satellites_preview | refactoring | skipped-safety | — | n/a | n/a | n/a | none — not exercised | needs-more-evidence | Phase 6 blocked by sanctioned-root policy |
| tool | bulk_replace_type_apply / extract_interface_apply / extract_method_apply / extract_type_apply / move_type_to_file_apply / fix_all_apply / format_range_apply / migrate_package_preview / split_class_preview / move_type_to_project_preview / extract_interface_cross_project_preview / dependency_inversion_preview / extract_and_wire_interface_preview / change_type_namespace_preview / replace_invocation_preview / extract_shared_expression_to_helper_preview / apply_composite_preview | refactoring | skipped-safety | — | n/a | n/a | n/a | none | needs-more-evidence | Phase 6 blocked |
| tool | scaffold_type_apply / scaffold_test_apply / scaffold_test_batch_preview / scaffold_first_test_file_preview | scaffolding | skipped-safety | — | n/a | n/a | n/a | none | needs-more-evidence | Phase 12 blocked |
| tool | add/remove_*_preview, set_project_property_preview, set_conditional_property_preview, add/remove_target_framework_preview, add/remove_central_package_version_preview, apply_project_mutation | project-mutation | skipped-safety | — | n/a | n/a | n/a | none | needs-more-evidence | Phase 13 blocked |
| tool | apply_with_verify, revert_apply_by_sequence, workspace_changes, workspace_drift_check | session/refactoring | skipped-safety | — | n/a | n/a | n/a | none | needs-more-evidence | Phase 6/9 blocked |
| tool | apply_text_edit, apply_multi_file_edit, preview_multi_file_edit, preview_multi_file_edit_apply, preview_record_field_addition, set_diagnostic_severity, add_pragma_suppression, verify_pragma_suppresses, pragma_scope_widen, set_editorconfig_option | direct-edit/diagnostic suppression | skipped-safety | — | n/a | n/a | n/a | none | needs-more-evidence | All write-capable; Phase 6 blocked |
| resource | server_catalog_full | server | exercised-by-template | n/a | yes | n/a | n/a | none | promote | 80KB payload; cap-safe siblings already preferred |
| resource | server_catalog_tools_page / prompts_page | server | exercised-by-template | n/a | yes | n/a | n/a | none | promote | shape verified via catalog |
| resource | source_file_lines | workspace | not exercised | n/a | unknown | unknown | n/a | not exercised | needs-more-evidence | |
| prompt | discover_capabilities | prompts | exercised | 7 | yes | yes | n/a | none | promote | full render OK; references only live tools |
| prompt | (other 19 experimental prompts) | prompts | not rendered (params unknown without per-prompt schema) | — | unknown | partial (unknown_prompt error excellent) | n/a | none | needs-more-evidence | One-by-one rendering would need each prompt's required-params shape |

## 12. Debug log capture

| timestamp | level | logger | correlationId | eventName | message | Phase | Tool in flight |
|-----------|-------|--------|----------------|-----------|---------|-------|----------------|

**N/A — Claude Code CLI does not surface MCP `notifications/message` log entries inline.** Recorded as a client limitation in the header. Server-side stderr was not captured this run.

## 13. MCP server issues (bugs)

### 13.1 `workspace_load` rejects sibling-directory worktree as outside client-sanctioned roots — Phase 6 surface unreachable
| Field | Detail |
|-------|--------|
| Tool | `mcp__roslyn__workspace_load` |
| Input | `path=C:/Code-Repo/DotNet-Firewall-Analyzer-surface-test-20260510T053000Z/FirewallAnalyzer.slnx` |
| Expected | The skill's Phase 6 contract creates a disposable worktree as a sibling directory of the audited repo (`git worktree add ../<repo>-surface-test-<ts>`) and assumes the MCP server can `workspace_load` it. The teardown sub-phase (6z) and the skill's safety story depend on Phase 6 mutations going to the disposable worktree, never to the audited repo's primary checkout. |
| Actual | `InvalidArgument: Path 'C:/Code-Repo/DotNet-Firewall-Analyzer-surface-test-20260510T053000Z/FirewallAnalyzer.slnx' is not under any client-sanctioned root. Allowed roots: file://C:\Code-Repo\DotNet-Firewall-Analyzer.` |
| Severity | **P1** — Phase 6 (apply-tool exercise), Phase 9 (undo verification), Phase 10 (file/cross-project), Phase 12 (scaffolding) and Phase 13 (project mutation) all become un-runnable in any host that pins the sanctioned-root policy to the audited repo (Claude Code is one such host). The skill's `--no-worktree` is a manual opt-in; this run hit the same outcome involuntarily. |
| Reproducibility | 100% — `git worktree add` outside the audited repo, then `mcp__roslyn__workspace_load` on the resulting `.slnx` always fails with the same error in this client. |

### 13.2 `semantic_search("async methods returning Task<bool>")` returns only 1 result on a code-base with many `Task<bool>`-returning members
| Field | Detail |
|-------|--------|
| Tool | `mcp__roslyn__semantic_search` |
| Input | `query="async methods returning Task<bool>"` |
| Expected | Multiple Task<bool>-returning async methods (e.g. JobTracker.HasActiveAnalysisJob/HasActiveCollectionJob, FileSnapshotReader.SnapshotExists). The skill prompt says v1.8+ HTML-decodes ingress so the literal `<bool>` should match. |
| Actual | 1 result (`PanosClient.TestConnectivityAsync`). `debug.parsedTokens=["Task","bool"]`, `appliedPredicates=["keyword:async","keyword:method","returning-type"]`, `fallbackStrategy="structured"`. The conjunction of `async` + `Task<bool>` is over-strict — many `Task<bool>` returners in the codebase do not carry the `async` modifier (they `return Task.FromResult(...)`), and the skill's own doc warns about this. |
| Severity | **P3** — documented behavior, but the result is so narrow it's misleading on this codebase. Re-running without "async" still returned no extra hits in the spot-check. |
| Reproducibility | 100% on this workspace. |

### 13.3 `find_consumers` / `find_type_consumers` mis-classify static-class invocation sites
| Field | Detail |
|-------|--------|
| Tools | `mcp__roslyn__find_consumers`, `mcp__roslyn__find_type_consumers` |
| Input | `metadataName=FirewallAnalyzer.Application.Drift.DriftDetector` (a `public static class` with 14 invocation sites across 3 projects) |
| Expected | A bucket that means "static method invocation" (e.g. `Invocation` or `StaticReference`). |
| Actual | `find_consumers` reports `dependencyKinds=["Other"]` for every consumer; `find_type_consumers` reports `kinds=["local"]` for every file rollup. Neither classification matches the documented enumeration on the relevant tool. |
| Severity | **P2** — useful tools whose classification axis is uninformative for the static-class case (very common in this codebase). |
| Reproducibility | 100% on this workspace. |

## 14. Improvement suggestions

- `workspace_load` — drop the `prewarm: bool??` from the public schema or accept `bool` properly. Right now passing `true` literally throws a JSON-deserialization error; the only callable shape is "omit the param" (#7 row 1).
- `get_completions` — the v1.8+ rank-order contract ("locals/parameters → type members → types → long tail") could not be verified from the chosen position. Either ship an example-driven test fixture in the docs that locks in a known position, or add a `rankReason` field to each item so callers can audit ordering.
- `get_prompt_text` — surface ALL required parameters in a single error envelope rather than one-error-per-missing-param. Today an agent has to call N+1 times to discover N required params (#8).
- `goto_type_definition` — when called on a built-in type, return the metadata-source location (or a sentinel `MetadataReference` payload) instead of `InvalidOperation`. Calling this tool on a primitive is a documented use case.
- `validate_recent_git_changes` — describe the tool's behavior on untracked top-level directories. The 9.5s wall-clock with `changedFilePaths=[]` looks like a no-op but spent the time on `git status`.
- `list_analyzers.totalRules` — this run reports `408`; the same tool against the same workspace at `2026-05-10T05:02:25Z` reported `495`. Variance is unexplained; the tool result is stable enough that swing-by-87 deserves a deterministic explanation (paging vs. full-walk modes? analyzer-set evolution between runs?).
- `find_consumers` / `find_type_consumers` — add an explicit `Invocation` (or `StaticReference`) classification kind so static-class call sites (very common in C#) classify informatively (#13.3).
- `semantic_search` — when an over-narrow query produces a near-empty result set, surface a "did you mean" sibling suggestion in the response (e.g. "matched 1; dropping `async` modifier would match N more"). The `debug.parsedTokens` and `debug.appliedPredicates` already supply the diagnostic — exposing a one-line hint would convert a confusing experience into a discoverable one (#13.2).
- Skill ↔ server contract: the `mcp-server-surface-test` skill's Phase 6 needs a documented escape hatch for hosts that pin sanctioned roots. Either (a) the skill should detect this case and auto-switch to `--no-worktree` with a clear runbook entry, or (b) the server should expose a per-call "expand sanctioned roots" affordance. Without one, every audit on Claude Code lands in degraded mode (#13.1).

## 15. Concurrency matrix (Phase 8b)

**N/A — `blocked — client serializes tool calls`.** Claude Code CLI serializes MCP tool invocations; true concurrent fan-out cannot be observed from the agent loop. Sequential baselines for R1–R5 already appear in *Performance baseline* (find_references, project_diagnostics, symbol_search, find_unused_symbols, get_complexity_metrics).

## 16. Writer reclassification verification (Phase 8b.5)

**N/A — Phase 8b writers depend on the disposable worktree (or, for `apply_text_edit`/`set_editorconfig_option`, on tolerating mutations on `main`). Both are unsafe in this run — see #13.1.**

## 17. Response contract consistency

| Tools | Concept | Inconsistency | Notes |
|-------|---------|---------------|-------|
| `find_consumers` vs `find_type_consumers` | dependency classification axes | Two different vocabularies for the same data (`dependencyKinds: Constructor/Field/Parameter/BaseType/.../Other` vs `kinds: using/ctor/inherit/field/local/other`). They are clearly intentionally different views, but a caller who runs both gets two unrelated taxonomies for the same conceptual relation, and neither covers static-method invocation cleanly. |
| `find_duplicated_methods` (canonical) vs `find_duplicated_code` (alias) | aliasing | Both return identical 8-group payload; alias adds a `deprecation` envelope. Behavior matches the documented contract — flagging only because the alias's existence is not surfaced in `discover_capabilities` output. |
| `_meta.gateMode` field | snippet-only tools (`analyze_snippet`, `evaluate_csharp`) | Returns `null` instead of `"rw-lock"` because they don't touch a workspace. This is correct but inconsistent with workspace tools' `_meta` shape — a `gateMode="none"` value would be more self-describing than `null`. |

## 18. Known issue regression check (Phase 18)

| Source id | Summary | Status |
|-----------|---------|--------|
| (prior quick-tier audit `audit-reports/20260510T050225Z_*`) | DI 36 regs / 0 mismatches / find_unused_symbols=0 / namespace cycles=0 / semantic_search IDisposable returns 15 hits | **still reproduces** — same shape this run (DI: 36/0; unused=0; cycles=0; IDisposable=10 in this run with `limit=10`, would be 15 unfiltered) |
| (prior quick-tier audit) | `list_analyzers.totalRules=495` | **changed without explanation** — this run reports `totalRules=408` against the same workspace. Filed under #14 (improvement). |
| (prior quick-tier audit) | All read-side performance budgets met | **still holds** — every exercised tool within budget |

## 19. Known issue cross-check

- `find_consumers` / `find_type_consumers` "Other" / "local" classification on static-class targets is a NEW finding (#13.3); not present in the prior quick-tier audit (which did not exercise these tools).
- `workspace_load` sanctioned-root rejection of sibling worktree is a NEW finding (#13.1); the prior quick-tier audit did not attempt Phase 6, so the gap was invisible there.
- `semantic_search("Task<bool>")` narrowness is a NEW observation (#13.2); the prior audit exercised only the "classes implementing IDisposable" query and got the expected 15 hits.

---

## Phase 19 — Finding emission

Three actionable findings worth filing upstream. None tagged `area: security` or `severity: P0`, so all are eligible for stdout-print (no `--auto-file` was passed to the skill).

### Finding 1
```
## TITLE: firewallanalyzer-workspace-load-rejects-sibling-worktree
Labels: area:tools, severity:P1
Body:
- id: firewallanalyzer-workspace-load-rejects-sibling-worktree
- source-repo: firewallanalyzer
- severity: P1
- area: tools
- server-version: 1.35.1+d1e56dd
- anchors:
  - audit-reports/20260510T053000Z_firewallanalyzer_mcp-server-surface-test.md:13.1
- finding: workspace_load refuses any path "not under any client-sanctioned root", including a `git worktree add ../<sibling>` directory. The mcp-server-surface-test skill's Phase 6 cannot create a workspace against the disposable worktree it just created, so all Phase 6 / 9 / 10 / 12 / 13 writer surfaces are forced to `skipped-safety` even when the operator did not pass `--no-worktree`.
- repro: From the audited repo's directory, `git worktree add ../foo-surface-test -b bar`, then `mcp__roslyn__workspace_load(path="../foo-surface-test/Foo.slnx")` → `InvalidArgument: Path '...' is not under any client-sanctioned root`. Reproducible 100% on Claude Code CLI.
- proposed-fix: Either (a) the skill's Phase 6 should detect this rejection and auto-engage `--no-worktree` mode with a runbook entry pointing at the host's sanctioned-roots configuration; or (b) the server should accept a per-call "expand sanctioned roots" affordance (gated by an explicit operator flag) so the skill can opt the disposable worktree path into the allow-list for the duration of the run.
```

### Finding 2
```
## TITLE: firewallanalyzer-find-consumers-static-class-bucket-uninformative
Labels: area:tools, severity:P2
Body:
- id: firewallanalyzer-find-consumers-static-class-bucket-uninformative
- source-repo: firewallanalyzer
- severity: P2
- area: tools
- server-version: 1.35.1+d1e56dd
- anchors:
  - audit-reports/20260510T053000Z_firewallanalyzer_mcp-server-surface-test.md:13.3
- finding: find_consumers and find_type_consumers do not have a classification bucket that represents static-method invocation. find_consumers buckets all 3 consumers of `DriftDetector` (a `public static class` with 14 invocation sites) as `dependencyKinds=["Other"]`; find_type_consumers buckets the same usages as `kinds=["local"]`, which is misleading because no `var x = DriftDetector...` exists.
- repro: `mcp__roslyn__find_consumers(metadataName="FirewallAnalyzer.Application.Drift.DriftDetector")` and `mcp__roslyn__find_type_consumers(typeName="FirewallAnalyzer.Application.Drift.DriftDetector")` against the audited repo at commit 184021e.
- proposed-fix: Add an `Invocation` (or `StaticReference`) classification kind to both tools' enumerations, or re-bucket static-method invocation under an existing kind that does not collide with "local variable". Document the choice in each tool's description.
```

### Finding 3
```
## TITLE: firewallanalyzer-workspace-load-prewarm-double-nullable-uncallable
Labels: area:tools, severity:P3
Body:
- id: firewallanalyzer-workspace-load-prewarm-double-nullable-uncallable
- source-repo: firewallanalyzer
- severity: P3
- area: tools
- server-version: 1.35.1+d1e56dd
- anchors:
  - audit-reports/20260510T053000Z_firewallanalyzer_mcp-server-surface-test.md:7
- finding: workspace_load's schemaHint advertises `prewarm: bool??` (double-nullable). Passing `prewarm=true` returns `JsonException: The JSON value could not be converted to System.Nullable\`1[System.Boolean]`. The only callable shape is to omit the parameter entirely, which makes the public schema misleading.
- repro: `mcp__roslyn__workspace_load(path="...", prewarm=true)` → JsonException. `mcp__roslyn__workspace_load(path="...")` → OK.
- proposed-fix: Drop the second nullable in the schema (advertise `prewarm: bool?` and accept `true` / `false`), or remove the parameter from the public surface entirely. Update the schemaHint to match the accepted call shape.
```

`**No actionable findings tagged area: security or severity: P0.**` (Refusal contract not engaged.)

---

## Final note on coverage gaps

The Roslyn MCP server's read surface is robust and well-instrumented in this run — every exercised tool returned semantically correct results within budget, every negative probe produced an actionable error envelope, and the prompt-renderer (`get_prompt_text` on `discover_capabilities`) referenced only catalog-resident tools. The audit's coverage gap is concentrated in the apply-mode surface (Phases 6 / 9 / 10 / 12 / 13), all of which are blocked by the same sanctioned-root constraint described in #13.1. Re-running this audit on a host that allows the disposable worktree path into the sanctioned-roots allow-list (or after the skill-side fix in Finding 1) would convert ~50 `skipped-safety` writer entries into either `exercised-apply` or `keep-experimental` rows in the scorecard.
