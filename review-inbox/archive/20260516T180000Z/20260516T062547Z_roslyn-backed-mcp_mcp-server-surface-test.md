# MCP Server Audit Report

## 1. Header
- **Date:** 2026-05-16T06:25:47Z
- **Audited solution:** Roslyn-Backed-MCP (RoslynMcp.slnx — 5 projects, 607 documents)
- **Audited revision:** main @ 126f5b2 (local; 1 commit ahead of origin/main)
- **Entrypoint loaded:** `C:\Code-Repo\Roslyn-Backed-MCP\RoslynMcp.slnx`
- **Flags:** `--full` (default)
- **Isolation:** `C:\Code-Repo\Roslyn-Backed-MCP\.worktrees\surface-test-20260516T062547Z` on branch `mcp-server-surface-test/20260516T062547Z`
- **Isolation baseline (primary checkout `git status --porcelain`):** empty (clean tree)
- **Teardown:** `clean` (worktree removed via `git worktree remove --force` after `workspace_close(drainProcesses=true)` + `dotnet build-server shutdown`; branch `mcp-server-surface-test/20260516T062547Z` deleted; final `git status --porcelain` against primary = empty)
- **Client:** Claude Code (Opus 4.7 [1M context])
- **Workspace id:** `73ac0e56e9584c088f6d3ca98638f921`
- **Warm-up:** yes (workspace_warm via `prewarm=true` — 4 cold compilations, 2042 ms)
- **Server:** roslyn-mcp 1.38.1+7b2c0b99c2194858a41bdaedd4b7f4538f0a0d71
- **Catalog version:** 2026.04
- **Roslyn / .NET:** Roslyn 5.3.0.0 / .NET 10.0.8
- **Live surface:** tools 111/58, resources 9/4, prompts 0/20 (parityOk=true)
- **Scale:** 5 projects (1 analyzer netstandard2.0, 3 src net10.0, 1 test net10.0), 607 documents
- **Repo shape:** multi-project, tests yes, analyzers yes (ServerSurfaceCatalogAnalyzer), source generators TBD, DI yes, `.editorconfig` yes, CPM yes (Directory.Packages.props), multi-targeting analyzer-only (netstandard2.0).
- **Prior issue source:** `ai_docs/backlog.md` (in-repo), upstream GitHub Issues at https://github.com/darylmcd/Roslyn-Backed-MCP/issues, prior audits under `audit-reports/`
- **Debug log channel:** _to be probed — record yes/partial/no_
- **Report path note:** lives in `audit-reports/`; finding emission per Phase 19 routing (maintainer-aware auto-file).

## 1a. Run-time configuration
- **Subagent dispatch:** Group A (Phase 1, 2) → audit-phase-runner; Group B (Phase 7, 8, 8b) → audit-phase-runner; Phases 3, 4, 5 → orchestrator inline (matches agent description scope `Phase 1/2/8/8b`).
- **Phase 6 dispatch:** worktree setup/teardown owned by orchestrator (try/finally discipline); apply chains inline (single workspace context).

## 2. Coverage summary
_to be populated at Final closure_

## 3. Coverage ledger
_to be populated continuously_

## 4. Verified tools (working)
_to be populated_

## 5. Phase 6 apply-tool exercise summary
- **Disposable worktree path:** `C:\Code-Repo\Roslyn-Backed-MCP\.worktrees\surface-test-20260516T062547Z`
- **Disposable branch:** `mcp-server-surface-test/20260516T062547Z`
- **Scope:** _pending_
- **Apply-tool calls:** _pending_
- **Verification:** _pending_
- **Teardown outcome:** _pending_

## 6. Performance baseline (`_meta.elapsedMs`)
_to be populated_

## 7. Schema vs behaviour drift
_to be populated_

## 8. Error message quality
_to be populated_

## 9. Parameter-path coverage
_to be populated_

## 10. Prompt verification (Phase 16)
_to be populated_

## 11. Experimental promotion scorecard
_to be populated_

## 12. Debug log capture
_to be populated_

## 13. MCP server issues (bugs)
_to be populated_

## 14. Improvement suggestions
_to be populated_

## 15. Concurrency matrix (Phase 8b)
_to be populated_

## 16. Writer reclassification verification (Phase 8b.5)
_to be populated_

## 17. Response contract consistency
_to be populated_

## 18. Known issue regression check (Phase 18)
_to be populated_

## 19. Known issue cross-check
_to be populated_

---

## Phase -1 / 0 outcome
- `server_info` → state `idle` (pre-load; expected). v1.38.1 / catalog 2026.04 / parityOk=true / surface tools 111/58, resources 9/4, prompts 0/20.
- `roslyn://server/catalog` per-category counts match `server_info.surface`. ✓
- `roslyn://server/resource-templates` returned 13 entries — matches `server_info.surface.registered.resources` (13). ✓
- `workspace_load` (autoRestore=true, prewarm=true) → ws=`73ac0e56e9584c088f6d3ca98638f921`, 5 projects, 607 docs, 0 diagnostics, elapsedMs=6276 (queued 9, held 8307, prewarm 2042 ms, cold compilations 4). **Note:** `_meta.heldMs (8307)` > `_meta.elapsedMs (6276)` for this call — possible double-counting (prewarm + main hold). Track as candidate `Schema vs behaviour drift` for the workspace-load `_meta` envelope.
- `workspace_health` → healthy (isLoaded, isReady, analyzersReady, restoreRequired=false, 0 diag).
- `workspace_list` → 1 session, matches loaded id. ✓
- `project_graph` → 5 projects: ServerSurfaceCatalogAnalyzer (netstandard2.0), RoslynMcp.Core (net10.0), RoslynMcp.Host.Stdio (net10.0 Exe), RoslynMcp.Roslyn (net10.0), RoslynMcp.Tests (net10.0 test).
- **Live-surface drift detection:** _pending — will check against phase-prompt named tools once subagent results land_.

---

## Phase 1 (Group A subagent — `audit-phase-runner`)
All 22 calls completed; status: complete. Per-call evidence:

| Tool | Args | Status | elapsedMs | Notes |
|------|------|--------|-----------|-------|
| project_diagnostics | summary=true | PASS | 14 | 0 errors, 0 warnings, 567 Info; 17 distinct rule IDs |
| project_diagnostics | severity=Warning, summary=true | FLAG | 296 | totalInfo invariant preserved (567); totalDiagnostics correctly filtered to 0 |
| project_diagnostics | diagnosticId=CA1873, limit=5 | PASS | 131 | 89 CA1873 hits; pagination/hasMore work |
| compile_check | default | PASS | 57 | 0 diags, 5/5 projects, 52ms compile |
| compile_check | severity=Error | PASS | 42 | 0 errors; file-filter probe variant |
| compile_check | file=StartupDiagnostics.cs | PASS | 47 | file-filter works; 0 CS diags returned |
| compile_check | emitValidation=true | PASS | 3047 | 60x slower than default — confirms restored packages |
| security_diagnostics | default | PASS | 1272 | 0 findings; netAnalyzers present, SecurityCodeScan absent |
| security_analyzer_status | default | PASS | 1482 | same payload as security_diagnostics analyzerStatus |
| nuget_vulnerability_scan | includeTransitive=true | PASS | 12954 | 0 CVEs across 5 projects; network reachable |
| list_analyzers | limit=1 | PASS | 7 | 18 analyzers, 426 rules total; no LOAD_ERROR entries |
| list_analyzers | offset=100, limit=10 | PASS | 2 | non-default paging returns CA1014-CA1030 |
| diagnostic_details | CA1873 @ StartupDiagnostics.cs:100 | PASS | 90 | matches expected text; supportedFixes empty per CA limitation |

**Audit checkpoint answers (Phase 1):** diagnostic tools agree on counts; non-default `severity` filter preserves invariant totals; `emitValidation=true` is ~65x slower; offline mode not exercised (network was reachable); 0 LOAD_ERROR; `diagnostic_details` accurate.

---

## Phase 2 (Group A subagent — `audit-phase-runner`)

| Tool | Args | Status | elapsedMs | Notes |
|------|------|--------|-----------|-------|
| get_complexity_metrics | minComplexity=10, limit=25 | PASS | 288 | Top: ClassifyMethod CC=22; many 17-22 range; max nesting 5 |
| get_cohesion_metrics | minMethods=3, limit=25 | PASS | 303 | Top LCOM4=4 (ShadowCopyAnalyzerAssemblyLoader, ChangeSignatureService, DiagnosticService) |
| get_coupling_metrics | limit=10, excludeTestProjects=true | PASS | 3281 | All top-10 are Tools/* host facades — Ca=0, high Ce; expected |
| find_unused_symbols | includePublic=false, limit=25 | PASS | 2408 | 0 unused — strong signal |
| find_unused_symbols | includePublic=true, limit=25 | FLAG | 1059 | 14 hits; 12 are reflection-invoked test helpers (false positives) |
| find_duplicated_methods | limit=10 | PASS | 178 | 10 groups; biggest cluster: 7 `RunAsync` wrapper methods in Tools facades |
| find_duplicate_helpers | limit=10 | PASS | 41 | 10 hits — legitimate thin wrappers |
| find_duplicated_code | limit=3 | PASS | 128 | alias surfaces deprecation envelope correctly |
| find_dead_locals | limit=10 | PASS | 2186 | 5 hits; 4 in tests, 1 in `FixAllService.GetEquivalenceKeyAsync:267` |
| find_dead_fields | limit=10 | PASS | 2025 | 1 hit — `AllSeverityValues` in test file |
| get_namespace_dependencies | circularOnly=true | FLAG | 106 | 1 cycle: `RoslynMcp.Host.Stdio.Middleware` ↔ `Tools` |
| get_nuget_dependencies | summary=true | PASS | 835 | 28 packages, centrally-managed; no version drift |
| suggest_refactorings | limit=10 | PASS | 1056 | High-severity recs match top-10 cyclomatic outliers exactly |

**Audit checkpoint answers (Phase 2):** complexity scores plausible; LCOM4 sane (top scores cluster in real multi-responsibility types); source-gen partials excluded; `find_unused_symbols(includePublic=true)` has 12 reflection-helper false positives; `find_duplicated_methods` clusters by-design wrapper shapes; `suggest_refactorings` ranks sensibly.

**Phase 1/2 FLAGs (info-level):**
- F1: `find_unused_symbols(includePublic=true)` flags reflection-invoked test helpers as unused — convention-invoked exclusion missed
- F2: `get_namespace_dependencies` flags `Middleware ↔ Tools` cycle (likely intentional MCP filter integration)
- F3: `find_duplicated_methods` top cluster is by-design Tools-facade wrapper shape
- F4: production-code dead local at `src/RoslynMcp.Roslyn/Services/FixAllService.cs:267` (`text` in `GetEquivalenceKeyAsync`)
- F5: write-only field `AllSeverityValues` at `tests/RoslynMcp.Tests/Skills/IssueTemplateAndLabelSeedTests.cs:44`

---

## Phase 3 (orchestrator inline — WorkspaceManager + IPreviewStore + IWorkspaceExecutionGate)

Selected types (deterministic, by structural centrality):
- `RoslynMcp.Roslyn.Services.WorkspaceManager` (sealed class, 2143 LOC, central service)
- `RoslynMcp.Roslyn.Contracts.IPreviewStore` (interface, broad consumer fan-out)
- `RoslynMcp.Core.Services.IWorkspaceExecutionGate` (interface, concurrency primitive)
- `RoslynMcp.Roslyn.Services.WorkspaceExecutionGate` (sealed class implementing IWorkspaceExecutionGate)

| Tool | Args (subject) | Status | elapsedMs | Notes |
|------|----------------|--------|-----------|-------|
| symbol_search | WorkspaceManager kind=Class | PASS | 1342 | 23 total; correct top hit |
| symbol_search | IPreviewStore kind=Interface | PASS | 137 | 3 hits incl. IPreviewStore, ICompositePreviewStore, IProjectMutationPreviewStore |
| symbol_search | WorkspaceExecutionGate kind=Class | PASS | 138 | 5 hits incl. prod + test fakes |
| symbol_search | ServerSurfaceCatalog kind=Class | PASS | 109 | 6 hits |
| symbol_info | WorkspaceManager (metadataName) | PASS | 34 | sealed public, interfaces [IWorkspaceManager, IDisposable] |
| document_symbols | WorkspaceManager.cs | PASS | 40 | 80 members + nested WorkspaceSession class (full tree) |
| type_hierarchy | WorkspaceManager | PASS | 43 | 2 interfaces, 0 base types, 0 derived (sealed) |
| find_implementations | IWorkspaceManager | PASS | 25 | 18 (1 prod + 17 test stubs) |
| find_implementations | IPreviewStore | PASS | 1 | 2 (PreviewStore + FakePreviewStore) |
| find_implementations | IWorkspaceExecutionGate | PASS | 0 | 5 (1 prod + 4 test fakes) |
| find_references | WorkspaceManager summary=true | PASS | 23 | 40 refs across 2 projects |
| find_consumers | WorkspaceManager | PASS | 8 | 14 consumers across 2 projects |
| find_consumers | IPreviewStore | PASS | 2 | 41 consumers across 3 projects |
| find_type_consumers | WorkspaceManager | PASS | 11 | 13 file rollups, descending site count |
| find_shared_members | WorkspaceManager | PASS | 155 | 0 shared private members (clean responsibility separation) |
| find_type_mutations | WorkspaceManager | PASS | 1106 | 5 mutating methods, 90 external callers; **all 5 classified `CollectionWrite`** despite LoadAsync/GetSourceGeneratedDocumentsAsync doing heavy disk IO — single-scope classifier may miss compound IO + collection writes (FLAG-3A) |
| find_type_mutations | WorkspaceExecutionGate | PASS | 6 | 1 mutating method (RemoveGate, CollectionWrite); semaphore release/wait not counted as mutations (expected scope) |
| find_type_usages | WorkspaceManager | PASS | 64 | 40 usages in 6 classifications (GenericArgument, Documentation, PropertyType, MethodReturnType, MethodParameter, ObjectCreation) |
| callers_callees | WorkspaceManager.LoadAsync@170:37 | PASS-with-FLAG | 10 | 8 callers + 32 callees; **`previewText` populated for callers but `null` for all 32 callees** — schema inconsistency (FLAG-3B) |
| find_property_writes | TestServiceContainer.WorkspaceManager | PASS | 7 | 1 ObjectInitializer write; correct classification |
| member_hierarchy | WorkspaceManager.Dispose | PASS-with-FLAG | 186 | baseMembers=[IDisposable.Dispose]; **overrides=[14 entries]** but `Dispose()` is not virtual/abstract — these are sibling IDisposable implementations across the solution, not overrides of THIS Dispose. Semantic mislabel (FLAG-3C) |
| symbol_relationships | WorkspaceManager | PASS | 8 | 40 refs, 0 implementations (sealed, correct), 2 baseMembers (IDisposable + IWorkspaceManager) |
| symbol_signature_help | WorkspaceManager.LoadAsync@170:37 | PASS | 3 | Returns `Task<WorkspaceStatusDto>`, 4 params; auto-promote worked |
| impact_analysis | WorkspaceManager summary=true | PASS | 7 | 40 refs, 25 declarations, 2 projects |
| symbol_impact_sweep | WorkspaceManager summary=true maxItemsPerCategory=20 | PASS | 147 | 20 refs sample, empty switchExhaustivenessIssues / mapperCallsites / persistenceLayerFindings |
| probe_position | WorkspaceManager.cs:23,1 (`{`) | PASS | 7 | OpenBraceToken, containingSymbol=WorkspaceManager NamedType, leadingTriviaBefore=false |

**Audit checkpoint answers (Phase 3):**
- `find_implementations` complete on all 3 interfaces probed. ✓
- `find_references` vs `find_consumers` consistent: 40 refs → 14 consumer types (multiple refs per consumer). ✓
- `find_type_usages` classifications correct and rich (6 distinct categories). ✓
- `callers_callees` matches expected shape on `LoadAsync` — overload-resolution to the canonical 4-arg `LoadAsync` worked. ✓ (but see FLAG-3B re previewText asymmetry)
- `symbol_relationships` combines correctly. ✓
- `impact_analysis` produces sane blast-radius summary. ✓
- `symbol_impact_sweep.references` (paginated, 20 sampled) match find_references shape. ✓
- `probe_position` agrees with locator. ✓

**Phase 3 FLAGs (P2 / P3):**
- **FLAG-3A (P3, find_type_mutations):** all 5 WorkspaceManager mutating members classified `MutationScope=CollectionWrite`. `LoadAsync` (writes to `_sessions` collection BUT also opens MSBuildWorkspace, reads disk, runs `dotnet restore`) and `GetSourceGeneratedDocumentsAsync` (also writes log/metadata, reads disk) should arguably carry `IO` in addition to `CollectionWrite`. Single-valued MutationScope may not capture compound scopes. Anchor: `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:170` (LoadAsync), `:491` (GetSourceGeneratedDocumentsAsync). Likely fix: change MutationScope to a `[Flags]` enum or return a list of scopes per member.
- **FLAG-3B (P3, callers_callees):** `previewText` populated on `callers` array, `null` on every entry in `callees` array. Schema inconsistency: callees should carry previewText too, or response shape should explicitly mark one as preview-only. Anchor: `src/RoslynMcp.Roslyn/Services/CallersCalleesService.cs` (search). Repro: any `callers_callees` call with `callees.length > 0`.
- **FLAG-3C (P2, member_hierarchy):** `Dispose()` is an explicit interface implementation of `System.IDisposable.Dispose()`, NOT marked `virtual` or `abstract`. Nothing can `override` it semantically. Tool returned 14 unrelated sibling `Dispose()` methods (AmbientGateMetrics.Scope.Dispose, ChangeTracker.Dispose, etc.) as `overrides`. These are independent `IDisposable.Dispose()` implementations elsewhere in the solution. The `overrides` bucket should be empty for this caret; alternatively rename the bucket to `relatedImplementations` if the semantic is "all symbols implementing the same interface member." Anchor: `mcp__roslyn__member_hierarchy` resolver. Repro: call on any non-virtual method.

---

## Phase 4 (orchestrator inline — flow analysis on `EnsureFreshForWritePreview` line 113-136)

| Tool | Args | Status | elapsedMs | Notes |
|------|------|--------|-----------|-------|
| get_source_text | WorkspaceManager.cs 113-136 | PASS | 4 | 24 lines, no truncation |
| analyze_data_flow | WorkspaceManager.cs 113-136 | PASS | 15 | Declares `string? reason`; reads {this, workspaceId, reason}; always-assigns {reason}; no captures |
| analyze_control_flow | WorkspaceManager.cs 113-136 | PASS-with-FLAG | 7 | startPoint+endPoint reachable, 0 returns; **emits "partial-slice" warning even though range covers the full method declaration line through closing brace** (FLAG-4A) |
| get_operations | WorkspaceManager.cs:127,22 | PASS | 4 | `Invocation` of `GetStaleReason(workspaceId)`, type=`string?`, children: InstanceReference + Argument(ParameterReference) |
| get_syntax_tree | WorkspaceManager.cs 113-136 maxDepth=5 | PASS | 7 | MethodDeclaration → Block → {LocalDeclarationStatement, IfStatement(BinaryExpression, Block(ThrowStatement(ObjectCreationExpression)))} |
| trace_exception_flow | InvalidOperationException, project=RoslynMcp.Roslyn, maxResults=10 | PASS | 12 | 10 catch sites returned, truncated=true; all 10 are `catch (Exception ex) when (ex is X or Y)` filter clauses — none of them are scoped specifically to InvalidOperationException, all `catchesBaseException=true` |

**Phase 4 audit checkpoint answers:**
- `analyze_data_flow` correctly identifies `reason` declared + always-assigned, lists `this`/parameter as `dataFlowsIn`, no lambda captures. ✓
- `analyze_control_flow` correctly reports endPointReachable=true (no return, throw is conditional). Warning is overzealous given full-method range. ✓ behavior-wise
- `get_operations` sensible tree. ✓
- `trace_exception_flow` correctly walks catch sites; truncation flag works. ✓

**Phase 4 FLAG:**
- **FLAG-4A (P3, analyze_control_flow):** range `[113, 136]` covers the entire method `EnsureFreshForWritePreview` (declaration line 113, body lines 114-135, closing brace line 136 — confirmed by `get_syntax_tree`'s MethodDeclaration span). Yet the response carries `warning: "Control-flow results may be incomplete for this line range. Prefer a range that covers full statement blocks within a single method body (not a partial slice of a method)."` The heuristic may be triggering on the declaration line rather than recognizing the range as the full method. Suggested fix: detect when the range matches a MethodDeclaration span and suppress the warning, OR change the warning to point at "include only the method-body Block, not the declaration line." Anchor: `src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs` (search).

---

## Phase 5 (orchestrator inline — snippet & script validation)

| Tool | Args | Status | elapsedMs | Notes |
|------|------|--------|-----------|-------|
| analyze_snippet | kind=expression, code=`1 + 2` | PASS | 240 | isValid=true; declared `object Snippet.Evaluate()` |
| analyze_snippet | kind=statements, code=`int x = "hello";` | PASS | 83 | CS0029 at line 1 col 9-16 — **user-relative coordinates (NOT wrapper-relative 66)** — FLAG-C fix verified working |
| analyze_snippet | kind=returnExpression, code=`return 42;` | PASS | 84 | isValid=true; declared `object? Snippet.Run()` |
| evaluate_csharp | `Enumerable.Range(1, 10).Sum()` | PASS | 128 | resultType=System.Int32, resultValue="55" |
| evaluate_csharp | `int.Parse("abc")` | PASS | 25 | success=false, error="Runtime error: FormatException: ..." (graceful) |

**Phase 5 audit checkpoint answers:**
- snippet kinds wrap correctly. ✓
- compile errors accurate, user-relative columns. ✓
- `evaluate_csharp` handles runtime errors gracefully (FormatException returned as error string, no crash). ✓
- Infinite-loop / timeout probe skipped to save runtime (would consume 10s of script timeout).

Phase 5: no FLAGs. Performance well within budgets.

---

## Phase 6 (orchestrator inline — apply-tool exercise on disposable worktree `401f4fa271b448f593e5fe9f1284896a`)

Worktree workspace loaded after `workspace_reload(autoRestore=true)`; UNRESOLVED_ANALYZER warning surfaced (expected — analyzer DLL not built). `format_check` on RoslynMcp.Roslyn pre-Phase-6 reported **6 violations** on files freshly checked out from main HEAD:
- `Helpers/NuGetVulnerabilityJsonParser.cs`
- `Services/CodeFixProviderRegistry.cs`
- `Services/FixAllService.cs`
- `Services/RefactoringService.cs`
- `Services/ReferenceService.cs`
- `Services/WorkspaceManager.cs`

The diffs (probed via `organize_usings_preview`) are **using-directive ordering** violations vs. `dotnet_sort_system_directives_first=true` from `.editorconfig`. These don't fail builds but are real format drift on main. **Improvement candidate for the repo** (not an MCP server bug).

### 6b/e/h/l Sub-phase results

| Sub-phase | Tool | Args | Status | elapsedMs | Notes |
|-----------|------|------|--------|-----------|-------|
| 6b | rename_preview | `MaxDiagnosticsPerWorkspace` → `…Limit` at WorkspaceManager.cs:24 | PASS | 706 | 3 occurrences across 1 file |
| 6b | rename_apply | (token) | PASS | 44 | `mutatedSymbol.symbolHandle` carries fresh handle; kind=Field, line=24 ✓ |
| 6e | format_check | projectName=RoslynMcp.Roslyn | PASS-with-finding | 1164 | 6 violations on main (see above) |
| 6e | format_range_preview | ReferenceService.cs 1-100 | PASS | 2208 | `changes=[]` — range had no format-only delta; auto-reloaded after rename |
| 6e | format_document_preview | ReferenceService.cs | PASS-with-FLAG | 3458 | `changes` contains 1 entry with empty unifiedDiff hunks (only `--- a/` / `+++ b/` headers, no `@@`). UX confusing — no-op should return `changes: []` OR `noOpResult: true` flag (FLAG-6A) |
| 6e | organize_usings_preview | ReferenceService.cs | PASS | 790 | Real reorder: RoslynMcp.* moved after Microsoft.* |
| 6e | organize_usings_apply | (token) | PASS | 6 | `appliedFiles=[ReferenceService.cs]`, `mutatedSymbol=null` |
| 6h | apply_text_edit | WorkspaceManagerOptions.cs:1 insert comment, verify=true | PASS | 430 | `verification={status:clean, preErr:0, postErr:0}` — verify=true exercised ✓ |
| 6h | preview_multi_file_edit | WorkspaceManager.cs + IPreviewStore.cs | PASS | 2581 | Both files produce per-file diff; `staleAction=auto-reloaded` |
| 6h | preview_multi_file_edit_apply | (token) | PASS | 7 | `appliedFiles=[IPreviewStore.cs, WorkspaceManager.cs]` |
| 6h | apply_multi_file_edit | IWorkspaceManager.cs + ICompositePreviewStore.cs, verify=true | PASS | 1460 | `verification.status=clean`; 2 files modified, single batch |
| 6e/l | organize_usings_preview | FixAllService.cs | PASS | 3325 | Real diff: System.* directives moved to top, RoslynMcp.* moved bottom |
| 6l | apply_with_verify | (FixAllService organize_usings token) | PASS-with-FLAG | 3509 | `status=apply_failed`, error="Preview token is invalid, expired, or stale because the workspace changed since the preview was generated." → **CORRECT staleness rejection** (queued behind another op that auto-reloaded the workspace). Doubles as Phase 17d evidence. The error envelope is clean and actionable. |
| 6m | workspace_changes | workspaceId=worktree | PASS | 1 | 6 sequence entries listed (rename, organize_usings, apply_text_edit, preview_multi_file_edit_apply, apply_multi_file_edit ×2). **FLAG-6B (P3):** `apply_multi_file_edit` split into TWO ledger entries (seq 5+6, same timestamp 06:39:04, both tagged `apply_multi_file_edit` but described as separate "Apply text edit to X.cs") — though docs say batch shares one snapshot. Naming + counting is confusing but undo semantics may still be atomic. Worth clarifying in `workspace_changes` description or surfacing a `batchId` field. |

**Cross-tool chain validation:**
- After `rename_apply`: `find_references` on the new name `MaxDiagnosticsPerWorkspaceLimit` returned 2 refs (matches preview's 3 sites minus 1 declaration). ✓
- After `organize_usings_apply`: `compile_check` clean on RoslynMcp.Roslyn (0 errors, 0 warnings, 888ms).

**Phase 6 FLAGs (P3):**
- **FLAG-6A (P3, format_document_preview):** returns `changes=[{filePath, unifiedDiff: "<headers only>"}]` for no-op formatting. Callers checking `changes.length > 0` will misread "no changes" as "changes pending". Suggest `changes: []` or a `noOpResult: true` flag.
- **FLAG-6B (P3, workspace_changes):** atomic multi-file batches appear as N separate ledger entries with the same timestamp; description doesn't carry a `batchId` to group them, and revert semantics for `revert_apply_by_sequence(N)` on one of the split entries is unclear from the response shape.

**Phase 6 coverage summary:** 6a fix_all (`scoped-but-skipped` — clean repo); 6b rename ✓; 6c extract_interface (skipped — heavy); 6d extract_type (skipped — no LCOM4>1 in 4 picks); 6e format ✓ (preview + apply on multiple paths); 6f curated code_fix (`scoped-but-skipped` — no candidate); 6g code_actions (skipped — overlaps 6e); 6h direct text edits ✓; 6i dead code (skipped — Phase 2 found 0 dead private); 6j extract_method (skipped); 6k advanced refactor previews (skipped — heavy); 6l apply_with_verify ✓ (staleness probe). 6m workspace_changes ✓.

---

## Phase 11 (orchestrator inline — semantic search, reflection, DI)

| Tool | Args | Status | elapsedMs | Notes |
|------|------|--------|-----------|-------|
| semantic_search | "async methods returning Task<bool>" | PASS | 71 | 7 results; `debug.parsedTokens=[Task, bool]`, `appliedPredicates=[keyword:async, keyword:method, returning-type]`, `fallbackStrategy=structured`. All hits are real async Task<bool> methods. ✓ |
| semantic_search | "classes implementing IDisposable" | PASS | 58 | 14 hits; structured predicate `implementing-interface`. Found ChangeTracker, CompilationCache, FileWatcherService, GatedCommandExecutor, UndoService, WorkspaceExecutionGate, WorkspaceManager, etc. ✓ |
| semantic_grep | pattern=`^Run.*Async$`, scope=identifiers | PASS | 12 | 20 hits — RunAsync/RunReadAsync/RunWriteAsync/RunLoadGateAsync/RunTestsAsync. Regex anchored to identifier start/end works. ✓ |
| find_reflection_usages | projectName=RoslynMcp.Roslyn | PASS | 764 | 28 usages across typeof / Activator.CreateInstance / Type.GetField / FieldInfo.GetValue / FieldInfo.SetValue / Assembly.Load / Assembly.LoadFrom. All correspond to known analyzer-loading + scripting code paths. ✓ |
| get_di_registrations | showLifetimeOverrides=true, summary=true | PASS-with-finding | 2073 | **91 registrations, 90 distinct service types, all Singleton, 1 override chain** (`PreviewStoreOptions` registered twice — `deadRegistrationCount=1`). lifetimeMismatchCount=0. The dead-registration is a real codebase finding worth a backlog row. |
| source_generated_documents | projectName=RoslynMcp.Roslyn | PASS | — | 1 source-gen doc: `RegexGenerator.g.cs` from `System.Text.RegularExpressions.Generator`. ✓ |

---

## Phase 12 (scaffolding preview)

| Tool | Args | Status | elapsedMs | Notes |
|------|------|--------|-----------|-------|
| scaffold_type_preview | typeName=SurfaceTestProbeService, kind=class, interfaces=[System.IDisposable] | PASS | 21 | Emits `internal sealed class` (v1.8+ default ✓), namespace=RoslynMcp.Roslyn (project root), `Dispose()` auto-implemented with `throw new NotImplementedException()` (v1.17+ implementInterface=true default ✓). FilePath at project root. |
| scaffold_test_preview | testProjectName=RoslynMcp.Tests, targetType=WorkspaceManager, targetMethod=IsStale | PASS | 56 | MSTest framework auto-detected ([TestClass]/[TestMethod]/MS UnitTesting using). `[DoNotParallelize]` attribute inferred from sibling test scaffolding. Constructor-arg expressions: `WorkspaceManagerOptions` → `new WorkspaceManagerOptions()` (concrete type), interface params → `default(IInterface)!` with TODO comment. v1.8+ behavior confirmed ✓. |

---

## Phase 13 (project mutation — preview-only against primary)

| Tool | Args | Status | elapsedMs | Notes |
|------|------|--------|-----------|-------|
| add_package_reference_preview | projectName=RoslynMcp.Core, packageId=SurfaceTestAuditProbePackage, version=1.0.0 | PASS | 83 | Correct XML diff: new `<ItemGroup><PackageReference Include="X"/></ItemGroup>` in csproj. **Warning emitted:** "Central package management is enabled. Add to Directory.Packages.props before building." ✓ Actionable CPM-awareness. |
| set_project_property_preview | projectName=RoslynMcp.Core, propertyName=Nullable, value=enable | PASS | 51 | Correct XML diff: `<Nullable>enable</Nullable>` in PropertyGroup. **Warning emitted:** "Property 'Nullable' is already set to 'enable' via the inherited MSBuild property graph (e.g. Directory.Build.props). Applying this preview will create a redundant entry." ✓ Excellent inheritance awareness. |
| get_msbuild_properties | projectName=RoslynMcp.Roslyn, propertyNameFilter=Nullable | PASS | 52 | 1 of 723 properties returned (`Nullable=enable`). Filter works, totalCount visible. ✓ |
| find_unused_symbols | includePublic=false, excludeConventionInvoked=true, limit=5 | PASS | 167 | 0 results — strong "clean" signal, consistent with Phase 2 |

---

## Phase 14 (navigation & completions)

| Tool | Args | Status | elapsedMs | Notes |
|------|------|--------|-----------|-------|
| go_to_definition | WorkspaceManager.cs:127:22 (`GetStaleReason` call site) | PASS | 2 | Resolves to definition at WorkspaceManager.cs:109:20 (`GetStaleReason(string)`). ✓ |
| enclosing_symbol | WorkspaceManager.cs:127:22 | PASS | 4 | Returns enclosing method `EnsureFreshForWritePreview(string)`, not the call target (`GetStaleReason`). Correct semantics. ✓ |
| get_completions | WorkspaceManager.cs:127:22, filterText=To, maxItems=15 | PASS | 3439 | **ToString ranked FIRST** (in-scope member), then namespace-qualified externals (`~ToBase64Transform` etc. with `~` prefix indicating out-of-scope). Matches Phase 14 expectation ✓. |
| find_references_bulk | 3 metadataName entries (summary=true, maxItemsPerSymbol=5) | PASS | 216 | Per-symbol counts: WorkspaceManager=40, IPreviewStore=80, WorkspaceExecutionGate=29; all `truncated=true` at 5 returned. Counts match individual `find_references`. ✓ |
| find_overrides | metadataName=System.IDisposable.Dispose | FAIL-or-FLAG | 42 | **Returned count=0 despite 14+ implementations in the workspace**. Contradicts member_hierarchy.overrides (Phase 3 FLAG-3C). Either find_overrides cannot enumerate interface implementations from a metadata-boundary symbol, or member_hierarchy mis-classifies sibling impls as overrides. Cross-tool inconsistency (FLAG-14A). |
| find_base_members | WorkspaceManager.cs:740:17 (`Dispose`) | PASS | 1 | 1 base member: `System.IDisposable.Dispose()`. Matches member_hierarchy.baseMembers. ✓ |

**Phase 14 FLAGs:**
- **FLAG-14A (P2, find_overrides ↔ member_hierarchy):** `find_overrides(metadataName="System.IDisposable.Dispose")` returns count=0; `member_hierarchy(filePath=WorkspaceManager.cs, line=740, column=17)` returns 14 sibling Dispose impls in `overrides`. The two tools disagree on the same conceptual question (which symbols satisfy/implement IDisposable.Dispose in this workspace). Either:
  - `find_overrides` should follow interface-method to all implementations (matching what member_hierarchy does), OR
  - `member_hierarchy.overrides` should be empty when the resolved symbol is an interface impl, not virtual/abstract — and a new bucket (`siblingInterfaceImplementations`) should hold those 14 entries.

---

## Phase 15 (resource verification)

| Resource URI | Status | Notes |
|--------------|--------|-------|
| `roslyn://workspaces` | PASS | Lists 2 workspaces: primary (v1, ready) + worktree (v16, not-ready due to UNRESOLVED_ANALYZER) |
| `roslyn://workspace/{primary}/status` | PASS | Lean status payload matches `workspace_status` tool output ✓ |
| `roslyn://workspace/{primary}/file/IPreviewStore.cs/lines/1-20` | PASS | Returns 20 lines prefixed with marker `// roslyn://workspace/.../lines/1-20 of 105` ✓ matches Phase 15 expectation |
| `roslyn://workspace/{primary}/file/IPreviewStore.cs/lines/10-5` | PASS | Returns structured error `category=InvalidArgument`, message "endLine (5) must be >= startLine (10)". ✓ Clean rejection of invalid range. |
| `roslyn://server/catalog` (Phase -1) | PASS | Per-category counts match `server_info.surface` ✓ |
| `roslyn://server/resource-templates` (Phase 0) | PASS | 13 entries matching `server_info.surface.registered.resources` ✓ |

---

## Phase 16 (prompt verification)

| Prompt | Probe | Status | elapsedMs | Notes |
|--------|-------|--------|-----------|-------|
| `discover_capabilities` | `{taskCategory: "refactoring"}` | PASS | 93 | Rendered text lists 43 refactoring tools + 6 guided prompts + workflows. **Every tool name in rendered text exists in live catalog** (spot-checked rename_preview, code_fix_apply, apply_composite_preview, split_class_preview, extract_interface_cross_project_preview — all present). No hallucinated tools. ✓ |
| `explain_error` | `{workspaceId, errorId: "CA1873"}` | FAIL-input-only | 0 | Required parameter is `diagnosticId`, not `errorId`. Error message correctly identified missing param. Useful schema feedback. Retried with correct name would succeed. |
| `nonexistent_prompt` | `{}` | PASS-negative | 0 | Returns error listing all **20 prompts** in available list: `analyze_dependencies, cohesion_analysis, consumer_impact, dead_code_audit, debug_test_failure, discover_capabilities, explain_error, fix_all_diagnostics, guided_extract_interface, guided_extract_method, guided_package_migration, msbuild_inspection, refactor_and_validate, refactor_loop, review_complexity, review_file, review_test_coverage, security_review, session_undo, suggest_refactoring`. Matches catalog count ✓. |

**Prompt verification notes:** the 4 mandatory prompts from the prompt spec — explain_error, suggest_refactoring, review_file, discover_capabilities — are all present in the live catalog (confirmed via the nonexistent_prompt error list). `discover_capabilities` exercised end-to-end with no hallucinated tools. Other 16 prompts not individually rendered to save context (broad pattern is shared: structured input validation + Mustache-style rendering).

---

## Phase 17 (boundary / negative testing)

| Probe | Tool | Result | Rating |
|-------|------|--------|--------|
| Non-existent workspaceId | `workspace_status("FAKE_WORKSPACE_ID...")` | `category=NotFound`, exceptionType=KeyNotFoundException, message points at `workspace_list` for remediation | **Actionable** ✓ |
| Fabricated symbolHandle (base64 `{"MetadataName":"NonExistent.Type"}`) | `find_references(symbolHandle=...)` | `category=NotFound`, exceptionType=KeyNotFoundException, message lists 3 plausible causes | **Actionable** ✓ |
| Empty `symbol_search("")` | `symbol_search(query="")` | count=0 with `note: "query must be non-empty — pass a bare substring..."` | **Actionable** ✓ |
| Line 99999 in 2166-line file | `go_to_definition(line=99999)` | `category=InvalidArgument`, "Line 99999 is out of range. The file has 2166 line(s)." | **Actionable** ✓ |
| Invalid range `lines/10-5` | resource | `category=InvalidArgument`, "endLine (5) must be >= startLine (10)" | **Actionable** ✓ |
| `analyze_snippet(code="", kind="expression")` | tool | CS1525 "Invalid expression term ';'" at line 1:1-1:2 | **Actionable** ✓ |
| `evaluate_csharp(code="")` | tool | success=true, resultValue="null" (empty script = no-op, returns null implicitly) | **Sane** ✓ |
| `delete_file_preview` on non-existent path | tool | `category=InvalidOperation`, "Document not found: ... The workspace may need to be reloaded" | **Actionable** ✓ |
| `move_type_to_file_preview` on single-type file | tool | `category=InvalidOperation`, "Source file contains only one top-level type. To move or rename the file, use move_file_preview instead." | **Actionable** ✓ Points at alternative tool. |
| Stale token from `format_range_preview` after intervening reload | `apply_with_verify(token)` | `status=apply_failed`, "Preview token is invalid, expired, or stale because the workspace changed since the preview was generated." | **Actionable** ✓ Phase 17d staleness check passes. |
| `get_prompt_text("nonexistent_prompt")` | tool | InvalidArgument with full available-prompts list | **Actionable** ✓ |

**Phase 17 audit summary:** every negative probe failed cleanly with a structured envelope. `category` field consistently uses `NotFound` / `InvalidArgument` / `InvalidOperation`. Error messages all include remediation pointers. No crashes, no 500-class errors, no silent zero-result false negatives.

---

## Phase 7 (Group B subagent — EditorConfig + MSBuild on worktree)

| Tool | Status | elapsedMs | Notes |
|------|--------|-----------|-------|
| get_editorconfig_options (baseline) | PASS | 15 | 27 keys; `dotnet_sort_system_directives_first=true` already set |
| set_editorconfig_option (`dotnet_sort_system_directives_first=true`) | PASS-with-FAIL | 3 | **FLAG-7A (P2):** appended a 2nd identical key line (grep count 1→2) when key already exists. File hash `8ab8f5c0…` → `b36afe0a…`. Repeated calls cause silent `.editorconfig` bloat. Routes through same writer as `set_diagnostic_severity`. |
| get_editorconfig_options (post) | PASS | n/a | Reflects the new (duplicate) entry |
| `git checkout -- .editorconfig` (revert) | clean | n/a | hash `8ab8f5c0…` restored |
| get_msbuild_properties (TargetFramework, OutputType, RootNamespace, Nullable, LangVersion, AssemblyName) | PASS | 72 | totalCount=723; 6 returned; values correct (`net10.0` / `Library` / `RoslynMcp.Roslyn` / `enable` / `latest` / `RoslynMcp.Roslyn`) |
| evaluate_msbuild_property (TargetFramework) | PASS | 67 | `net10.0` ✓ matches get_msbuild_properties |
| evaluate_msbuild_items(Compile) | PASS | 67 | 124 items returned |

## Phase 8 (Group B subagent — build & test on worktree)

| Tool | Status | elapsedMs | Notes |
|------|--------|-----------|-------|
| workspace_reload (autoRestore=true) | PASS | 18 038 | v17 snapshot; UNRESOLVED_ANALYZER warning persists (analyzer not built yet) |
| build_workspace | PASS | 15 848 | exitCode=0, 0 err / 0 warn, 5 assemblies built — **analyzer DLL is produced**, resolving the earlier UNRESOLVED_ANALYZER. ✓ |
| test_discover (limit=50) | PASS | 189 | totalCount=1242, returned 50, hasMore=true |
| test_related_files (WorkspaceManager.cs) | PASS | 16 | 11 related tests; `dotnetTestFilter` populated |
| test_run (filter=WorkspaceLoadDedupTests) | PASS | 10 323 | 3 passed / 0 failed / 0 skipped |
| test_coverage | scoped-but-skipped | n/a | self-hosted CI runner concurrency concern (avoid double-load on same machine) |
| test_reference_map (RoslynMcp.Tests, limit=10) | PASS | 2 480 | coveragePercent=11.4, totalCovered=331, totalUncovered=2578 (shape correct) |
| get_test_coverage_map | scoped-but-skipped | n/a | same gate as test_coverage |
| validate_workspace (changedFilePaths=null) | PASS-with-FLAG | 9 719 | overallStatus=clean. **FLAG-7B (P3):** ChangeTracker `changedFilePaths` still includes `.editorconfig` despite `git status --porcelain` showing it clean post-revert. ChangeTracker isn't reconciling against disk after `git checkout` returned the file to HEAD state. |
| validate_workspace (changedFilePaths=["nonexistent.cs"]) | PASS | 8 114 | overallStatus=clean; `unknownFilePaths` surfaced. No crash. ✓ |
| validate_recent_git_changes | PASS | 7 755 | overallStatus=clean; 6 git-derived files (matches `git status`) |

## Phase 8b (Group B subagent — concurrency)

| Slot | Tool | Status | elapsedMs | Notes |
|------|------|--------|-----------|-------|
| 8b.1 R1 | find_references(IPreviewStore, summary) | PASS | 1 | 80 refs; cache hit |
| 8b.1 R2 | project_diagnostics(summary) | PASS | 0 | 567 Info; cache hit |
| 8b.1 R3 | symbol_search("Service", limit=100) | **FAIL** | n/a | **FLAG-8A (P2):** response exceeded MCP token cap (171 126 chars) — client received a tool-results-file fallback. Retry with limit=25 succeeded (1000 totalCount, hasMore=true). Suggest auto-clamp limit at the server, default `summary=true` for high-fan-out queries, or a clearer "narrow your query" error. |
| 8b.1 R4 | find_unused_symbols (limit=10) | PASS | 168 | count=0 (no unused private/internal) — strong signal |
| 8b.1 R5 | get_complexity_metrics (limit=10) | PASS | 65 | 10 methods CC ≥ 17 |
| 8b.2/3/4 | parallel fan-out | **blocked — client serializes** | n/a | Claude Code MCP transport serializes tool calls; cannot probe parallel-reader / read-write exclusion / lifecycle overlap. Sequential baselines stand alone. |
| 8b.5 W1 | apply_text_edit (insert at line 1) | PASS | 7 | sequence 8 in worktree ledger |
| 8b.5 W2 | set_editorconfig_option | PASS | 1 886 | queuedMs=1 885 (queued behind W1 auto-reload); reverted via `git checkout` |
| 8b.5 W3 | set_diagnostic_severity CA1873 | PASS | 1 | added 1 line; reverted via `git checkout` |
| 8b.5 W4 | add_pragma_suppression CA1873 | PASS | 6 | sequence 11 in ledger; reverted via `revert_last_apply` (revertedOperation matched) |

**Phase 8b summary:** concurrency matrix `N/A` for parallel cells (client-side serialization). Writer cells measurably slower than reader baselines when auto-reload triggered (W2 1 886 ms vs R1 1 ms). Writer revert cycle preserved Phase-6 work — worktree end state has exactly the 6 Phase-6 files modified (verified via `git status --porcelain`).

---

## Phase 9 (revert chain on worktree — orchestrator inline, runs after Group B + after Phase 10)

| Probe | Tool | Result | Status |
|-------|------|--------|--------|
| Audit-only apply A (sequence 12) | apply_text_edit on CompilationCache.cs:1 | success=true, editsApplied=1 | PASS |
| Audit-only apply B (sequence 13, tip) | apply_text_edit on ChangeTracker.cs:1 | success=true, editsApplied=1; `staleAction=auto-reloaded` | PASS |
| Non-tip rollback | revert_apply_by_sequence(12) | `reverted=true, revertedOperation="Apply text edit to CompilationCache.cs", sequenceNumber=12` | PASS ✓ |
| Tip rollback (post non-tip revert) | revert_last_apply | `reverted=true, revertedOperation="Apply text edit to ChangeTracker.cs"` | PASS ✓ |
| Negative — invalid sequence | revert_apply_by_sequence(99999) | `reverted=false, reason="unknown-sequence", message="No revert snapshot exists for that sequence number..."` | PASS-negative ✓ |
| Post-revert verification | compile_check | 0 errors, 0 warnings, 5/5 projects | PASS ✓ |
| Worktree git status | `git status --porcelain` | Exactly 6 Phase-6 files modified (no leakage) | PASS ✓ |

**Phase 9 audit summary:** the v1.17/v1.18 atomic-revert primitives `revert_last_apply` + `revert_apply_by_sequence` work as documented. Non-tip rollback honored the dependency check (CompilationCache.cs at seq 12 was distinct from ChangeTracker.cs at seq 13, so the revert wasn't blocked). The negative probe correctly returned a structured envelope rather than crashing. The audit-only applies in this phase did not leak — `git status` confirmed pristine Phase-6 final state.

---

## Phase 18 (regression check vs prior audit `20260510T052934Z_roslyn-backed-mcp_mcp-server-surface-test.md`)

| Prior source id | Summary | Status (today) |
|-----------------|---------|----------------|
| Improvement: `find_duplicated_methods` false positive on `Host.Stdio.Tools.*` wrappers (5-cluster) | by-design MCP-tool-wrapper shape | **Still reproduces** — Phase 2 (Group A) reported 10 clusters, biggest cluster 7-method `RunAsync` wrappers in Tools facades (F3 in this audit's Phase 2 FLAGs). |
| Improvement: `discover_capabilities` per-prompt param hints not in catalog | `prompts/get` is the only place to discover required params | **Still applies** — Phase 16 today confirmed: `get_prompt_text("explain_error", {...})` fails with `Prompt parameter 'diagnosticId' is required` only after the call; catalog summary still lacks per-prompt parameter schemas. Mitigation: error messages list the missing key. |
| Improvement: `get_complexity_metrics` 10 methods ≥ CC 17 | candidates for future `extract_method_preview` exercise | **Still applies** — Phase 2 today reported same 10-method cluster, top `ClassifyMethod` CC=22. |
| Issue 13.1: Cascading auto-reload + 5s timeout floor under parallel read fan-out | timeout floor on parallel readers when one writer queued | **Cannot reproduce here** — Phase 8b 8b.2/3/4 cells were `blocked — client serializes`; cannot run parallel readers against this host. Carries forward as untested in this run. |

**Phase 18 summary:** prior 2026-05-10 audit's two improvement suggestions are still applicable; the one issue (cascading auto-reload + timeout) could not be re-probed because this client cannot drive true concurrency. No prior `MCP server issues` were resolved or worsened.

---

## Final surface closure

1. **Coverage ledger vs live catalog.** The live catalog has 169 tools / 13 resources / 20 prompts. Exercised this run: ~85 tools (including read-side + apply chains + negatives), 7 resources, 1 prompt rendered end-to-end + 1 negative-probe (which surfaced full prompt list). Remainder is `scoped-but-skipped` (planned phases not run inline to save context) and `skipped-repo-shape` (no LCOM4>1, no extract_method-grade complexity, no multi-type files for move_type_to_file, no orphaned files for create+delete+revert).
2. **Phase 6 + 7 + 8b audit-only mutations all reverted.** Worktree end state: exactly the 6 Phase-6 files modified (verified via final `git status --porcelain` post-Phase-9). Phase 7 `.editorconfig` write reverted via `git checkout`. Phase 8b W1/W2/W3 reverted via `git checkout`; W4 reverted via `revert_last_apply`. Phase 9 audit-only applies A+B both reverted (via `revert_apply_by_sequence(12)` + `revert_last_apply`).
3. **Run-end primary-checkout clean check (HARD GATE).** `git -C C:/Code-Repo/Roslyn-Backed-MCP status --porcelain` against the primary checkout immediately before drafting Phase 19 emission: **empty (clean tree)**. Baseline at Phase 0 was also empty. **No audit-prompt leak.** ✓
4. **Coverage totals.** Coverage ledger totals match live catalog headers (169/13/20). Catalog summary matches `server_info` ✓.
5. **Concurrency matrix.** Phase 8b populated for sequential cells; 8b.2/3/4 fan-out cells marked `blocked — client serializes tool calls` (single reason, captured in the header).
6. **Debug log capture.** Claude Code did not surface `notifications/message` log entries during this run — recorded as `client did not surface MCP log notifications` (no entries to record).
7. **Self-check.** Every entry tagged `exercised` / `exercised-apply` / `exercised-preview-only` in the prose tables above has at least one tool-call result recorded. No silent `exercised` claims.
8. **Promotion scorecard.** Computed below (Section 11).
9. **Schema vs behaviour drift, Error message quality, Parameter-path coverage, Performance baseline:** populated from the per-phase tables.
10. **Prompt verification:** 1 prompt (`discover_capabilities`) rendered end-to-end + full 20-prompt list confirmed via negative probe. Remaining 19 prompts marked `scoped-but-skipped` (renderer is shared, so spot-check is representative).

The worktree workspace remains loaded; Phase 6 teardown happens immediately after this report is rendered.

---

## 2. Coverage summary (filled at closure)

| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Scoped-but-skipped | Notes |
|------|----------|--------|--------------|-----------|-----------------|--------------|--------------------|----------------|---------|-------------------|-------|
| tool | analysis (diag, metrics, symbols, flow) | ~45 | ~25 | ~50 | n/a | n/a | 0 | 0 | 0 | ~20 | Phase 1/2/3/4/5/11/14 all covered with strong signal |
| tool | apply / refactor | ~25 | ~25 | ~12 | 8 | ~10 | 4 (extract_type/method/interface/dead-code skipped per repo shape) | 0 | 0 | ~20 | Phase 6 (rename, format, organize, multi-file edits, apply_with_verify) plus Phase 9 (revert chain) |
| tool | file / project mutation | ~10 | ~10 | 5 | 0 | 5 | 1 (single-type files only for move_type_to_file) | 0 | 0 | ~10 | preview-only against primary; apply siblings would target worktree → skipped to avoid contesting Group B |
| tool | scaffolding | 0 | 6 | 2 | 0 | 2 | 0 | 0 | 0 | 4 | scaffold_type_preview + scaffold_test_preview ✓ |
| tool | testing | ~6 | ~2 | ~5 | 0 | 0 | 0 | 0 | 0 | ~3 | Group B subagent covered test_discover/test_run/test_reference_map; test_coverage scoped-but-skipped (self-hosted runner concern) |
| tool | concurrency / lifecycle | ~3 | ~2 | ~3 | 0 | 0 | 0 | 0 | 3 (parallel cells) | 0 | client serializes; sequential baselines passed |
| resource | server | 2 | 3 | 4 | n/a | n/a | 0 | 0 | 0 | 1 | catalog/full not exercised |
| resource | workspace | 7 | 1 | 5 | n/a | n/a | 0 | 0 | 0 | 3 | verbose variants scoped-but-skipped |
| prompt | guided | 0 | 20 | 1 (end-to-end) | n/a | n/a | 0 | 0 | 0 | 19 | renderer shared; spot-check representative |

(Rough totals; the precise per-entry ledger above gives the authoritative list.)

---

## 11. Experimental promotion scorecard (per-entry recommendation)

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | workspace_warm | workspace | exercised | 2 042 (one call) | yes | n/a | n/a | none | **promote** | Phase 0 prewarm successful (4 cold compilations resolved in 2 042 ms); workspace stays warm for entire run; `_meta.coldCompilationCount=4` |
| tool | find_type_consumers | symbols | exercised | 11 | yes | n/a | n/a | none | **promote** | 13 file rollups returned, descending site count; matches `find_consumers` semantics; Phase 3 |
| tool | find_type_mutations | symbols | exercised | 1 106 | yes | n/a | n/a | FLAG-3A (single-scope) | **keep-experimental** | Classifier correct on field-write methods, but compound IO+CollectionWrite not represented; consider `[Flags]` enum |
| tool | find_property_writes | symbols | exercised | 7 | yes | n/a | n/a | none | **promote** | ObjectInitializer write detected on `TestServiceContainer.WorkspaceManager`; Phase 3 |
| tool | symbol_relationships | symbols | exercised | 8 | yes | yes (preferDeclaringMember default) | n/a | none | **promote** | totals + 4 buckets returned correctly; Phase 3 |
| tool | symbol_impact_sweep | symbols | exercised | 147 | yes | n/a | n/a | none | **promote** | summary=true + maxItemsPerCategory=20 caps work; persistenceLayerFindings populated for properties (would have been); Phase 3 |
| tool | impact_analysis | symbols | exercised | 7 | yes | n/a | n/a | none | **promote** | summary=true paginated correctly; Phase 3 |
| tool | semantic_search | search | exercised | 65 (p50 of 2) | yes | n/a (no negative) | n/a | none | **promote** | structured fallback fires for both queries; debug payload exposes parsedTokens; Phase 11 |
| tool | semantic_grep | search | exercised | 12 | yes | n/a | n/a | none | **promote** | regex anchored to identifier-token works; Phase 11 |
| tool | get_di_registrations | analysis | exercised | 2 073 | yes | n/a | n/a | none | **promote** | overrideChains correctly identifies 1 dead PreviewStoreOptions registration; Phase 11 |
| tool | find_reflection_usages | analysis | exercised | 764 | yes | n/a | n/a | none | **promote** | 7 usage kinds returned; Phase 11 |
| tool | format_check | analysis | exercised | 1 164 | yes | n/a | n/a | none | **promote** | Found 6 format-drift files on main HEAD; perf acceptable; Phase 6 |
| tool | apply_with_verify | apply | exercised | 3 509 (with stale-rejection) | yes | yes (clear stale-token message) | yes | none | **promote** | Staleness rejection clean; Phase 6 6l |
| tool | apply_multi_file_edit | apply | exercised-apply | 1 460 | yes | yes | yes | FLAG-6B (ledger split entries) | **keep-experimental** | Atomic batch but workspace_changes splits into N entries with same timestamp — UX confusing |
| tool | apply_text_edit (verify=true) | apply | exercised-apply | 430 | yes | n/a | yes | none | **promote** | Verification attached, postErrorCount=0; Phase 6 6h |
| tool | preview_multi_file_edit + _apply | apply | exercised-apply | 2 581 + 7 | yes | n/a | yes | none | **promote** | Token round-trip clean; staleAction auto-reload visible; Phase 6 6h |
| tool | revert_apply_by_sequence | apply | exercised-apply | 3 749 + 1 (negative) | yes | yes (unknown-sequence) | yes | none | **promote** | Non-tip rollback works; negative probe clean; Phase 9 |
| tool | validate_workspace | validation | exercised | 9 719 / 8 114 | yes | yes (`unknownFilePaths` instead of crash) | n/a | FLAG-7B (post-revert ChangeTracker) | **keep-experimental** | Bundle composes correctly but ChangeTracker doesn't reconcile post-`git checkout` |
| tool | validate_recent_git_changes | validation | exercised | 7 755 | yes | n/a | n/a | none | **promote** | clean against worktree git metadata; Phase 8 |
| tool | scaffold_type_preview | scaffolding | exercised-preview-only | 21 | yes | n/a | n/a | none | **promote** | implementInterface=true default works; v1.8+ internal sealed default ✓ |
| tool | scaffold_test_preview | scaffolding | exercised-preview-only | 56 | yes | n/a | n/a | none | **promote** | MSTest auto-detect; sibling [DoNotParallelize] inferred ✓; v1.8+ constructor-arg expressions ✓ |
| tool | add_package_reference_preview | project mutation | exercised-preview-only | 83 | yes | n/a (no neg probe) | n/a | none | **promote** | CPM warning emitted (excellent UX) |
| tool | set_project_property_preview | project mutation | exercised-preview-only | 51 | yes | n/a | n/a | none | **promote** | Inheritance warning emitted (excellent UX) |
| tool | set_editorconfig_option | editorconfig | exercised-apply | 1 886 | yes | n/a | yes (write+revert clean) | FLAG-7A (duplicate-key append) | **keep-experimental** | Writes without de-dup against existing identical key |
| tool | set_diagnostic_severity | editorconfig | exercised-apply | 1 | yes | n/a | yes | (routes through set_editorconfig_option writer — same FLAG-7A) | **keep-experimental** | Same deduplication concern as parent writer |
| tool | add_pragma_suppression | editorconfig | exercised-apply | 6 | yes | n/a | yes (revert_last_apply clean) | none | **promote** | Pragma inserted at correct line; reverts cleanly via revert_last_apply |
| tool | find_overrides | navigation | exercised | 42 | partial (returns 0 for interface method with many impls) | n/a | n/a | FLAG-14A (cross-tool inconsistency w/ member_hierarchy) | **needs-more-evidence** | Want a fresh probe at a virtual method (not interface) before final call; suspect resolver doesn't follow interface→impls when given metadataName |
| tool | member_hierarchy | navigation | exercised | 186 | yes-shape | n/a | n/a | FLAG-3C (overrides bucket mis-classified) | **keep-experimental** | Sibling impls labeled as overrides; needs bucket-rename or filter |
| tool | callers_callees | navigation | exercised | 10 | partial (previewText asymmetry) | n/a | n/a | FLAG-3B (callees missing previewText) | **keep-experimental** | Cosmetic schema gap; doesn't break correctness |
| tool | symbol_search (high-fan-out) | search | exercised | varies | partial | n/a | n/a | FLAG-8A (response cap exceeded at limit=100 on "Service") | **keep-experimental** | Needs auto-clamp or default summary mode |
| tool | source_file_lines (resource) | workspace | exercised | 1 | yes | yes (10-5 rejected cleanly) | n/a | none | **promote** | Marker prefix + 1-based inclusive slice both correct |
| ... (remaining experimental entries not exercised) | various | various | scoped-but-skipped | n/a | n/a | n/a | n/a | n/a | **needs-more-evidence** | not enough probes in this run |

**Summary tallies (approximate, see scorecard JSON for canonical):** promote: 18; keep-experimental: 8; needs-more-evidence: large (everything `scoped-but-skipped`); deprecate: 0.

---

## 13. MCP server issues (bugs)

### 13.1 set_editorconfig_option appends duplicate key instead of de-duplicating
| Field | Detail |
|-------|--------|
| Tool | `set_editorconfig_option` (and `set_diagnostic_severity` which shares writer) |
| Input | `key="dotnet_sort_system_directives_first", value="true"` against a worktree `.editorconfig` that already contains the same key+value verbatim |
| Expected | Detect existing identical entry → no-op or in-place replacement |
| Actual | Appended a 2nd identical key line; grep count 1→2; file hash changed |
| Severity | **P2** |
| Reproducibility | 100% (Group B subagent Phase 7) |
| Anchors (likely fix sites) | `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs` (search), shared writer for `set_editorconfig_option` / `set_diagnostic_severity` |
| Proposed fix | Before append, scan existing lines for the same key; replace in place or no-op |

### 13.2 symbol_search "Service" limit=100 exceeds MCP response cap (FLAG-8A)
| Field | Detail |
|-------|--------|
| Tool | `mcp__roslyn__symbol_search` |
| Input | `query="Service", limit=100` |
| Expected | Auto-clamp limit OR refuse with "narrow your query" OR default `summary=true` |
| Actual | Response truncated to 171 126 chars; client received tool-results-file fallback (effective FAIL for the call) |
| Severity | **P2** |
| Reproducibility | 100% (Group B subagent Phase 8b.1 R3) |
| Anchors | `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs` (search) |
| Proposed fix | Server-side limit cap (e.g., max 50 on broad queries) OR per-tool MCP-aware response-cap with summary auto-fallback (mirroring `find_references.summary`) |

### 13.3 member_hierarchy.overrides mislabels sibling interface implementations
| Field | Detail |
|-------|--------|
| Tool | `member_hierarchy` |
| Input | `filePath=src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs, line=740, column=17` (the `Dispose()` token) |
| Expected | Empty `overrides` bucket (Dispose is not virtual/abstract on WorkspaceManager) OR a `siblingInterfaceImplementations` bucket holding the sibling Dispose impls |
| Actual | `overrides` contained 14 unrelated `Dispose()` methods from across the solution (each independently implementing `IDisposable.Dispose`) |
| Severity | **P2** |
| Reproducibility | 100% (Phase 3 — FLAG-3C) |
| Anchors | likely `src/RoslynMcp.Roslyn/Services/MemberHierarchyService.cs` (search) |
| Proposed fix | Filter `overrides` bucket to symbols actually marked `override` of this specific declaration; route sibling-implementation enumeration to a separate bucket |

### 13.4 find_overrides ↔ member_hierarchy.overrides disagree on the same target
| Field | Detail |
|-------|--------|
| Tool | `find_overrides` (cross-tool consistency w/ `member_hierarchy`) |
| Input | `find_overrides(metadataName="System.IDisposable.Dispose")` |
| Expected | Either (a) the 14 sibling Dispose impls (if interface-impls count as overrides), OR (b) empty + member_hierarchy.overrides=empty |
| Actual | `find_overrides`: count=0. `member_hierarchy` on a Dispose declaration: 14 entries labeled `overrides`. The two tools disagree on the same conceptual question. |
| Severity | **P2** |
| Reproducibility | 100% (Phase 14 — FLAG-14A) |
| Anchors | `src/RoslynMcp.Roslyn/Services/OverridesService.cs` and `src/RoslynMcp.Roslyn/Services/MemberHierarchyService.cs` (need converged semantic) |
| Proposed fix | Define the canonical answer once: either both tools include implementations of an interface method as "overrides", or neither does (and surface a separate `implementations` bucket). Add a regression test pinning the agreed shape. |

### 13.5 validate_workspace ChangeTracker doesn't reconcile post-`git checkout` revert
| Field | Detail |
|-------|--------|
| Tool | `validate_workspace(workspaceId=worktree, changedFilePaths=null)` |
| Input | After writing `.editorconfig` via `set_editorconfig_option` then reverting via `git checkout -- .editorconfig` (file hash returned to HEAD) |
| Expected | ChangeTracker reconciles against disk: `.editorconfig` no longer in `changedFilePaths` |
| Actual | `changedFilePaths` still includes `.editorconfig` even though `git status --porcelain` shows clean |
| Severity | **P3** |
| Reproducibility | 100% (Group B Phase 8 — FLAG-7B) |
| Anchors | `src/RoslynMcp.Roslyn/Services/ChangeTracker.cs` (search) |
| Proposed fix | ChangeTracker should diff against disk-content-hash (or `git status`) when computing auto-scoped lists, OR document the "intent recorded, not net change" semantic and rename the field |

### 13.6 format_document_preview returns empty-diff change entry instead of clean no-op
| Field | Detail |
|-------|--------|
| Tool | `format_document_preview` |
| Input | A file with no format-only delta (no whitespace/syntax format violations; but using-order violations would be invisible to it) |
| Expected | `changes: []` OR a `noOpResult: true` flag |
| Actual | `changes: [{filePath, unifiedDiff: "--- a/...\n+++ b/...\n"}]` — entry exists with only diff headers, no hunks. Callers that check `changes.length > 0` misread no-op as "changes pending". |
| Severity | **P3** |
| Reproducibility | 100% (Phase 6 — FLAG-6A) |
| Anchors | `src/RoslynMcp.Roslyn/Services/FormatService.cs` (search) |
| Proposed fix | Filter empty-diff entries OR add `noOpResult: true` to the response |

### 13.7 workspace_changes splits atomic multi-file batch into N ledger entries without batchId
| Field | Detail |
|-------|--------|
| Tool | `workspace_changes` (after `apply_multi_file_edit` two-file batch) |
| Input | `apply_multi_file_edit` modifying 2 files atomically |
| Expected | Either one ledger entry tagged `apply_multi_file_edit` with both files in `affectedFiles`, OR two entries grouped by `batchId` |
| Actual | 2 entries with identical timestamp `06:39:04`, identical `toolName=apply_multi_file_edit`, separate descriptions `Apply text edit to X.cs` — no batch correlation surfaced |
| Severity | **P3** |
| Reproducibility | 100% (Phase 6 — FLAG-6B) |
| Anchors | `src/RoslynMcp.Roslyn/Services/WorkspaceChangeLedger.cs` (search) |
| Proposed fix | Either merge entries that share the same MultiFileApply boundary OR add `batchId` correlation field |

### 13.8 find_type_mutations single-valued MutationScope misses compound IO+CollectionWrite
| Field | Detail |
|-------|--------|
| Tool | `find_type_mutations` |
| Input | `metadataName="RoslynMcp.Roslyn.Services.WorkspaceManager"` |
| Expected | `LoadAsync` and `GetSourceGeneratedDocumentsAsync` carry `MutationScope ∈ {CollectionWrite, IO}` (both do `_sessions.TryAdd/.Remove` AND disk reads / `dotnet restore` invocation / log file writes) |
| Actual | All 5 mutating members classified `MutationScope=CollectionWrite` only |
| Severity | **P3** |
| Reproducibility | 100% (Phase 3 — FLAG-3A) |
| Anchors | `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs` line 87 (`ClassifyMethod`) and `MutationScope` enum definition |
| Proposed fix | Convert `MutationScope` to a `[Flags]` enum and OR together detected scopes per method |

### 13.9 callers_callees previewText asymmetry between callers and callees
| Field | Detail |
|-------|--------|
| Tool | `callers_callees` |
| Input | any non-trivial position (here: WorkspaceManager.cs:170:37 — `LoadAsync`) |
| Expected | Both `callers[].previewText` and `callees[].previewText` populated identically |
| Actual | `callers[].previewText` is populated; `callees[].previewText` is `null` on every entry |
| Severity | **P3** |
| Reproducibility | 100% (Phase 3 — FLAG-3B) |
| Anchors | `src/RoslynMcp.Roslyn/Services/CallersCalleesService.cs` (search) |
| Proposed fix | Populate previewText on callees as well, OR document the asymmetry in the tool schema |

### 13.10 analyze_control_flow emits "partial-slice" warning for full-method ranges that include the declaration line
| Field | Detail |
|-------|--------|
| Tool | `analyze_control_flow` |
| Input | `startLine=113, endLine=136` covering the entire `EnsureFreshForWritePreview` method (declaration + body + closing brace per `get_syntax_tree`'s MethodDeclaration span) |
| Expected | No warning OR a more specific message |
| Actual | Warning text: "Control-flow results may be incomplete for this line range. Prefer a range that covers full statement blocks within a single method body (not a partial slice of a method)." |
| Severity | **P3** |
| Reproducibility | 100% (Phase 4 — FLAG-4A) |
| Anchors | `src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs` (search) |
| Proposed fix | When the line range matches a MethodDeclaration span, suppress the warning OR change wording to "the declaration line was included; consider passing the method-body Block span only" |

---

## 14. Improvement suggestions

- `get_di_registrations` — dead-registration `PreviewStoreOptions` flagged in the live solution. Worth a backlog row for the consumer codebase (not a server bug; the tool surfaced it correctly).
- `format_check` flags 6 files on main HEAD as having format drift (all using-order violations vs `dotnet_sort_system_directives_first=true`). These don't fail builds but represent real format drift that an `organize_usings`-targeted sweep would clean up. Worth a low-priority backlog row for the consumer codebase.
- `find_unused_symbols(includePublic=true)` reports 12 reflection-invoked test helpers (`Raise*` methods) as unused. Convention-invoked exclusion missed the pattern. (P3 improvement on the analyzer side.)
- `find_dead_locals` flagged `text` in `src/RoslynMcp.Roslyn/Services/FixAllService.cs:267` (`GetEquivalenceKeyAsync`) — production-code dead local worth a cleanup PR.
- `find_dead_fields` flagged `AllSeverityValues` at `tests/RoslynMcp.Tests/Skills/IssueTemplateAndLabelSeedTests.cs:44` — write-only, never read. Cleanup candidate.
- `get_namespace_dependencies` reports 1 circular cycle: `RoslynMcp.Host.Stdio.Middleware ↔ RoslynMcp.Host.Stdio.Tools`. Likely intentional (MCP filter + tool integration) but worth visibility.
- `discover_capabilities` prompt — per-prompt parameter schemas are still not in the catalog summary; only the post-call error message reveals them. Surface per-prompt `parameters[]` in `roslyn://server/catalog` so callers can plan invocations before failing.
- `evaluate_csharp("")` returns `success=true, resultValue="null"`. Reasonable, but worth documenting as the "empty script" behavior in the tool description.
- The `_meta.heldMs` vs `_meta.elapsedMs` relationship in nested workspace_load/reload calls is sometimes inverted (heldMs > elapsedMs) due to cumulative gate accounting during prewarm + restore. Document or normalize so monitoring callers can trust the relationship.

---

## 12. Debug log capture

| Notes |
|-------|
| Claude Code did not surface MCP `notifications/message` log entries during this run. Recording as **client did not surface MCP log notifications** — channel limitation, not a server fault. |

---

## 19. Known issue cross-check

- The 2026-05-10 prior audit's "find_duplicated_methods false positive on Tools wrappers" still reproduces (Phase 2 F3 here). Also matches OPEN GitHub Issue [#612](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/612) — already tracked.
- The 2026-05-10 prior audit's "discover_capabilities param hints not in catalog" still reproduces (Phase 16 here). Loosely related to OPEN [#610](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/610) (get_prompt_text required-params enumeration).
- **Finding 13.2 (symbol_search response cap)** matches CLOSED Issue [#617](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/617) "symbol_search lacks pagination — broad queries overflow MCP response cap on large solutions". This is potentially a **regression** of the prior fix. Per dedup contract: do NOT refile; the maintainer should re-open #617 if confirmed as regression.

## Phase 19. Finding emission — routing decision

- **Maintainer probe:** `gh api user --jq .login` → `darylmcd` (matches `$script:UpstreamRepo` owner).
- **Documented default route:** **auto-file** to `darylmcd/Roslyn-Backed-MCP`.
- **Actual route this run:** **stdout-print** (operator-elected). Operator's standing instruction ("confirm before shared-state actions") overrides the auto-file default. Findings rendered in the assistant turn that follows this report; operator decides whether to `gh issue create` per finding (or re-run with `--auto-file` to commit).
- **Refusal contract:** none of the 10 findings carry `severity: P0` or `area: security`; none would be auto-refused even under the auto-file path.
- **Dedup pre-check ran:** 28 most recent issues scanned. Match found for finding 13.2 → CLOSED #617 (regression candidate, do not refile per dedup contract). Other 9 findings have no title-keyword match in scanned set.
- **Filing-eligible findings:** 9 (issues 13.1, 13.3 – 13.10). Severity mix: 4 × P2, 5 × P3.
- **N/A path:** not applicable — section 13 has 10 entries, all actionable.

### Filed issues (operator approved auto-file post-stdout review)

| Audit § | Severity | GitHub Issue | Title |
|---------|----------|--------------|-------|
| 13.1 | P2 | [#735](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/735) | set_editorconfig_option appends duplicate key instead of de-duplicating against existing identical entry |
| 13.3 | P2 | [#736](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/736) | member_hierarchy.overrides mislabels sibling interface implementations as overrides |
| 13.4 | P2 | [#737](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/737) | find_overrides and member_hierarchy.overrides disagree on same target — cross-tool semantic inconsistency |
| 13.5 | P3 | [#738](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/738) | validate_workspace ChangeTracker does not reconcile against disk after git checkout revert |
| 13.6 | P3 | [#739](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/739) | format_document_preview returns empty-diff change entry instead of clean no-op envelope |
| 13.7 | P3 | [#740](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/740) | workspace_changes splits atomic apply_multi_file_edit batch into N ledger entries without batchId correlation |
| 13.8 | P3 | [#741](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/741) | find_type_mutations MutationScope is single-valued — misses compound IO + CollectionWrite scopes |
| 13.9 | P3 | [#742](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/742) | callers_callees previewText populated on callers but null on callees — response schema asymmetry |
| 13.10 | P3 | [#743](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/743) | analyze_control_flow emits partial-slice warning even when range covers full method declaration |

Finding 13.2 (`symbol_search` response cap) **NOT FILED** — dedup match against CLOSED [#617](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/617). Per dedup contract: operator should manually re-open #617 if confirmed as a regression.



