# MCP Server Audit Report (DRAFT)

## 1. Header
- **Date:** 2026-05-16 (UTC)
- **Audited solution:** NetworkDocumentation.sln
- **Audited revision:** branch `main`, rev `b958fb0d78f80ef88634f9a25a7fdbaec71d2f9e`
- **Entrypoint loaded:** `C:\Code-Repo\DotNet-Network-Documentation\NetworkDocumentation.sln`
- **Flags:** `--full` (default — no `--no-worktree`, no `--single-agent`)
- **Subagent dispatch:** mixed — orchestrator owns Phase -1/0, Phase 6, Phase 9, Phase 19. `audit-phase-runner` (which by its description only supports phases 1/2/8/8b) is used for Phase 1+2 (G1) and Phase 8+8b (part of G5). Phases 3, 4, 5, 7, 10, 11, 12, 13, 14, 15, 16, 17, 18 run inline in the orchestrator because the `audit-phase-runner` agent does not support those phases.
- **Isolation (disposable worktree):** `C:\Code-Repo\DotNet-Network-Documentation\.worktrees\surface-test-20260516T062659Z` on branch `mcp-server-surface-test/20260516T062659Z`
- **Isolation baseline (primary checkout):** `` (empty — clean tree at run start; HEAD at b958fb0; no pre-existing untracked or modified files in primary checkout)
- **Teardown:** `partial — worktree directory survived (Windows file lock on empty .worktrees/surface-test-20260516T062659Z; admin record removed; branch deleted; primary checkout clean); manual rmdir required after session ends`
- **Client:** Claude Code (Opus 4.7, 1M context window) — Windows 11 Pro 10.0.26200
- **Workspace id:** `88bbb36db63e4797a3a3909be178cacb`
- **Workspace version / snapshot:** `1` / `88bbb36db63e4797a3a3909be178cacb:1`
- **Warm-up:** yes — `workspace_warm` ran inline via `workspace_load(prewarm=true)`; 9 projects warmed, 5 cold compilations, 4242 ms elapsed
- **Server:** roslyn-mcp 1.38.1+7b2c0b9 (.NET 10.0.8, Windows 10.0.26200, stdio host PID 39284, started 2026-05-16T06:23:20Z)
- **Catalog version:** 2026.04
- **Roslyn / .NET:** Roslyn 5.3.0.0, .NET 10.0.8
- **Live surface:** tools 111/58, resources 9/4, prompts 0/20 (parityOk=true; registered: 169 tools / 13 resources / 20 prompts)
- **Scale:** 9 projects, 477 documents (all targeting `net10.0`)
- **Repo shape:** multi-project (8 library/exe + 1 test); single-target `net10.0`; analyzers loaded (analyzersReady=true); `Directory.Build.props` + `global.json` present; **no Central Package Management** (`Directory.Packages.props` absent); **no multi-targeting** (all projects single-target net10.0); source generators not observed in project graph; **tests present** (NetworkDocumentation.Tests, isTestProject=true, references all other projects); DI presence TBD in Phase 11 (`get_di_registrations`). Test project uses a flat structure with subdirs mirroring source project names + `WriteGolden.runsettings`.
- **Prior issue source:** `ai_docs/backlog.md` (backlog file present in repo), `ai_docs/reports/mcp-server-audit-report.md` (prior MCP server audit), prior crashed-run draft `audit-reports/20260516T055611Z_networkdocumentation_mcp-server-surface-test.md.draft.md` (Phase 0 complete only — Phase 6 + later phases never ran). The crashed-run draft is preserved on disk as prior context; this run starts a fresh ledger.
- **Debug log channel:** `no` — Claude Code does not surface `notifications/message` from the MCP server's `McpLoggingProvider`.
- **Report path note:** Lives under `<audited-repo-root>/audit-reports/`. Cross-repo handoff to upstream Roslyn-Backed-MCP via Phase 19 finding emission (default `--output-mode=findings`; maintainer detected → auto-file via `gh issue create`, except P0/security stdout-only per refusal contract).

## 2. Coverage summary
| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Scoped-but-skipped | Notes |
|------|----------|--------|--------------|-----------|------------------|--------------|--------------------|----------------|---------|-------------------|-------|
| tool | navigation | 25 | 5 | 24 | 0 | 0 | 0 | 0 | 0 | 6 | nav family broadly exercised |
| tool | analysis | 18 | 9 | 21 | 2 | 0 | 0 | 0 | 0 | 4 | metrics + flow + symbol exhaustively probed |
| tool | refactoring | 30 | 18 | 38 | 8 | 6 | 1 | 1 | 0 | 4 | preview-apply chains in Phase 6 |
| tool | project-mutation | 8 | 7 | 11 | 2 | 0 | 2 | 0 | 0 | 0 | CPM tools skipped — no Directory.Packages.props |
| tool | test | 6 | 1 | 7 | 0 | 0 | 0 | 0 | 0 | 0 | full test surface exercised on disposable worktree |
| tool | scripting | 2 | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | both incl. timeout/runtime-error |
| tool | workspace+lifecycle | 12 | 3 | 13 | 0 | 0 | 0 | 0 | 0 | 2 | revert_apply_by_sequence + workspace_close exercised |
| tool | concurrency | 0 | 1 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | client serializes; partial blocked |
| tool | misc | 10 | 14 | 8 | 0 | 0 | 1 | 1 | 0 | 14 | apply_composite_preview skipped-safety |
| resource | server | 5 | 2 | 5 | 0 | 0 | 0 | 0 | 0 | 2 | catalog/full not directly read |
| resource | workspace | 4 | 2 | 6 | 0 | 0 | 0 | 0 | 0 | 0 | line-range + diagnostics exercised |
| prompt | all | 0 | 20 | 18 | 0 | 0 | 0 | 0 | 0 | 0 | 2 prompts FAILED on payload overflow (analyze_dependencies, review_test_coverage) |
| **TOTAL** | | **120** | **84** | **161** | **12** | **6** | **4** | **3** | **1** | **32** | tool counts may double-count tools exercised across multiple phases |

Live catalog totals: 169 tools (111 stable + 58 experimental) + 13 resources (9 stable + 4 experimental) + 20 prompts (0 stable + 20 experimental). Counts above reflect distinct tool/resource/prompt names exercised at least once.

## 3. Coverage ledger
*(condensed to one row per tool/resource/prompt; status reflects the strictest observation across phases)*

**Read-side tools — all stable nav/analysis (28 entries, all `exercised`):** `server_info`, `workspace_load`, `workspace_health`, `workspace_status`, `workspace_list`, `workspace_warm`, `workspace_reload`, `project_graph`, `project_diagnostics`, `compile_check`, `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan`, `list_analyzers`, `diagnostic_details`, `get_complexity_metrics`, `get_cohesion_metrics`, `get_coupling_metrics`, `find_unused_symbols`, `find_duplicated_methods`, `find_duplicate_helpers`, `find_duplicated_code`, `find_dead_locals`, `find_dead_fields`, `get_namespace_dependencies`, `get_nuget_dependencies`, `suggest_refactorings`, `symbol_search`, `symbol_info`, `document_symbols`, `type_hierarchy`, `find_implementations`, `find_references`, `find_consumers`, `find_type_usages`, `callers_callees`, `find_property_writes`, `member_hierarchy`, `symbol_relationships`, `symbol_signature_help`, `impact_analysis`, `get_source_text`, `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `get_syntax_tree`, `analyze_snippet`, `evaluate_csharp`, `get_editorconfig_options`, `get_msbuild_properties`, `evaluate_msbuild_property`, `evaluate_msbuild_items`, `find_reflection_usages`, `get_di_registrations`, `source_generated_documents`, `go_to_definition`, `goto_type_definition`, `enclosing_symbol`, `get_symbol_outline`, `get_completions`, `find_references_bulk`, `find_base_members`, `find_overrides` (FAIL payload overflow), `workspace_changes`, `semantic_search`, `build_workspace`, `build_project`, `test_discover`, `test_related`, `test_related_files`, `test_run`, `test_coverage`, `validate_workspace`, `probe_position` (experimental).

**Experimental tools exercised:** `find_type_consumers`, `find_shared_members`, `find_type_mutations`, `symbol_impact_sweep`, `trace_exception_flow`, `test_reference_map`, `validate_recent_git_changes`, `restructure_preview`, `replace_string_literals_preview`, `change_signature_preview`, `symbol_refactor_preview`, `change_type_namespace_preview`, `preview_record_field_addition`, `record_field_add_with_satellites_preview`, `extract_shared_expression_to_helper_preview`, `apply_with_verify`, `apply_project_mutation`, `set_project_property_preview`, `set_conditional_property_preview`, `add_target_framework_preview`, `semantic_grep`, `probe_position`.

**Write-side tools exercised-apply (12):** `rename_apply`, `format_range_apply`, `format_document_apply`, `organize_usings_apply`, `format_check`, `apply_code_action`, `apply_text_edit`, `apply_multi_file_edit`, `preview_multi_file_edit_apply`, `remove_dead_code_apply`, `extract_method_apply`, `scaffold_type_apply`, `scaffold_test_apply`, `delete_file_apply`, `apply_with_verify`, `apply_project_mutation`, `revert_last_apply`, `revert_apply_by_sequence`, `set_diagnostic_severity`, `add_pragma_suppression`, `set_editorconfig_option`, `workspace_close`.

**Skipped tools:** `add_central_package_version_preview`, `remove_central_package_version_preview` (skipped-repo-shape — no CPM); `apply_composite_preview` (scoped-but-skipped — destructive despite name, not exercised on safety grounds); `remove_target_framework_preview` (skipped — only-exercised-as-preview because add was preview-only); plus several lower-priority experimental tools listed in *scoped-but-skipped* below.

**Scoped-but-skipped (orchestrator context budget) — ~32 tools mostly experimental:** `extract_protocol`, `dependency_inversion_preview`, `extract_and_wire_interface_preview`, `split_service_with_di_preview`, `replace_invocation_preview`, `move_file_preview`/`apply`, `move_type_to_file_apply`, `move_type_to_project_apply`, `extract_interface_apply`, `extract_type_apply`, `extract_interface_cross_project_apply`, `bulk_replace_type_preview`/`apply`, `split_class_preview` (preview-only, no apply), `pragma_scope_widen`, plus other v1.38 surface entries (see promotion scorecard for the experimental subset). All `scoped-but-skipped` entries are scored `needs-more-evidence` in the promotion scorecard per spec.

**Resources (10 exercised of 13):** `server_catalog`, `resource_templates`, `workspaces`, `workspaces_verbose`, `workspace_status`, `workspace_status_verbose`, `workspace_projects`, `workspace_diagnostics` (FLAG — count mismatch w/ project_diagnostics in this run), `source_file`, `source_file_lines` (experimental). **Scoped-but-skipped:** `server_catalog_full` (large; sister `server_catalog` exercised), `server_catalog_tools_page` (paged form of the same content), `server_catalog_prompts_page` (paged form of the same content).

**Prompts (18 PASS of 20 in live catalog):** `explain_error`, `suggest_refactoring`, `review_file`, `discover_capabilities`, `debug_test_failure`, `refactor_and_validate`, `fix_all_diagnostics`, `guided_package_migration`, `guided_extract_interface`, `security_review`, `dead_code_audit`, `review_complexity`, `cohesion_analysis`, `consumer_impact`, `guided_extract_method`, `msbuild_inspection`, `session_undo`, `refactor_loop`. **FAIL:** `analyze_dependencies` (63 KB overflow), `review_test_coverage` (121 KB overflow).

## 4. Verified tools (working)
- `server_info` — version/catalogVersion/connection/parityOk reported; state=`idle` pre-load (valid per state machine), advances to `ready` after `workspace_load`; surface counts 169/13/20 match the catalog summary.
- `roslyn://server/catalog` resource — returned 9 stable + 4 experimental resources, summary matches `server_info.surface`; toolCount=169, promptCount=20.
- `roslyn://server/resource-templates` resource — 13 templates enumerated (9 stable + 4 experimental), matches catalog.
- `workspace_load(prewarm=true)` — 9 projects, 477 documents, 0 errors/warnings, prewarmed 9 projects (5 cold compilations) in 4242 ms; load wall-clock 10084 ms (heldMs 14319 ms — includes prewarm; queuedMs 5 ms; cacheHit=false).
- `workspace_health` — `isReady=true`, `isStale=false`, 0 errors/warnings; returned cleanly without `_meta` (expected on info-only call).
- `project_graph` — 9 projects with project-reference edges; elapsedMs=2 ms (cache hit after prewarm); confirms NetworkDocumentation.Cli is the apex referencing all others; Tests references everything (typical fan-in pattern).
- `project_diagnostics` (Phase 1) — 0 errors, 0 warnings, 1075 Info across 28 distinct ids (top: Meziantou MA0003/MA0076/MA0001); paginated cleanly; summary mode in 16579 ms, page in 1 ms.
- `compile_check` (Phase 1) — 0 CS diagnostics across 9 projects; default 80 ms, `emitValidation=true` 2682 ms (~33× delta confirms emit path).
- `security_diagnostics` (Phase 1) — 0 findings, Puma + SecurityCodeScan present; 2551 ms.
- `security_analyzer_status` (Phase 1) — NetAnalyzers + Puma + SecurityCodeScan installed, no missing recommendations; 2362 ms.
- `nuget_vulnerability_scan` (Phase 1) — 0 CVEs across 9 projects; 13413 ms.
- `list_analyzers` (Phase 1) — 35 analyzers / 764 rules; project-filter + paging both work cleanly; 28 ms.
- `diagnostic_details` (Phase 1) — analyzer help link returned for MA0007 hit; `supportedFixes=[]` with guidance to get_code_actions (expected for MA-series); 119 ms.
- `get_complexity_metrics` (Phase 2) — top hits: ShowVersionParser.BuildVersionRecord (cyc=28, MI=44.07), NxosVersionParser.ParseNxos (cyc=25, nesting=4), DiagramComparisonTests test (cyc=24, 206 LoC); 123 ms.
- `get_cohesion_metrics` (Phase 2) — top LCOM4: PipelineProgressReporter=24 (logger sink — intentional), DeviceClassifierService=5, SnapshotStore=5; 179 ms.
- `get_coupling_metrics` (Phase 2) — **tool exists** (contradicts older backlog row `coupling-metrics-tool`); 10 unstable types Ce=34 (WebInventoryBuilderTests / CollectionEndpoints / SearchEndpoints); 4713 ms.
- `find_unused_symbols(includePublic=false)` — 2 hits: CliProgram (NamedType, high), CredentialManager.Overrides (Property, high); 2470 ms.
- `find_unused_symbols(includePublic=true)` — 7 hits (5 additional public, mostly low/medium confidence); 3577 ms.
- `find_duplicated_methods` — 10 clusters; largest TempDir.Dispose×5 (test helpers), NeighborParserHelpers.TryGetValue×3; 136 ms.
- `find_duplicate_helpers` — 10 high-confidence BCL-wrappers in Diagrams/Parsers (EscapeInlineScript/Style etc.); no BCL-wrapper false-positives; 47 ms.
- `find_duplicated_code` — deprecation banner populated (`canonicalName=find_duplicated_methods`); same 10 clusters; 86 ms.
- `find_dead_locals` — 10 hits across Diagrams/Web/Tests (region, stpDomainTag, cacheKey, extension, deviceMap×3, isStack); 1087 ms.
- `find_dead_fields` — 2 hits: `_payloadDir` in SnapshotManifestManager (safelyRemovable=false, ConstructorWrite blocker — accurate), `_factory` in WebApiContractTests (safelyRemovable=true); 1452 ms.
- `get_namespace_dependencies(circularOnly=true)` — **FLAG** — 2 cycles inside NetworkDocumentation.Core: Models↔Routing and Config↔Snapshots; 124 ms.
- `get_nuget_dependencies(summary=true)` — 28 distinct packages, all single-version; Meziantou + Puma + SecurityCodeScan present on all 9 projects; 766 ms.
- `suggest_refactorings` — top 15 high-severity complexity suggestions; tool sequence accurate (analyze_data_flow → extract_method_*); 427 ms.
- `symbol_search` (Phase 3) — cold first call 627 ms then 0-74 ms; resolves NetworkDocumentation types cleanly.
- `symbol_info` (Phase 3) — happy path 0-2 ms; negative probe (`NetworkDocumentation.Nonexistent.NoSuchType`) returns typed NotFound with actionable message.
- `document_symbols` (Phase 3) — fast (1-11 ms); correctly enumerates ShowVersionParser children incl. BuildVersionRecord at lines 125-168.
- `type_hierarchy` (Phase 3) — clean for both static utilities (empty) and concrete services (IDeviceClassifierService, ISnapshotStore populated correctly).
- `find_implementations` (Phase 3) — 0 hits on static classes, 1 hit each on the IDeviceClassifierService and ISnapshotStore — interface completeness verified.
- `find_references` (Phase 3, summary mode) — counts: ShowVersionParser=47, SnapshotStore=56, DeviceClassifierService=28, PipelineProgressReporter=9, NxosVersionParser=1; consistent with `find_type_usages` totals.
- `find_consumers` (Phase 3) — typed dependency-kind classification works; see drift table for vocabulary mismatch vs `find_type_consumers`.
- `find_type_consumers` (Phase 3, experimental) — file-rollup view useful but uses divergent vocabulary (`ctor` vs `ObjectCreation`).
- `find_shared_members` (Phase 3, experimental) — SnapshotStore returns 4 shared private members explaining its LCOM4=5; static utilities correctly return 0.
- `find_type_mutations` (Phase 3, experimental) — strong: `SnapshotStore.Save`/`Delete` classified `mutationScope=IO`, `callerPhase=PostConstruction`; all 4 other targets correctly 0 mutations.
- `find_type_usages` (Phase 3) — totals match `find_references`; uses `ObjectCreation` vocabulary (third variant in trio mismatch).
- `callers_callees` (Phase 3) — auto-resolves method symbol from position; SnapshotStore.Save reports 28 callers / 19 callees.
- `find_property_writes` (Phase 3) — handles NamedType target gracefully (`resolvedSymbolKind=NamedType`, hint to `find_references`).
- `member_hierarchy` (Phase 3) — auto-promotes return-type tokens to enclosing method, then surfaces method's return-type's interface — flagged (undocumented chain).
- `symbol_relationships` (Phase 3) — flagged: includes generator partials in `definitions` (see drift table).
- `symbol_signature_help` (Phase 3) — flagged: returns bare `null` for resolvable Save method metadataName (see bugs).
- `impact_analysis` (Phase 3) — 1-56 refs / 1-5 projects across targets; summary mode fast.
- `probe_position` (Phase 3, experimental) — Keyword + Punctuation probes both clean.
- `symbol_impact_sweep` (Phase 3, experimental) — summary mode + `maxItemsPerCategory` cap honored.
- `get_source_text` (Phase 4) — line range reads; totalLineCount returned.
- `analyze_data_flow` (Phase 4) — handles repeated-name locals (disambiguated by `(decl line N)` suffix).
- `analyze_control_flow` (Phase 4) — strong on ExcelCellValue.ToCellValue (6 returns enumerated with expressionText); flagged "incomplete" warning on void methods (see bugs).
- `get_operations` (Phase 4) — column points at token, not enclosing expression — matches UX-003 docs.
- `get_syntax_tree` (Phase 4) — `TruncationNotice` with actionable budget guidance when caps fire.
- `trace_exception_flow` (Phase 4, experimental) — strong: filter expansion captured in bodyExcerpt; `scopeProjectFilter` works.
- `analyze_snippet(kind=expression)` on `1 + 2` (Phase 5) — PASS — isValid=true, 0 errors; ephemeral `Snippet.Evaluate()` symbol declared; 78 ms.
- `analyze_snippet(kind=program)` on small class (Phase 5) — PASS — declared symbols listed (NamedType: TestClass, Property: Value, Method: Add); 60 ms.
- `analyze_snippet(kind=statements)` on broken code `int x = "hello";` (Phase 5) — PASS — CS0029 with `startColumn=9` (user-relative — pre-fix was 66; v1.7+ regression-free); 81 ms.
- `analyze_snippet(kind=returnExpression)` on `return 42;` (Phase 5) — PASS — value-bearing return allowed (`Snippet.Run()` returns object?); 58 ms.
- `evaluate_csharp("Enumerable.Range(1, 10).Sum()")` (Phase 5) — PASS — result=55 (System.Int32); 68 ms; `appliedScriptTimeoutSeconds=10` (default).
- `evaluate_csharp(multi-line sort script)` (Phase 5) — PASS — result="1,1,2,3,3,4,5,5,6,9"; 25 ms.
- `evaluate_csharp("int.Parse(\"abc\")")` (Phase 5) — PASS — graceful FormatException, `success=false`, `error` populated; 51 ms.
- `evaluate_csharp(infinite loop, timeoutSeconds=3)` (Phase 5) — PASS — clean timeout at 13007 ms (3 s budget + 10 s grace per docs); 1/8 abandoned worker thread reported; server did not hang.
- `get_editorconfig_options` (Phase 7) — disk-source readout matches the on-disk file 1:1 incl. Phase 6f-ii's MA0007 entry; 8 ms.
- `set_editorconfig_option(CA1822.severity, suggestion)` (Phase 7) — `createdNewFile=false`, key appears in next read; 1 ms.
- `get_msbuild_properties(NetworkDocumentation.Core, includedNames=[...5 props])` — returns 5/719 with `appliedFilter` echoed; 102 ms; demonstrates `includedNames` allowlist works.
- `evaluate_msbuild_property(NetworkDocumentation.Core, TargetFramework)` — `evaluatedValue="net10.0"`; 56 ms; matches `get_msbuild_properties` value (consistent).
- `evaluate_msbuild_items(NetworkDocumentation.Core, Compile)` — 105 items; 80 ms.
- `workspace_reload` (Phase 8) — 2841 ms post-Phase-6 refresh.
- `build_workspace` (Phase 8) — surfaced Phase-6 MA0150 leak (compile_check is CS-only and missed it).
- `build_project` (Phase 8) — separate per-project build, ~1-14 s depending on project size.
- `test_discover` (Phase 8) — 1203 tests in NetworkDocumentation.Tests; limit=500 overflowed 250 KB MCP cap.
- `test_related_files` (Phase 8) — partial resolve (2 of 4 paths returned "did not resolve to a workspace document" — flagged).
- `test_related` (Phase 8) — 0 hits on a renamed-since-Phase-6 method; correctly reports locator did not resolve.
- `test_run` (Phase 8) — exit 0 with "No test matches" when filter uses test_discover's flat fqdn vs xunit's infixed fqdn (flagged).
- `test_coverage` (Phase 8) — cascade-failed due to MA0150 build error; recovered after pragma added.
- `test_reference_map` (Phase 8, experimental) — 370 covered / 2357 uncovered, coveragePercent 13.6%, `mockDriftWarnings=[]`; 2019 ms.
- `validate_workspace` (Phase 8) — `overallStatus="analyzer-error"` with `errorDiagnostics=[]` and `compileResult.success=true` (flagged drift).
- `validate_recent_git_changes` (Phase 8, experimental) — auto-derived 9 changed files from git status; same overallStatus drift.
- `find_references` Phase 8b — Device hot symbol >598 hits paged.
- `revert_last_apply` (Phase 8b.5 + Phase 9) — single-slot semantics confirmed; "No operation to revert" returned cleanly on exhaustion.
- `set_diagnostic_severity(CA1822, suggestion)` (Phase 8b.5) — 1 ms, .editorconfig updated.
- `add_pragma_suppression(MA0150 @ PipelineProgressReporter.cs:97)` (Phase 8b.5) — 5 ms.
- `move_type_to_file_preview(InterfaceRecord)` (Phase 10) — PASS — 24-line record extracted from DeviceRecords.cs into new InterfaceRecord.cs with namespace declaration; clean unified diff.
- `move_type_to_project_preview(VlanHelpers → Parsers)` (Phase 10) — correctly refused with `circular dependency` error (Parsers references Core).
- `extract_interface_cross_project_preview` (Phase 10) — same circular dependency refusal — consistent guardrail.
- `split_class_preview(DeviceClassifierService → DeviceClassifierService.MacVendor.cs)` (Phase 10, experimental) — clean preview marking class partial + new partial file with the GetMacVendor member.
- `migrate_package_preview(Meziantou.Analyzer → Meziantou.Analyzer)` (Phase 10, experimental) — InvalidOperation: "No project references to Meziantou.Analyzer were found in the loaded workspace" — but `get_nuget_dependencies` clearly shows Meziantou.Analyzer present on all 9 projects (analyzer-package via `OutputItemType="Analyzer"`). Flagged: tool misses analyzer-only package references.
- `create_file_preview` (Phase 10) — clean refusal with InvalidOperation when target file already exists.
- `delete_file_preview(SurfaceTestProbe.cs)` (Phase 10) — clean delete diff.
- `delete_file_apply` (Phase 10) — `appliedFiles=[<deleted-path>]` populated correctly (contrast with `symbol_refactor_preview` apply path).
- `semantic_search` HTML-decoded ingress (Phase 11) — `query="async methods returning Task&lt;bool&gt;"` returns identical 1 hit as `query="async methods returning Task<bool>"`; HTML-decode contract met. Debug payload shows parsed tokens + appliedPredicates + fallbackStrategy.
- `semantic_search("classes implementing IDisposable")` (Phase 11) — 10 hits (all IHostedService backends + TempDir test helper); structured parse.
- `semantic_grep` (Phase 11, experimental) — bogus pattern returns clean `count=0, items=[]`; no crash.
- `find_reflection_usages` (Phase 11) — 12 hits across `typeof`/`Activator.CreateInstance`/`Type.GetProperty`; usageKind-grouped output is well-structured.
- `get_di_registrations(summary=true, showLifetimeOverrides=true)` (Phase 11) — 30 registrations, 28 Singletons + 2 Scoped, 1 override chain on `ICollectionJobQueue` with 1 dead registration.
- `source_generated_documents` (Phase 11) — 12 documents (RegexGenerator × 6 projects, LoggerMessage × 2, OpenApiXmlCommentSupport × 3, GlobalUsings × 1) — `project_graph` doesn't surface these; cross-tool view useful.
- `scaffold_type_preview` (Phase 12) — namespace-folder path resolution: namespace `NetworkDocumentation.Core.SurfaceTest` lands at `SurfaceTest/SurfaceTestTargetType.cs`; default `internal sealed class`; 3 ms.
- `scaffold_test_preview(DeviceClassifierService.GetMacVendor)` (Phase 12) — emits ctor + `[Fact]` stub with parameterless `new DeviceClassifierService()` + comment "Target method has parameters — add arguments"; correctly identifies method has parameters.
- `scaffold_test_batch_preview(3 targets)` (Phase 12) — ONE composite token covers 3 generated test files; verified contract.
- `scaffold_first_test_file_preview(SnapshotStore)` (Phase 12) — comprehensive fixture: ctor with `default(ILogger?)!` placeholder for the constructor's ILogger dep, 8 Fact methods (one per public method); preview only, didn't apply.
- `scaffold_type_apply` (Phase 12) — `success=true`, `appliedFiles=[<new-path>]`; 487 ms.
- `scaffold_test_apply` (Phase 12, first attempt) — **stale-token rejection** after workspace changed mid-flight: `"Preview token is invalid, expired, or stale because the workspace changed since the preview was generated. Please create a new preview."` — strong negative-probe evidence.
- `scaffold_test_apply` (Phase 12, after re-preview) — PASS, `success=true`, `appliedFiles=[<new-path>]`; 505 ms.
- `workspace_changes` (Phase 6m / 9) — 35 entries by Phase 9 end; ordered by sequence; descriptions/tool names/affected files/timestamps all populated; revert_last_apply does NOT add a new sequence (file reverts, history preserves the original).
- `apply_text_edit` (Phase 9 audit-only apply A/B/C) — clean diff + sequence recorded.
- `revert_last_apply` (Phase 9) — undoes A, then C in single-slot fashion; second consecutive call returns `reverted: false, message: "No operation to revert..."` — clean exhaustion.
- `revert_apply_by_sequence(34)` (Phase 9) — non-tip revert: undoes B (seq 34) without disturbing C (seq 35) because their affected files don't overlap; returns `reverted: true, affectedFiles=[<file>], sequenceNumber=34`.
- `revert_apply_by_sequence(9999)` (Phase 9 negative probe) — clean error: `reverted: false, reason: "unknown-sequence", message: "No revert snapshot exists for that sequence number..."`.

## 5. Phase 6 apply-tool exercise summary
- **Disposable worktree path:** `C:\Code-Repo\DotNet-Network-Documentation\.worktrees\surface-test-20260516T062659Z`
- **Disposable branch:** `mcp-server-surface-test/20260516T062659Z`
- **Worktree workspaceId:** `2c31d2c6fffa4288ba47f09df1b0b848` (separate from the primary `88bbb36db63e4797a3a3909be178cacb`)
- **Scope:** 6a (skipped — no loadable fix providers), 6b (rename), 6c (skipped — interface exists; FLAG: tool didn't refuse), 6d (preview-only refusal exercised), 6e (format), 6f (skipped — no loadable fixers), 6f-ii (diagnostic severity + pragma), 6g (code actions), 6h (text edits incl. negative stale-token probe), 6i (dead-code removal), 6j (extract method — substituted target after false-negative), 6k (advanced refactor previews — 8+ tools), 6l (apply_with_verify clean + rolled_back paths), 6m (workspace_changes).
- **Apply-tool calls (key preview→apply pairs exercised):**
  - `rename_preview` → `rename_apply` (CredentialManager.Overrides → OverridesLegacy); MutatedSymbol present.
  - `format_document_preview` → `format_document_apply` (InventorySummaryBuilder.cs — real apply after `format_check` flagged 6 violations).
  - `format_range_preview` / `format_range_apply` (no-op on already-formatted region).
  - `organize_usings_preview` / `organize_usings_apply` (no-op).
  - `set_diagnostic_severity` (MA0007 → silent in `.editorconfig`).
  - `add_pragma_suppression` (MA0007 in QueryParser.cs) → `verify_pragma_suppresses` (suppresses=true).
  - `get_code_actions` → `preview_code_action` → `apply_code_action` (DebuggerDisplay attribute added — introduced CS8603 fixed by follow-up edit).
  - `apply_text_edit(verify=true)` (correctly caught the CS8603 as errors_introduced); fixup `apply_text_edit` clean.
  - `apply_multi_file_edit(verify=true)` (2-file coordinated edit).
  - `preview_multi_file_edit` + `preview_multi_file_edit_apply`; negative stale-token probe rejected cleanly.
  - `remove_dead_code_preview` → `remove_dead_code_apply` (OverridesLegacy removed).
  - `extract_method_preview` → `extract_method_apply` (NxosVersionParser.ParseNxos → SetVendorAndOs after substitution from BuildVersionRecord false-negative).
  - `change_signature_preview` (op=add/rename/reorder) → applied one via `preview_multi_file_edit_apply`.
  - `symbol_refactor_preview` composite (rename) → applied via `preview_multi_file_edit_apply` (FLAG: empty appliedFiles in response).
  - `apply_with_verify` — exercised both `status=applied` and `status=rolled_back` paths.
- **Verification:** Final `compile_check` PASS (0 errors / 0 warnings / 9 of 9 projects); `workspace_changes` shows 21 ordered entries with descriptions/tool names/file paths/timestamps.
- **Teardown outcome:** TBD — recorded after Phase 9 (which adds one more audit-only apply on top of the Phase 6 stack).
- **Files mutated in worktree (not in primary checkout):**
  - `.editorconfig`
  - `src/NetworkDocumentation.Collection/NetworkDocumentation.Collection/Credentials/CredentialManager.cs`
  - `src/NetworkDocumentation.Core/NetworkDocumentation.Core/Utils/InventorySummaryBuilder.cs`
  - `src/NetworkDocumentation.Core/NetworkDocumentation.Core/Utils/QueryParser.cs`
  - `src/NetworkDocumentation.Cli/NetworkDocumentation.Cli/PipelineProgressReporter.cs`
  - `src/NetworkDocumentation.Reports/NetworkDocumentation.Reports/ReportData.cs`
  - `src/NetworkDocumentation.Web/NetworkDocumentation.Web/ReportEndpoints.cs`
  - `src/NetworkDocumentation.Cli/NetworkDocumentation.Cli/PipelineOrchestrator.cs`
  - `src/NetworkDocumentation.Parsers/NetworkDocumentation.Parsers/Commands/NxosVersionParser.cs`

## 6. Performance baseline (`_meta.elapsedMs`)
| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| workspace_load | stable | workspace | 1 | 10084 | 10084 | 10084 | 9p/477d, prewarm=true | ≤15 s scan | within budget; heldMs 14319 ms incl. prewarm |
| workspace_warm (inline via load) | stable | workspace | 1 | 4242 | 4242 | 4242 | 9 projects | n/a | 5 cold compilations |
| workspace_health | stable | workspace | 1 | n/a | n/a | n/a | 1 ws | ≤5 s | _meta missing on info-only call (expected) |
| project_graph | stable | workspace | 1 | 2 | 2 | 2 | 9 projects | ≤5 s | cache-hit after prewarm; queuedMs 0 |
| project_diagnostics | stable | diagnostics | 3 | 1 | 16579 | 16579 | 9p / 1075 hits | ≤15 s scan | summary mode dominates; page reads ≤2 ms |
| compile_check | stable | diagnostics | 4 | 60 | 2682 | 2682 | 9p | ≤15 s | `emitValidation=true` adds ~33× cost |
| security_diagnostics | stable | security | 1 | 2551 | 2551 | 2551 | 9p | ≤15 s | within budget |
| security_analyzer_status | stable | security | 1 | 2362 | 2362 | 2362 | 9p | ≤15 s | within budget |
| nuget_vulnerability_scan | stable | security | 1 | 13413 | 13413 | 13413 | 9p, transitive=false | ≤15 s | within budget; dominated by `dotnet list package` per-project |
| list_analyzers | stable | diagnostics | 2 | 15 | 28 | 28 | 9p / 35 analyzers | ≤5 s | well under budget |
| diagnostic_details | stable | diagnostics | 2 | 3276 | 6433 | 6433 | 2 probes (1 happy, 1 not-found) | ≤5 s | not-found 6.4 s — FLAG slow path |
| get_complexity_metrics | stable | metrics | 1 | 123 | 123 | 123 | 9p / top 15 | ≤15 s | excellent |
| get_cohesion_metrics | stable | metrics | 1 | 179 | 179 | 179 | 9p | ≤15 s | excellent |
| get_coupling_metrics | stable | metrics | 1 | 4713 | 4713 | 4713 | 9p / top 10 | ≤15 s | within budget |
| find_unused_symbols | stable | dead-code | 2 | 3023 | 3577 | 3577 | 9p / 2-7 hits | ≤15 s | within budget |
| find_duplicated_methods | stable | duplication | 1 | 136 | 136 | 136 | 9p / 10 clusters | ≤15 s | well under |
| find_duplicate_helpers | stable | duplication | 1 | 47 | 47 | 47 | 9p / 10 hits | ≤15 s | well under |
| find_duplicated_code | stable | duplication | 1 | 86 | 86 | 86 | 9p / 10 clusters | ≤15 s | deprecated alias, well under |
| find_dead_locals | stable | dead-code | 2 | 601 | 1087 | 1087 | 9p / 10 hits | ≤15 s | within budget |
| find_dead_fields | stable | dead-code | 1 | 1452 | 1452 | 1452 | 9p / 2 hits | ≤15 s | within budget |
| get_namespace_dependencies | stable | architecture | 1 | 124 | 124 | 124 | 9p / 2 cycles | ≤15 s | well under |
| get_nuget_dependencies | stable | dependencies | 1 | 766 | 766 | 766 | 9p / 28 packages | ≤15 s | within budget |
| suggest_refactorings | stable | metrics | 1 | 427 | 427 | 427 | 9p / 15 hits | ≤15 s | within budget |
| symbol_search | stable | navigation | 6 | 74 | 627 | 627 | 9p / NetworkDocumentation types | ≤5 s | cold first call dominates |
| symbol_info | stable | navigation | 6 | 0 | 2 | 2 | metadataName | ≤5 s | excellent |
| document_symbols | stable | navigation | 5 | 1 | 11 | 11 | per file | ≤5 s | excellent |
| type_hierarchy | stable | navigation | 5 | 0 | 4 | 4 | metadataName | ≤5 s | excellent |
| find_implementations | stable | navigation | 5 | 0 | 9 | 9 | metadataName | ≤5 s | excellent |
| find_references | stable | navigation | 5 | 2 | 14 | 14 | summary mode | ≤5 s | excellent |
| find_consumers | stable | analysis | 5 | 1 | 6 | 6 | metadataName | ≤15 s | well under |
| find_type_consumers | experimental | analysis | 5 | 1 | 9 | 9 | typeName | ≤15 s | well under |
| find_shared_members | experimental | analysis | 5 | 1 | 12 | 12 | metadataName | ≤15 s | well under |
| find_type_mutations | experimental | analysis | 5 | 2 | 42 | 42 | metadataName | ≤15 s | within budget |
| find_type_usages | stable | analysis | 5 | 1 | 12 | 12 | metadataName | ≤15 s | well under |
| find_property_writes | stable | analysis | 1 | 3 | 3 | 3 | NamedType target (negative) | ≤5 s | well under |
| callers_callees | stable | navigation | 3 | 1 | 8 | 8 | position | ≤5 s | excellent |
| member_hierarchy | stable | navigation | 1 | 117 | 117 | 117 | position | ≤5 s | within budget |
| symbol_relationships | stable | navigation | 2 | 9 | 16 | 16 | position+promotion test | ≤5 s | excellent |
| symbol_signature_help | stable | navigation | 2 | 2 | 2 | 2 | metadataName / position+preferDeclaring | ≤5 s | excellent (but null body — see bugs) |
| impact_analysis | stable | analysis | 5 | 2 | 9 | 9 | metadataName summary | ≤15 s | well under |
| probe_position | experimental | navigation | 2 | 2.5 | 4 | 4 | position | ≤5 s | excellent |
| symbol_impact_sweep | experimental | analysis | 5 | 1 | 111 | 111 | metadataName summary + cap | ≤15 s | well under |
| get_source_text | stable | navigation | 4 | 0 | 3 | 3 | line range | ≤5 s | excellent |
| analyze_data_flow | stable | flow | 4 | 2 | 8 | 8 | method range | ≤5 s | excellent |
| analyze_control_flow | stable | flow | 4 | 1 | 6 | 6 | method range | ≤5 s | excellent |
| get_operations | stable | flow | 4 | 0 | 3 | 3 | position | ≤5 s | excellent |
| get_syntax_tree | stable | flow | 4 | 2 | 5 | 5 | line range + maxBytes | ≤5 s | excellent |
| trace_exception_flow | experimental | flow | 2 | 15 | 18 | 18 | exceptionType / scopeFilter | ≤15 s | well under |
| analyze_snippet | stable | scripting | 4 | 70 | 81 | 81 | expression/program/statements/returnExpression | ≤10 s budget | well under |
| evaluate_csharp | stable | scripting | 4 | 60 | 13007 | 13007 | expression/multi-line/runtime-error/timeout | ≤10 s + 10 s grace | timeout fires cleanly at budget+grace |

## 7. Schema vs behaviour drift
| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| project_diagnostics | response-field invariant | Per tool doc: "Totals count the full queried scope and ignore the severity filter" — `totalDiagnostics` should remain at unfiltered total | `totalDiagnostics` collapses to 0 when `severity=Error` is applied to a workspace with 0 errors / 1075 info, even though top-level `totalErrors/totalWarnings/totalInfo` remain correctly invariant | P2 | deterministic across two runs (Phase 1 step 2) |
| symbol_relationships vs find_implementations | `definitions` dedupe semantics | Both default to dedupe of generator partials (find_implementations default `includeGeneratedPartials=false`) | symbol_relationships includes both source + generator partial; find_implementations dedupes — same workspace, opposite default | P3 | reliable |
| find_consumers / find_type_consumers / find_type_usages | classification vocabulary | Unified or cross-referenced kind labels for the same site | Three vocabularies for the same `new T(...)` site: `MethodParameter\|Other` / `ctor\|other` / `ObjectCreation` | P3 | reliable |
| analyze_control_flow | warning gating | Suppress incomplete-range warning when range covers full body | Warning fires on void methods with zero return statements even when range is the full body | P3 | reliable |
| member_hierarchy | promotion-trace surface | Document the auto-promotion (caret → method → return type → interface) and gate via `preferDeclaringMember` | Promotion happens silently; no trace; surfaces interface of return type when caret is on the method's return-type token | P3 | undocumented |
| symbol_signature_help | error-envelope shape | Typed NotFound envelope matching `symbol_info` shape | Bare JSON `null` (no `_meta`, no error category) for metadataName lookups that other tools resolve | P2 | reliable |

## 8. Error message quality
| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| diagnostic_details | bad position (line=1,col=1) for valid (id, file) | actionable | none — error message is good | found=false reply is clear; only complaint is the 6.4 s latency (FLAG separate) |
| compile_check | `projectName=NoSuchProject` | actionable | none — error message is excellent | `restoreHint` explains naming rule + recovery tool |
| symbol_search | `query=DoesNotExistType_NegativeProbe_XYZ` | actionable | none | clean `count=0, totalCount=0, hasMore=false` shape |
| symbol_info | `metadataName=NetworkDocumentation.Nonexistent.NoSuchType` | actionable | none | typed `category=NotFound`, exceptionType set, recovery hint names `workspace_load` |
| find_property_writes | `metadataName=PipelineProgressReporter` (NamedType, not Property) | actionable | none | `resolvedSymbolKind=NamedType` + hint pointing to `find_references` |
| symbol_signature_help | resolvable `metadataName=SnapshotStore.Save(...)` | unhelpful | return typed error envelope | bare JSON `null` returned silently |

## 9. Parameter-path coverage
| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| project_diagnostics | `severityFilter`, `offset+limit`, `summary=true` | exercised | invariant claim violated for `totalDiagnostics` (P2 above) |
| compile_check | `severity`, `file`, `emitValidation=true`, `projectName=<bad>` | exercised | all paths produce sensible outputs |
| list_analyzers | `projectName`, `offset+limit` | exercised | filter + paging work |
| diagnostic_details | bad position (negative probe) | exercised | found=false is clear |
| find_unused_symbols | `includePublic=true` (vs false) | exercised | both happy-path |
| find_dead_locals | `projectFilter=<single-project>` (vs unfiltered) | exercised | both happy-path |
| get_coupling_metrics | `limit=10` | exercised | sole shape probed |
| get_nuget_dependencies | `summary=true` | exercised | reduced payload |
| get_namespace_dependencies | `circularOnly=true` | exercised | surfaced cycles |

## 10. Prompt verification (Phase 16)
(populated in Phase 16)

## 11. Experimental promotion scorecard

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | find_type_consumers | analysis | exercised | 1 | yes | yes | n/a (read) | vocabulary mismatch w/ peers (13.6) | **keep-experimental** | Phase 3 cross-checked vs find_consumers + find_type_usages — same site labeled `ctor` vs `MethodParameter\|Other` vs `ObjectCreation`. Functional but inconsistent. |
| tool | find_shared_members | analysis | exercised | 1 | yes | yes | n/a | none | **promote** | Phase 3: SnapshotStore returned 4 clusters explaining LCOM4=5; static utility types correctly return 0; performance excellent. |
| tool | find_type_mutations | analysis | exercised | 2 | yes | yes | n/a | none | **promote** | Phase 3: SnapshotStore correctly classified Save/Delete `mutationScope=IO callerPhase=PostConstruction`; static utilities 0 mutations. |
| tool | symbol_impact_sweep | analysis | exercised | 1 | yes | yes | n/a | none | **promote** | Phase 3: summary mode + `maxItemsPerCategory` cap honored across 5 types ranging 1-56 refs. |
| tool | trace_exception_flow | flow | exercised | 15 | yes | yes | n/a | none | **promote** | Phase 4: catch-filter expansion captured in bodyExcerpt; `scopeProjectFilter` works; clean output on 10-result negative-probe set. |
| tool | test_reference_map | test | exercised | 2019 | yes | yes | n/a | none | **promote** | Phase 8: 370 covered / 2357 uncovered / coveragePercent=13.6; `mockDriftWarnings=[]`; shape matches docs. |
| tool | validate_recent_git_changes | test | exercised | 10107 | yes | partial | n/a | inherits 13.11 overallStatus drift | **keep-experimental** | Phase 8: auto-derived 9 changed files cleanly; but `overallStatus="analyzer-error"` with empty `errorDiagnostics` (same issue as validate_workspace). |
| tool | restructure_preview | refactoring | exercised-preview-only | 9 | yes | yes | preview only | none | **keep-experimental** | Phase 6k: clean preview emitting 23 matches; apply path not exercised in this run. |
| tool | replace_string_literals_preview | refactoring | exercised-preview-only | 5 | yes | yes | preview only | none | **keep-experimental** | Phase 6k: 2 matches; apply not exercised. |
| tool | change_signature_preview | refactoring | exercised-apply | 51 | yes | partial | yes | spec/server drift on op=reorder | **keep-experimental** | Phase 6k: op=add, op=remove, op=rename, op=reorder all tested; reorder IS supported (contradicts spec). |
| tool | symbol_refactor_preview | refactoring | exercised-apply | 72 | partial | yes | partial | empty appliedFiles on apply (13.10) | **keep-experimental** | Phase 6k: composite rename landed but `appliedFiles=[]` in response. |
| tool | change_type_namespace_preview | refactoring | exercised-preview-only | 115 | partial | yes | preview only | incomplete preview (13.9) | **deprecate** OR **major-rework** | Phase 6k: preview misses consumer-side `using` updates — would not compile. |
| tool | preview_record_field_addition | refactoring | exercised-preview-only | 168 | yes | yes | preview only | none | **promote** | Phase 6k: graceful no-impact response; `suggestedTasks` populated. |
| tool | record_field_add_with_satellites_preview | refactoring | exercised-preview-only | 9 | yes | yes | preview only | none | **promote** | Phase 6k: graceful "no satellite coverage" with `patternDetectionReason`. |
| tool | extract_shared_expression_to_helper_preview | refactoring | exercised-preview-only | 6 | yes | yes | preview only | none | **promote** | Phase 6k: correctly refused single-occurrence case with redirect to `extract_method_preview`. |
| tool | apply_with_verify | apply | exercised-apply | 923 | yes | yes | yes | none | **promote** | Phase 6l: both `status=applied` and `status=rolled_back` paths exercised. |
| tool | apply_project_mutation | project-mutation | exercised-apply | 2413 | yes | yes | yes | none | **promote** | Phase 13: forward + reverse round-trip on csproj — byte-for-byte identical post-revert. |
| tool | set_project_property_preview | project-mutation | exercised | 81 | partial | yes | preview only | 4-property allowlist undocumented (13.21) | **keep-experimental** | Phase 13: works within allowlist; allowlist scope too narrow. |
| tool | set_conditional_property_preview | project-mutation | exercised | 1 | partial | yes | preview only | same as above | **keep-experimental** | same allowlist constraint. |
| tool | add_target_framework_preview | project-mutation | exercised-preview-only | 114 | yes | yes | preview only | none | **promote** | Phase 13: clean diff promoting single → multi-target. |
| tool | remove_target_framework_preview | project-mutation | scoped-but-skipped | n/a | n/a | n/a | n/a | not exercised | **needs-more-evidence** | apply not executed in this run. |
| tool | add_central_package_version_preview | project-mutation | skipped-repo-shape | n/a | n/a | n/a | n/a | no CPM in repo | **needs-more-evidence** | repo has no `Directory.Packages.props`. |
| tool | remove_central_package_version_preview | project-mutation | skipped-repo-shape | n/a | n/a | n/a | n/a | no CPM in repo | **needs-more-evidence** | repo has no `Directory.Packages.props`. |
| tool | semantic_grep | navigation | exercised | 100 | yes | yes | n/a | identifier-scope qualified-name limitation (13.19) | **keep-experimental** | Phase 11: returns clean 0 on bogus and on cross-token patterns; documentation should clarify identifier scope semantics. |
| tool | probe_position | navigation | exercised | 2 | yes | yes | n/a | none | **promote** | Phase 3 + 17: small sharp tool, Keyword/Punctuation/Whitespace-equivalent probes all clean. |
| tool | split_class_preview | refactoring | exercised-preview-only | 2 | yes | yes | preview only | none | **keep-experimental** | Phase 10: clean partial-class split preview; apply not exercised. |
| tool | migrate_package_preview | refactoring | exercised-preview-only | 3 | partial | partial | preview only | misses analyzer-only packages (13.18) | **keep-experimental** | Phase 10: Meziantou.Analyzer (present per get_nuget_dependencies) not detected; needs PackageReference-walk widened. |
| tool | move_type_to_project_preview | refactoring | exercised-preview-only | 323 | yes | yes | preview only | none | **promote** | Phase 10: correctly refused on cycle creation. |
| tool | extract_interface_cross_project_preview | refactoring | exercised-preview-only | 647 | yes | yes | preview only | none | **promote** | Phase 10: same cycle-refusal guardrail. |
| tool | create_file_preview / create_file_apply | refactoring | exercised-apply | 4 | yes | yes | yes | none | **promote** | Phase 10: round-trip with delete cleanup. |
| tool | delete_file_preview / delete_file_apply | refactoring | exercised-apply | 3 | yes | yes | yes | none | **promote** | Phase 10: `appliedFiles` populated cleanly. |
| tool | move_type_to_file_preview | refactoring | exercised-preview-only | 4298 | yes | yes | preview only | none | **keep-experimental** | Phase 10: clean preview; apply not exercised. |
| tool | scaffold_type_preview / scaffold_type_apply | scaffolding | exercised-apply | 3 / 487 | yes | yes | yes | none | **promote** | Phase 12: namespace-folder path resolution + apply + appliedFiles populated. |
| tool | scaffold_test_preview / scaffold_test_apply | scaffolding | exercised-apply | 22 / 505 | yes | yes | yes | none | **promote** | Phase 12: stale-token negative path also verified. |
| tool | scaffold_test_batch_preview | scaffolding | exercised-preview-only | 5 | yes | yes | preview only | none | **promote** | Phase 12: one composite token for 3 files — contract met. |
| tool | scaffold_first_test_file_preview | scaffolding | exercised-preview-only | 14 | yes | yes | preview only | none | **promote** | Phase 12: comprehensive fixture (ctor + 8 smoke tests) for SnapshotStore. |
| tool | apply_composite_preview | refactoring | scoped-but-skipped | n/a | n/a | n/a | n/a | "destructive despite name" caution | **needs-more-evidence** | not exercised per safety contract. |
| tool | revert_apply_by_sequence | lifecycle | exercised-apply | 2509 | yes | yes | yes | none | **promote** | Phase 9: non-tip revert succeeded; out-of-range probe returned clean error. |
| resource | source_file_lines | workspace | exercised | n/a | yes | yes | n/a | none | **promote** | Phase 15: marker comment present; invalid range returns structured error. |
| resource | server_catalog_full | server | scoped-but-skipped | n/a | n/a | n/a | n/a | scope-skipped (sister exercised) | **needs-more-evidence** | not directly read this run. |
| resource | server_catalog_tools_page | server | scoped-but-skipped | n/a | n/a | n/a | n/a | scope-skipped | **needs-more-evidence** | not directly read this run. |
| resource | server_catalog_prompts_page | server | exercised | 1 | yes | yes | n/a | none | **promote** | Phase 16: paginated read exercised. |
| prompt | analyze_dependencies | prompts | FAIL | n/a | n/a | n/a | n/a | 63 KB overflow (13.22) | **deprecate** OR **major-rework** | needs server-side cap. |
| prompt | review_test_coverage | prompts | FAIL | n/a | n/a | n/a | n/a | 121 KB overflow (13.23) | **deprecate** OR **major-rework** | needs server-side cap. |
| prompt | explain_error | prompts | exercised | 5182 | yes | yes | n/a | renders even when diagnostic body null | **keep-experimental** | could short-circuit on missing diagnostic. |
| prompt | debug_test_failure | prompts | exercised | 13546 | yes | yes | n/a | no-failure rendering awkward | **keep-experimental** | could short-circuit on zero failures. |
| prompt | cohesion_analysis | prompts | exercised | 2 | yes | yes | n/a | empty LCOM4 array on a project that has cohesion issues | **keep-experimental** | filter may be over-tight. |
| prompt | suggest_refactoring | prompts | exercised | 2 | yes | yes | n/a | none | **promote** | strong actionability. |
| prompt | review_file | prompts | exercised | 267 | yes | yes | n/a | none | **promote** | live diagnostics + symbols. |
| prompt | discover_capabilities | prompts | exercised | 0 | yes | yes | n/a | none | **promote** | byte-identical idempotent. |
| prompt | refactor_and_validate | prompts | exercised | 81 | yes | yes | n/a | none | **promote** | concrete tool sequence. |
| prompt | fix_all_diagnostics | prompts | exercised | 92 | yes | yes | n/a | none | **promote** | diagnostic-ID-grouped, batching guidance. |
| prompt | guided_package_migration | prompts | exercised | 4346 | yes | yes | n/a | none | **promote** | handles zero-result case gracefully. |
| prompt | guided_extract_interface | prompts | exercised | 1 | yes | yes | n/a | none | **promote** | 7-step workflow. |
| prompt | security_review | prompts | exercised | 4602 | yes | yes | n/a | none | **promote** | clean zero-finding case. |
| prompt | dead_code_audit | prompts | exercised | 7 | yes | yes | n/a | none | **promote** | |
| prompt | review_complexity | prompts | exercised | 2 | yes | yes | n/a | none | **promote** | |
| prompt | consumer_impact | prompts | exercised | 3 | yes | yes | n/a | none | **promote** | |
| prompt | guided_extract_method | prompts | exercised | 0 | yes | yes | n/a | none | **promote** | |
| prompt | msbuild_inspection | prompts | exercised | 0 | yes | yes | n/a | none | **promote** | |
| prompt | session_undo | prompts | exercised | 0 | yes | yes | n/a | none | **promote** | byte-identical idempotent. |
| prompt | refactor_loop | prompts | exercised | 0 | yes | yes | n/a | none | **promote** | 4-stage loop. |

**Summary counts:** promote=29 · keep-experimental=14 · needs-more-evidence=6 · deprecate-or-major-rework=3 · blocked=0.

## 12. Debug log capture
**N/A — client did not surface MCP log notifications.** Claude Code does not forward the McpLoggingProvider's `notifications/message` channel to the assistant. No structured ILogger entries available for this run.

## 13. MCP server issues (bugs)

### 13.1 project_diagnostics — `totalDiagnostics` collapses under severity filter
| Field | Detail |
|-------|--------|
| Tool | `project_diagnostics` |
| Input | `severityFilter="Error"` on workspace with 0 errors / 1075 info |
| Expected | Per the tool description: "Totals … count the full queried scope and ignore the severity filter" — `totalDiagnostics` should remain at 1075 (unfiltered total) so the filtered-vs-unfiltered ratio stays inspectable |
| Actual | `totalDiagnostics` collapses to 0 while top-level `totalErrors/totalWarnings/totalInfo` correctly remain 0/0/1075; only the per-call array narrows. |
| Severity | P2 |
| Reproducibility | deterministic across two runs (G1 Phase 1 step 2) |

### 13.2 diagnostic_details — slow not-found path (6.4 s)
| Field | Detail |
|-------|--------|
| Tool | `diagnostic_details` |
| Input | bad position (line=1, column=1) for a valid (id, file) tuple |
| Expected | sub-second response (no expensive lookup needed once the (id, file) lookup determines the position is not the diagnostic site) |
| Actual | 6433 ms wall-clock for a confirmed `found=false` reply |
| Severity | P3 |
| Reproducibility | deterministic |

### 13.3 symbol_signature_help — returns bare `null` for resolvable method metadata
| Field | Detail |
|-------|--------|
| Tool | `symbol_signature_help` |
| Input | `metadataName=NetworkDocumentation.Core.Snapshots.SnapshotStore.Save(NetworkDocumentation.Core.Models.NetworkInventory, string, string?)` (a method that `symbol_info`, `find_references`, `callers_callees` all resolve cleanly) |
| Expected | typed error envelope (matching `symbol_info` NotFound shape) OR signature payload with `displaySignature` |
| Actual | bare JSON `null` (no `_meta`, no error category, no body) |
| Severity | P2 |
| Reproducibility | reliable |

### 13.4 symbol_relationships — `definitions` includes generator-emitted partials (no dedupe)
| Field | Detail |
|-------|--------|
| Tool | `symbol_relationships` |
| Input | metadataName of a partial class that has a Regex-generator partial (e.g. SnapshotStore) at line/col of the class declaration |
| Expected | dedupe to user-authored partial — matches `find_implementations` default `includeGeneratedPartials=false` behaviour on the same workspace |
| Actual | `definitions` array returns 2 entries: source (`SnapshotStore.cs:25`) AND `RegexGenerator.g.cs:377` — schema inconsistency with `find_implementations` |
| Severity | P3 |
| Reproducibility | reliable |

### 13.5 analyze_control_flow — misleading "incomplete" warning on full-body void methods with zero returns
| Field | Detail |
|-------|--------|
| Tool | `analyze_control_flow` |
| Input | full body of a void method with no `return` statements (e.g. `NxosVersionParser.ParseNxos` lines 32-112, range covers `{`…`}` of the method body) |
| Expected | no warning, OR a sentinel like `void-method, no explicit returns` instead of generic "incomplete" |
| Actual | Warning text "Control-flow results may be incomplete for this line range. Prefer a range that covers full statement blocks within a single method body." fires even when the range IS the full body |
| Severity | P3 |
| Reproducibility | reliable |

### 13.6 find_consumers / find_type_consumers / find_type_usages — three vocabularies for the same constructor-call site
| Field | Detail |
|-------|--------|
| Tool | `find_consumers`, `find_type_consumers`, `find_type_usages` (cross-tool drift) |
| Input | identical metadataName / typeName for a class with `new T(...)` callsites (PipelineProgressReporter) |
| Expected | unified or at minimum cross-referenced kind labels for the same site |
| Actual | `find_consumers` → `MethodParameter|Other`; `find_type_consumers` → `ctor|other`; `find_type_usages` → `ObjectCreation`. Three vocabularies for what is the exact same site. |
| Severity | P3 |
| Reproducibility | reliable |

### 13.7 extract_method_preview — false-negative "same block scope" rejection on valid single-statement `if`-block selection
| Field | Detail |
|-------|--------|
| Tool | `extract_method_preview` |
| Input | `ShowVersionParser.BuildVersionRecord` lines 127-135 (a single complete `if`-block at method top-level scope), multiple end-column choices tried (10, 9, 11) |
| Expected | preview generated — selection IS a single complete statement at the method's outer block scope |
| Actual | every variation rejected with `"All selected statements must be in the same block scope."`. Substitution to NxosVersionParser.ParseNxos:33-34 (two top-level property assignments) succeeded — confirming the tool works on simpler selections but over-rejects multi-line `if`-statement bodies whose end-column lands on the closing brace |
| Severity | P1 — high false-negative rate on a canonical refactor target |
| Reproducibility | reliable |

### 13.8 extract_interface_preview — silently produces duplicate interface when type already implements one
| Field | Detail |
|-------|--------|
| Tool | `extract_interface_preview` |
| Input | `DeviceClassifierService` (which already implements `IDeviceClassifierService`) with members `IsComputeDevice, GetMacVendor`, target interface name `IDeviceClassifierServiceProbe` |
| Expected | refuse with actionable message, OR warn that the type already exposes a covering interface |
| Actual | preview generated normally — would have produced a second redundant interface on apply |
| Severity | P2 |
| Reproducibility | reliable |

### 13.9 change_type_namespace_preview — does not auto-add `using` directives for cross-namespace consumers
| Field | Detail |
|-------|--------|
| Tool | `change_type_namespace_preview` |
| Input | type `VersionParseState` moved from `NetworkDocumentation.Parsers.Commands` → `NetworkDocumentation.Parsers.Commands.State` (sub-namespace inside same project) |
| Expected | preview includes consumer-side `using NetworkDocumentation.Parsers.Commands.State;` additions OR description warns about manual consumer rewrites |
| Actual | preview only edits the type's own namespace declaration; multiple consumer files in the parent namespace would not compile after apply |
| Severity | P2 — incomplete preview produces non-compiling state |
| Reproducibility | reliable |

### 13.10 symbol_refactor_preview + preview_multi_file_edit_apply — empty `appliedFiles` on success
| Field | Detail |
|-------|--------|
| Tool | `symbol_refactor_preview` (composite preview) + `preview_multi_file_edit_apply` |
| Input | composite operations array including a rename, applied via the shared `IPreviewStore` apply tool |
| Expected | response `success=true` with `appliedFiles=[<list of files>]` matching what landed on disk |
| Actual | `success=true, appliedFiles=[]` despite the rename actually landing on disk (verified via subsequent `symbol_search`) |
| Severity | P2 — misleading response shape; callers cannot programmatically confirm what mutated |
| Reproducibility | reliable |

### 13.11 validate_workspace — `overallStatus="analyzer-error"` while `errorDiagnostics=[]` and `compileResult.success=true`
| Field | Detail |
|-------|--------|
| Tool | `validate_workspace`, `validate_recent_git_changes` (inherits the same shape) |
| Input | both (a) real `changedFilePaths` listing Phase-6-touched files, (b) fabricated `changedFilePaths` entry |
| Expected | `overallStatus` ∈ {`clean`, `compile-error`, `analyzer-error`, `test-failure`} reflects the worst diagnostic — when `errorDiagnostics=[]` and `compileResult.success=true`, status should be `clean` |
| Actual | overallStatus=`analyzer-error` with `errorDiagnostics=[]` and `compileResult.success=true` on BOTH probes; verdict does not match payload. `build_workspace` (separate tool) DID surface a real MA0150 error in the same worktree, so the underlying analyzer state is consistent with "there exists an error somewhere", but the validate_workspace output never names a diagnostic — callers cannot act on `analyzer-error` without details. |
| Severity | P2 — verdict/payload mismatch on a bundle tool documented as "post-edit verification" |
| Reproducibility | reliable |

### 13.12 find_unused_symbols — false positive on assembly entry-point class
| Field | Detail |
|-------|--------|
| Tool | `find_unused_symbols(includePublic=false)` |
| Input | NetworkDocumentation.Cli — has `CliProgram` class wrapping the entry-point |
| Expected | convention-invoked entry-point class (Program / Main / startup shape) is filtered out (per the tool's documented "convention-invoked shapes" exclusion) |
| Actual | `CliProgram` flagged as unused at high confidence; the type is the assembly's invocation root (its `static Task<int> Main` is `[Program]` entry-point) |
| Severity | P3 — false positive on a load-bearing class; risky to act on without checking csproj OutputType=Exe |
| Reproducibility | reliable |

### 13.13 test_discover / test_run — fqdn drift between discovery and assembly view
| Field | Detail |
|-------|--------|
| Tool | `test_discover` + `test_run` |
| Input | filter using fqdn from `test_discover` results |
| Expected | filter applied to `test_run` returns the matching tests |
| Actual | `test_discover` returns flat fqdns like `NetworkDocumentation.Tests.CliOptionsTests`, but the compiled xunit testcase fqdn has the file-folder infix `NetworkDocumentation.Tests.Cli.CliOptionsTests`; passing the discover-shape filter to `test_run` produces "No test matches the given testcase filter" with **exit 0** — a silent zero-hits result easy to misread as "no failures" |
| Severity | P2 — silent-zero-hits is a correctness trap for callers chaining discover → run |
| Reproducibility | reliable |

### 13.14 revert_last_apply — single-slot semantics undocumented in apply_multi_file_edit
| Field | Detail |
|-------|--------|
| Tool | `revert_last_apply` |
| Input | after `apply_multi_file_edit(2 files, verify=true)`, then `set_editorconfig_option(...)`, then `revert_last_apply` twice |
| Expected | second `revert_last_apply` undoes the multi-file edit (after the first undoes the editorconfig write) — implied by "atomic batch undo" in apply_multi_file_edit description |
| Actual | second revert returns "No operation to revert" — the slot is single-slot per workspace; the editorconfig write overwrote the multi-edit's snapshot |
| Severity | P3 — semantics are documented for `revert_last_apply` ("single-slot"), but `apply_multi_file_edit`'s atomic-batch claim is misleading without the cross-reference |
| Reproducibility | reliable |

### 13.15 test_run failureEnvelope — build failures classified `errorKind=Unknown`
| Field | Detail |
|-------|--------|
| Tool | `test_run` |
| Input | test_run with a filter when underlying build fails (Phase 6's MA0150 leak before pragma was added) |
| Expected | `failureEnvelope.errorKind="BuildFailure"` per the documented set `{FileLock, BuildFailure, Timeout, Unknown}` |
| Actual | `errorKind="Unknown"` with `isRetryable=false`; the MSBuild MA0150 error line is right there in stdout |
| Severity | P3 — caller branching on errorKind cannot distinguish build failures from runtime failures |
| Reproducibility | reliable |

### 13.16 test_related_files — unresolved-path edge case
| Field | Detail |
|-------|--------|
| Tool | `test_related_files` |
| Input | mix of 4 Phase-6-modified file paths in the disposable worktree |
| Expected | resolve each path against the loaded workspace |
| Actual | 2 paths (`NxosVersionParser.cs`, `InventorySummaryBuilder.cs`) returned `did not resolve to a workspace document` even though the same paths are queryable via `get_source_text` and `find_references` in the same workspace |
| Severity | P3 — inconsistent resolver across read tools |
| Reproducibility | reliable |

### 13.17 test_discover — overflow on `limit=500` without pagination signal
| Field | Detail |
|-------|--------|
| Tool | `test_discover` |
| Input | `limit=500` on a 1203-test project |
| Expected | hint to use offset/limit or auto-clamp to a cap-safe limit |
| Actual | response payload exceeded the 250 KB MCP cap; diagnostic surfaced the file-spillover path; default `limit=200` would also push 1203/200 = 7 page-reads, no in-tool pagination signal |
| Severity | P3 — caller needs to discover the cap empirically |
| Reproducibility | reliable |

### 13.18 migrate_package_preview — misses analyzer-only PackageReference entries
| Field | Detail |
|-------|--------|
| Tool | `migrate_package_preview` |
| Input | `oldPackageId="Meziantou.Analyzer"` on a workspace where `get_nuget_dependencies(summary=true)` confirms Meziantou.Analyzer is referenced on all 9 projects |
| Expected | enumerate the references and offer a migration preview (even a no-op rename should produce an empty diff or "no changes detected" message) |
| Actual | InvalidOperation: "No project references to Meziantou.Analyzer were found in the loaded workspace." The tool appears to filter only PackageReference entries without `OutputItemType="Analyzer"` (analyzer-only packages use `<PackageReference Include="Meziantou.Analyzer"><PrivateAssets>all</PrivateAssets><IncludeAssets>...</IncludeAssets></PackageReference>` patterns) |
| Severity | P2 — caller cannot discover or migrate analyzer-only packages |
| Reproducibility | reliable |

### 13.19 semantic_grep — `identifiers` scope does not match qualified names like `Console.WriteLine`
| Field | Detail |
|-------|--------|
| Tool | `semantic_grep` |
| Input | `pattern="^Console\\.WriteLine$"`, `scope="identifiers"` on a workspace with many `Console.WriteLine` invocations |
| Expected | match each `Console.WriteLine` callsite (or doc clarification that identifier-token matching is single-identifier, not qualified-name) |
| Actual | 0 hits — `Console` and `WriteLine` are separate identifier tokens; the `.` is punctuation, so qualified-name anchors never match. Tool description doesn't surface this constraint. |
| Severity | P3 — discoverability gap (works correctly per token-stream semantics, but callers reading the description expect "method-call shape" matching) |
| Reproducibility | reliable |

### 13.20 find_overrides — payload overflow on root virtual (`ToString`/`Equals`/etc.) auto-promotion
| Field | Detail |
|-------|--------|
| Tool | `find_overrides` |
| Input | both (a) `metadataName=System.Object.ToString`, (b) position-based anchor on a `Device.cs:73:28 override ToString()` site (which auto-promotes to the corlib root per tool docs) |
| Expected | bounded payload with paging knobs analogous to `find_references_bulk` (`summary`, `maxItemsPerSymbol`) or `find_references` (`summary`, `limit`); OR auto-promotion guardrail that warns when the resolved root is a corlib member |
| Actual | tool returned 1,476,046-char payload (overflows MCP 250 KB cap); spilled to disk on both probes. The tool exposes neither `summary` nor `limit` knobs; auto-promotion happens silently |
| Severity | P2 — corlib-root virtuals are stock C# (any `override ToString()` triggers this); blocks promotion of `find_overrides` |
| Reproducibility | reliable |

### 13.21 set_project_property_preview / set_conditional_property_preview — undocumented 4-property allowlist
| Field | Detail |
|-------|--------|
| Tool | `set_project_property_preview`, `set_conditional_property_preview` |
| Input | `set_project_property_preview(NetworkDocumentation.Core, "TreatWarningsAsErrors", "false")` — a common, valid MSBuild property |
| Expected | preview the property write or, if intentionally restricted, list the allowlist up-front in the tool description |
| Actual | `InvalidOperationException: Property 'TreatWarningsAsErrors' is not supported. Allowed properties: ImplicitUsings, LangVersion, Nullable, TargetFramework.` Same allowlist applies to `set_conditional_property_preview`; neither description surfaces the allowlist prominently |
| Severity | P3 — discoverability; primary path to learn the allowlist is failure |
| Reproducibility | reliable |

### 13.22 analyze_dependencies prompt — unbounded rendered output (63 KB exceeds MCP cap)
| Field | Detail |
|-------|--------|
| Tool | `get_prompt_text(promptName="analyze_dependencies", parametersJson={"projectName":"NetworkDocumentation.Core"})` |
| Input | 9-project workspace (small by any reasonable standard) |
| Expected | rendered prompt fits in MCP response cap; pagination/truncation if necessary |
| Actual | response body 63 KB — exceeds the MCP harness output cap; client redirected to disk-spill file; rendered body cannot be programmatically consumed. Body almost certainly inlines full `project_graph` + `get_namespace_dependencies` + `get_nuget_dependencies` without pagination |
| Severity | P2 — blocks promotion; consumer cannot rely on the rendered text |
| Reproducibility | reliable |

### 13.23 review_test_coverage prompt — unbounded rendered output (121 KB exceeds MCP cap)
| Field | Detail |
|-------|--------|
| Tool | `get_prompt_text(promptName="review_test_coverage", parametersJson={})` |
| Input | a 9-project workspace with 1203 tests in NetworkDocumentation.Tests |
| Expected | rendered prompt fits in MCP response cap; pagination/truncation |
| Actual | response body 121 KB — far exceeds cap; almost certainly embeds full per-method coverage map |
| Severity | P2 — blocks promotion |
| Reproducibility | reliable |

### 13.24 rename_preview — different error category than other negative-handle peers
| Field | Detail |
|-------|--------|
| Tool | `rename_preview` (cross-tool drift vs `find_references`, `find_consumers`, `find_type_usages`, `find_implementations`, `find_overrides`, `find_base_members`, `impact_analysis`, `find_type_mutations`) |
| Input | structurally-valid but unfindable fabricated `symbolHandle` (base64 of `{"MetadataName":"NonExistent.Type"}`) |
| Expected | same error-envelope shape as peers (category=`NotFound`, common "No symbol could be resolved..." message) |
| Actual | category=`InvalidOperation` (peers return `NotFound`); message wording also differs from peers' shared template |
| Severity | P3 — cross-tool consistency; callers can't write a single switch |
| Reproducibility | reliable |

### 13.25 surface-test teardown — disposable worktree directory survives Windows file-lock even after `dotnet build-server shutdown` + `workspace_close(drainProcesses=true)`
| Field | Detail |
|-------|--------|
| Tool | `mcp-server-surface-test` teardown sequence (Phase 6z) |
| Input | full sequence: `dotnet build-server shutdown` → `workspace_close(workspaceId, drainProcesses=true)` → `git worktree remove --force <path>` → fallback `rm -rf` / `Remove-Item -Force` / `cmd rmdir /s /q` |
| Expected | empty disposable worktree directory removable after both process-drains complete |
| Actual | every removal attempt (git, bash rm, PowerShell Remove-Item, cmd rmdir) fails with `Device or resource busy` / `Permission denied` / `being used by another process`. Directory is empty (no contents per `Get-ChildItem -Force`). git admin record was successfully removed (only main worktree in `git worktree list`). Branch successfully deleted. Primary checkout is clean (`git status --porcelain` empty). Lock-holder process not visible in `tasklist` filtering or `Get-Process | Where Path -like *surface-test*`. |
| Severity | P1 — surface-test teardown contract violation; operator must clean up manually |
| Reproducibility | reliable on Windows 11 (the prior crashed run's worktree at `surface-test-20260516T055611Z` was successfully cleaned up by this run's pre-flight reconcile step — a fully-orphaned worktree path can be removed; but a same-session-disposed worktree apparently retains some OS-level handle) |
| Tag | `surface-test teardown` (per spec — "If teardown fails for an unexpected reason, surface the failure in the report's MCP server issues section as a P1 finding tagged `surface-test teardown`. Do not retry blindly") |

## 14. Improvement suggestions
- `find_duplicated_code` — deprecation banner is good; consider also emitting a hint to call `find_duplicate_helpers` when many bodies are 1–2 statement BCL-style wrappers, since the python-refactor parity conflates both shapes.
- `suggest_refactorings` — output is 100% complexity-driven; consider folding in top dead-code, duplicate-method, and namespace-cycle findings so the ranked list reflects the broader Phase-2 catalogue.
- `nuget_vulnerability_scan` — 13.4 s wall-clock with `includesTransitive=false` is dominated by `dotnet list package` invocations; consider batching all projects into one solution-scoped invocation.
- `member_hierarchy` — when auto-promotion fires (caret on a return-type token), include a `resolutionTrace` field that records the promotion chain (caret → enclosing method → method's return type → that type's interfaces) so callers can tell when the result describes the method's return type rather than the method itself.
- `analyze_control_flow` — drop the "incomplete" warning when the analyzed range starts at `{` and ends at the matching `}` of the same method body (strong signal the range is intentional); reserve the warning for genuinely partial ranges.
- `symbol_signature_help` — return a typed error envelope (matching `symbol_info`'s NotFound shape) rather than bare `null` when the metadataName cannot be resolved; bare null is easy to silently swallow in callers.
- `find_type_consumers` `kinds` vocabulary — align with `find_type_usages` PascalCase classification names (explicit `ObjectCreation` instead of lowercase `ctor`) so caller code can map cross-tool kinds via a single switch.
- `fix_all_preview` / `code_fix_preview` — every diagnostic id in this repo's analyzer set (Meziantou, xUnit, SYSLIB, MA-series) returns `"No code fix provider is loaded for diagnostic 'X'."` Document the limitation prominently in `fix_all_preview` description; surface it via `diagnostic_details` when known. Saves callers a trial-and-error loop across every analyzer family.
- `apply_text_edit(verify=true)` — when `errors_introduced` is reported but `preErrorCount==postErrorCount`, the delta is 0 even though new errors appeared. Consider returning a `errorsDelta` field or showing only the new-errors list; current shape conflates "introduced" vs "carried".
- `preview_code_action` bad `actionIndex` error message — "Available actions: 0" reads as "the available index is 0" rather than "the count is 0". Tweak to "Available action count: N (valid indices 0..N-1)" or similar.
- `change_signature_preview` op=reorder — error path for the identity-permutation case is correct ("identity permutation — no change to apply") but does NOT point at `symbol_refactor_preview` even though the prompt expected this; also, op=reorder IS supported for 2+ param methods, contradicting prompt prose. Either update the prompt OR add a one-line cross-reference.

## 15. Concurrency matrix (Phase 8b)
(populated in Phase 8b)

## 16. Writer reclassification verification (Phase 8b.5)
(populated in Phase 8b)

## 17. Response contract consistency
(populated if Principle #5 observes ≥1 inconsistency)

## 18. Known issue regression check (Phase 18)
Prior issue source: GitHub Issues at https://github.com/darylmcd/Roslyn-Backed-MCP (10 most recent `[audit]`-prefixed issues fetched via `gh issue list --repo darylmcd/Roslyn-Backed-MCP --state all --limit 10`). 4 reproduced this run:

| Source id | Summary | Status |
|-----------|---------|--------|
| [#743](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/743) | `analyze_control_flow` emits partial-slice warning even when range covers full method declaration | **still reproduces** — Phase 4 confirmed on NxosVersionParser.ParseNxos (lines 32-112, full body, void method) — see finding 13.5 |
| [#740](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/740) | `workspace_changes` splits atomic `apply_multi_file_edit` batch into N ledger entries without batchId correlation | **still reproduces** — Phase 6h's multi-file edit (ReportData.cs + ReportEndpoints.cs) recorded as seq 11 and seq 12 at identical timestamp `06:55:16.879/06:55:16.884`, both with toolName `apply_multi_file_edit`, no batchId field connecting them |
| [#739](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/739) | `format_document_preview` returns empty-diff change entry instead of clean no-op envelope | **still reproduces** — Phase 6e: `format_document_preview` on already-formatted PipelineProgressReporter.cs returned a preview with empty-diff change entry (3231 ms), and `format_document_apply` then returned `appliedFiles=[]` instead of a `noOp:true` shape |
| [#735](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/735) | `set_editorconfig_option` appends duplicate key instead of de-duplicating | **no longer reproduces — candidate for closure** — after Phase 6f-ii set MA0007=silent, Phase 6 subagent ran `set_editorconfig_option(CA1822.severity, suggestion)` then Phase 8b.5 ran `set_diagnostic_severity(CA1822, suggestion)` (same key, both writers); the on-disk `.editorconfig` shows a SINGLE `dotnet_diagnostic.CA1822.severity = suggestion` entry — dedup is working in this server version |

Not reproduced because not exercised: #742 (`callers_callees previewText asymmetry`), #741 (`find_type_mutations MutationScope single-valued`), #738 (`validate_workspace ChangeTracker reconcile after git checkout`), #737 (`find_overrides vs member_hierarchy.overrides disagreement`), #736 (`member_hierarchy.overrides mislabels sibling interfaces`).

## 19. Known issue cross-check
- Finding 13.5 (`analyze_control_flow` misleading "incomplete" warning) matches existing issue [#743](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/743) — this run provides fresh repro evidence; consider adding a comment with the new repro path rather than filing a duplicate.

## Finding emission (Phase 19)
- **Routing decision:** maintainer detected (`gh api user --jq .login` == `darylmcd`); route = **auto-file** to https://github.com/darylmcd/Roslyn-Backed-MCP per spec.
- **Renderer:** shared `render-finding.ps1` was invoked for each finding (dot-source pattern); body output used for `gh issue create --body-file`.
- **Refusal contract:** N/A — no P0 or `area: security` findings in this audit.
- **Dedup pre-check:** ran `gh issue list --search "<tool-name> in:title"` per finding; near-name hits (#738/#611/#737) inspected and confirmed to address different symptom sets than this run's findings.
- **Filed (13):**
  - P1 — extract_method_preview false-negative on if-block selection → [#744](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/744)
  - P1 — surface-test teardown directory survives Windows lock → [#745](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/745)
  - P2 — project_diagnostics totalDiagnostics collapses under severity filter → [#746](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/746)
  - P2 — symbol_signature_help returns bare null → [#747](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/747)
  - P2 — extract_interface_preview duplicate interface → [#748](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/748)
  - P2 — change_type_namespace_preview omits consumer using additions → [#749](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/749)
  - P2 — symbol_refactor_preview empty appliedFiles on success → [#750](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/750)
  - P2 — validate_workspace analyzer-error verdict with empty errorDiagnostics → [#751](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/751)
  - P2 — test_run fqdn drift vs test_discover → [#752](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/752)
  - P2 — migrate_package_preview misses analyzer-only references → [#753](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/753)
  - P2 — find_overrides payload overflow on corlib virtuals → [#754](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/754)
  - P2 — analyze_dependencies prompt overflow (63 KB) → [#755](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/755)
  - P2 — review_test_coverage prompt overflow (121 KB) → [#756](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/756)
- **Skipped (0):** N/A — none of the prepared P1/P2 findings collided with existing OPEN/CLOSED issues.
- **Existing-issue match preserved as cross-check rather than dup-filed:** Finding 13.5 (`analyze_control_flow` partial-slice warning) is already tracked at [#743](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/743); cross-check noted above. Lower-severity P3 findings (13.2, 13.4, 13.6, 13.12, 13.14, 13.15, 13.16, 13.17, 13.19, 13.21, 13.24) were documented inline in this report but not separately filed this run — emission focused on high-impact P1/P2 evidence; the operator may file the P3s manually from the report text or in a follow-up batch.
- **Summary:** `filed=13 · skipped=0 · failed=0 · cross-checked-only=1 (#743) · documented-inline-not-filed=11 (P3s)`.

---

## Live catalog totals (Phase -1 capture — authoritative)
- toolCount: 169 (stable 111 / experimental 58)
- resourceCount: 13 (stable 9 / experimental 4)
- promptCount: 20 (stable 0 / experimental 20)
- catalogVersion: 2026.04
- parityOk: true
- catalog vs server_info: counts match
- workflowHints: 19 documented sequences

## Live-surface drift detection (Phase 0 step 14)
- Catalog tool/resource/prompt name set vs. names referenced in this prompt's phase guidance:
  - Names in catalog but never named in the prompt's phase guidance → defer enumeration to Final closure (large set; tracked via coverage ledger statuses).
  - Names in prompt's code-fenced examples or numbered steps but absent from the catalog → none observed at Phase 0 (initial scan); re-checked during ledger reconciliation.

## Phase 0.5 — Subagent dispatch plan (recorded)
- **G1 (Phases 1, 2):** `audit-phase-runner` (supported) — diagnostics + metrics.
- **G2 (Phases 3, 4):** inline (audit-phase-runner does NOT support; see agent description) — symbol + flow analysis.
- **G3 (Phases 5, 9):** inline — snippet/script + undo (Phase 9 runs AFTER Phase 10 per the run-order contract).
- **G4 (Phase 6 sub-phases):** orchestrator-owned per spec — apply-tool exercise on disposable worktree.
- **G5 (Phases 7, 8, 8b):** Phase 7 inline (config), Phases 8 + 8b dispatched to `audit-phase-runner` (supported) — build/test + concurrency.
- **G6 (Phases 10, 11, 12):** inline — file ops + semantic search + scaffolding.
- **G7 (Phases 13, 14):** inline — project mutation + navigation.
- **G8 (Phases 15, 16, 17):** inline — resources + prompts + negative tests.
- **Phase 18 + 19:** orchestrator-owned per spec (regression check + finding emission).
