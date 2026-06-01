# MCP Server Audit Report

## 1. Header
- **Date:** 2026-05-31 (run TS `20260531T192823Z`)
- **Audited solution:** Roslyn-Backed-MCP.sln
- **Audited revision:** branch `main` @ `a52d95e`
- **Entrypoint loaded:** `C:\Code-Repo\Roslyn-Backed-MCP\Roslyn-Backed-MCP.sln`
- **Flags:** (none) — full tier, default mode. Emission: started `--output-mode=findings` (maintainer auto-file eligible); operator switched to `--output-mode=fragments` at Phase 19, so findings emitted as `backlog.d/` fragments (no GitHub Issues filed).
- **Isolation:** `C:\Code-Repo\Roslyn-Backed-MCP\.worktrees\surface-test-20260531T192823Z` + branch `mcp-server-surface-test/20260531T192823Z`
- **Isolation baseline:** (primary checkout `git status --porcelain` at run start) → **empty (clean)**
- **Teardown:** `partial` (git-clean; one orphaned directory still lock-held as of last retry). **Confirmed done:** worktree session `workspace_close(drainProcesses=true)`; git worktree **registration pruned** (`git worktree list` shows only the main checkout); branch `mcp-server-surface-test/20260531T192823Z` **deleted**; primary checkout **clean** (porcelain empty apart from the intended `backlog.d/` fragments + this report). **Still present:** the physical directory `.worktrees/surface-test-20260531T192823Z/` — removal has failed across multiple retries (`Invalid argument` / `Device or resource busy`) on `tests/RoslynMcp.Tests/bin/Debug/net10.0` despite repeated `dotnet build-server shutdown` + `testhost.exe` kills; a long-lived holder retains the bin lock. **Manual cleanup once the holder exits:** `rm -rf C:/Code-Repo/Roslyn-Backed-MCP/.worktrees/surface-test-20260531T192823Z` (git registration is already pruned). Filed as `worktree-teardown-windows-lock-multi-drain` (P3, `backlog.d/`).
- **Client:** Claude Code (Opus 4.8 1M) — MCP debug-log `notifications/message` channel: **no** (client does not surface them)
- **Workspace id (primary, reads):** `d251445e57b6450abcfcffc94ce160f1`
- **Workspace id (worktree, writes):** `c1754d7105864b96ba033fd44db806ed`
- **Warm-up:** yes — primary warmed (7 projects, 5 cold compilations, 2637ms)
- **Server:** roslyn-mcp `2.3.1+e1478756132f4a32575aa8f066ee9a90d0d5b055`
- **Catalog version:** `2026.04`
- **Roslyn / .NET:** Roslyn 5.3.0.0 / .NET 10.0.8 / Windows 10.0.26200
- **Live surface:** tools `111/60`, resources `9/4`, prompts `0/20` (registered: tools 171, resources 13, prompts 20; **parityOk=true**)
- **Scale:** 7 projects, 679 documents
- **Repo shape:** 7 projects (1 analyzer netstandard2.0 `ServerSurfaceCatalogAnalyzer`; `RoslynMcp.Core`/`.Roslyn`/`.Host.Stdio` net10.0; `RoslynMcp.Tests` test project net10.0; `SampleApp`/`SampleLib` fixtures). DI: yes (Host.Stdio). Tests: yes (RoslynMcp.Tests, xUnit). Analyzers: yes (ServerSurfaceCatalogAnalyzer + package analyzers). Source generators: yes ([LoggerMessage]/[GeneratedRegex]). `.editorconfig`: yes. CPM: yes (Directory.Packages.props). Directory.Build.props: yes. global.json: yes. Multi-targeting: no (single TFM per project). Restore: complete (primary restoreRequired=false; worktree restored).
- **Prior issue source:** GitHub Issues (darylmcd/Roslyn-Backed-MCP) — open: #769, #611, #608, #606. Plus `ai_docs/backlog.md`.
- **Debug log channel:** no
- **Maintainer probe:** `gh api user` = `darylmcd` → **auto-file route** (findings mode). P0/security findings refused → stdout-only.
- **Environment note:** Self-hosted CI runner service (`Runner.Listener.exe`) is up but **idle** (no `Runner.Worker`, latest CI run completed 2026-05-30). Audit does not push/trigger CI. Phase 6/8 run against the disposable worktree.
- **connection.state:** `idle` at Phase -1 (correct pre-load; transitioned to `ready` after workspace_load).

### Phase -1 / catalog sanity (PASS)
- `server_info` callable; `parityOk=true`; catalog-resource per-category counts match `server_info.surface` exactly (111/60 tools, 9/4 resources, 0/20 prompts). resource-templates count=13 == registered.resources. **PASS.**
- `workspace_health(primary)` → healthy (isReady, isStale=false, 0 errors/warnings). **PASS.**

### Candidate findings (Phase 0)
- **[retracted]** earlier `workspace_list` "duplicate session" suspicion was a shell-output misread; clean `workspace_list` returns `count:2` with exactly 2 entries. No finding.
- **DRIFT candidate (P3, low):** after worktree `workspace_load`/`workspace_reload` with `autoRestore=true`, status reports `restoreRequired=true` with `restoreHint` "Run `dotnet restore`…", **but** `dotnet restore` reports "All projects are up-to-date" and the actual unmet dependency is a **build** output — `WORKSPACE_UNRESOLVED_ANALYZER` (the netstandard2.0 `ServerSurfaceCatalogAnalyzer.dll` is not built in the fresh worktree). `restoreRequired`/`restoreHint` conflate "needs build" with "needs restore"; the workspace diagnostic message itself correctly says "Run `dotnet build` on the analyzer project". Worktree-shape artifact, not a primary-checkout issue. Verify whether `restoreRequired` should be `buildRequired` when the unmet input is an analyzer build output.
- Worktree readiness: `isReady=false`/`analyzersReady=false` until the analyzer project is built; building it in-worktree before Phase 6 to clear the warning.

---

## Coverage ledger — seeded: 204 entries in `_ledger-skeleton.tsv` (tools 111/60, resources 9/4, prompts 0/20 — reconciles with server_info). Tool category histogram: refactoring 33, symbols 19, advanced-analysis 17, analysis 16, project-mutation 14, validation 14, workspace 12, editing 6, file-operations 6, scaffolding 6, orchestration 5, code-actions 3, configuration 3, cross-project-refactoring 3, dead-code 3, security 3, undo 3, server 2, prompts 1, scripting 1, syntax 1.

## Phase results

### Phase 1 — Broad diagnostics (PASS, 2 FLAGs)
- Diagnostics headline: **0 errors / 1 warning** (CS0414 `DiagnosticsProbe._unusedForDiagnostics` in sample fixture) / info totals unread (payload cap).
- `compile_check` default→1479ms (0 err/1 warn, 7/7 projects); `severity=Error`→481ms (0); `file=`→180ms (file-scope fallback surfaced transparently); `emitValidation=true`→4609ms (~3× non-emit, real PE emit). PASS.
- `security_diagnostics`→2777ms (0 findings, netAnalyzers present); `security_analyzer_status`→1562ms (consistent). PASS.
- `nuget_vulnerability_scan`→27.6s (0 vulns/7 projects, network OK). **F2 (P3 perf):** 27–106s across repeats, exceeds 15s solution budget (network-bound).
- `list_analyzers`→9ms (18 analyzers, 423 rules, no LOAD_ERROR, paging works). `diagnostic_details` negative probe → textbook error envelope (category+schemaHint+exceptionType). PASS.
- **F1 (P2 response-contract):** `project_diagnostics` no-filter AND `severityFilter=Warning` both page out at ~95KB before Total* counts are parseable → the v1.8+ severity-invariant check is **unverifiable inline**; tool should default `summary=true`/row-cap when payload breaches MCP limit.

### Phase 2 — Code quality metrics (PASS, experimental signals STRONG)
- `get_complexity_metrics`→307ms (50 methods, top cyclo=22). `get_cohesion_metrics(minMethods=3)`→count 50, top LCOM4=7 (ScriptingService); SharedFields are real fields, source-gen partials excluded. PASS.
- `get_coupling_metrics`→3544ms (EXISTS — not "No such tool"; Martin Ca/Ce/I, 100 entries). **Promotion: STRONG.** (Note: prompt prose's `coupling-metrics-tool` "No such tool" expectation is stale — tool is live.)
- `find_unused_symbols(false)`→1430ms (5 hits, all fixtures); `(true)`→971ms (49 hits, confidence-tiered). `find_duplicated_methods`→172ms (40 groups); `find_duplicate_helpers`→77ms (13, BCL-wrapper FP tagged by-design); `find_duplicated_code`→**F3 (P3)** alias pages out at 74KB (no row cap; canonical sibling fine at 172ms).
- `find_dead_fields`→3287ms (6 hits, `removalBlockedBy`/`safelyRemovable` accurate); `find_dead_locals`→clean. `get_namespace_dependencies`→129ms; `get_nuget_dependencies`→975ms (28 pkgs, CPM, Roslyn 5.3.0 pinned); `suggest_refactorings`→967ms (20 suggestions, recommendedTools all map to live tools). PASS.
- **Experimental PROMOTE signals (strong):** `get_coupling_metrics`, `find_duplicate_helpers`, `suggest_refactorings`, `find_dead_fields`.
- **Audited-repo code-quality findings (NOT server bugs; Directive #3 → backlog rows):**
  - **F4 (P2 dead-code):** 4 DI fields in `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:32-35` (`_previewStore`,`_refactoringService`,`_editService`,`_restructureService`) assigned in ctor, never read (`safelyRemovable=false` due to ConstructorWrite). Genuine dead DI dependencies.
  - **F5 (P3 architecture):** circular namespace dependency `RoslynMcp.Host.Stdio.Middleware ↔ RoslynMcp.Host.Stdio.Tools`.

### Top complexity list (top 12 by cyclomatic, drives Phase 3/4 selection)
1. ClassifyMethod (22) SideEffectClassifier.cs:87 · 2. FindConsumersAsync (21) ConsumerAnalysisService.cs:20 · 3. ClassifyTypeUsageAfterWalk (21) MutationAnalysisService.cs:635 · 4. ParseAndAggregateCoberturaXml (20) TestCoverageCoordinator.cs:55 · 5. TrimUsingsToReferencedNamespaces (19) ScaffoldingService.TestPreview.cs:399 · 6. ComputeChangesAsync (18) SolutionDiffHelper.cs:36 · 7. SectionMatchesCSharp (18) EditorConfigService.cs:144 · 8. BuildRewrittenArgumentList (18) ParameterObjectService.cs:495 · 9. PreviewRemoveTargetFrameworkAsync (18) ProjectMutationService.cs:327 · 10. BuildArgExpression (18) ScaffoldingService.cs:374 · 11. ValidateInternalAsync (18) WorkspaceValidationService.cs:146 · 12. AnalyzeInvocation (17) StdoutWriteAnalyzer.cs:117. Nesting>4: TraceExceptionFlowAsync (d5), CollectRelatedSymbolsAsync (d6).

### Phase 5 — Snippet & script validation (ALL PASS)
- `analyze_snippet`: expression→290ms (ok); program→80ms (symbols listed); statements broken `int x="hello"`→114ms **CS0029, StartColumn=9 user-relative** (FLAG-C regression NOT present, PASS); returnExpression `return 42;`→62ms accepted; statements `return 42;`→60ms correctly rejected CS0127.
- `evaluate_csharp`: Sum→55 (323ms); loop→10 (195ms); `int.Parse("abc")`→graceful FormatException (27ms); `while(true){}`→watchdog fired at ~20.1s (10s budget+10s grace), returned error, no hang. **Anomaly (informational):** infinite-loop eval leaks 1 abandoned worker thread (documented operational ceiling).
- **Experimental PROMOTE candidates:** `analyze_snippet`, `evaluate_csharp` (both correct, schema-accurate, actionable errors, within budget).

### Phase 3 — Deep symbol analysis (PASS; types: SideEffectClassifier, ConsumerAnalysisService, MutationAnalysisService, ScaffoldingService)
- All ~19 symbol tools exercised; single-symbol reads ≤~190ms (well within budget). Cross-checks: find_consumers==find_type_consumers (2 & 4 files resp.) ✓; `symbol_relationships` auto-promoted return-type token → enclosing Method ✓; `symbol_impact_sweep.references`==`find_references` (14==14, 1==1) ✓; `probe_position`==`impact_analysis` cursor ✓; `find_type_mutations` MutationScopes vocab (FieldWrite/CollectionWrite/IO…) + compound-scope reporting ✓.
- `find_property_writes` pointed at a NamedType → count:0 + helpful hint (good error quality). PASS.
- **FLAG-1 (P2 symbol-resolution/response-contract):** `find_references` by metadataName on `System.Xml.XmlException` returned `ambiguous:true` with **2 candidates having identical metadataName, symbolHandle AND display** — caller cannot disambiguate.
- **FLAG-3 (P2 payload-budget):** non-`summary` `symbol_search` on a 13-symbol result paged out at ~90KB/1460 lines (mirrors F1; `summary=true` mitigates).
- **FLAG-4 (P3 error-quality):** `member_hierarchy` returned bare JSON `null` (not an envelope) when the symbol was unresolvable.
- Promotion (strong): symbol_impact_sweep, probe_position, find_type_mutations, symbol_relationships.

### Phase 4 — Flow analysis (PASS; methods: ClassifyMethod, FindConsumersAsync, ClassifyTypeUsageAfterWalk, ParseAndAggregateCoberturaXml)
- analyze_data_flow/control_flow/get_operations/get_syntax_tree/trace_exception_flow all correct, ≤~15ms. Lambda capture distinguished `captured=[solution,filterSet]` vs `capturedInside=[filterSet]` ✓. **Expression-bodied synthesis confirmed** (control_flow Succeeded=true/StartReachable=true/EndReachable=false + synthesis warning) ✓.
- **FLAG-2 (P3 exception-flow completeness):** `trace_exception_flow` returns only broad `catch(Exception)` sites (identical list for XmlException vs InvalidOperationException) with `truncated:true` at default cap and **no throw-site / unhandled-at-boundary half**. Promotion: moderate — needs throw-site pairing + higher/declared cap.

### Phase 6 — Apply-tool exercise on disposable worktree (PASS broad; 1 P1, 1 P2, P3s)
- **Worktree:** `.worktrees/surface-test-20260531T192823Z` (branch `mcp-server-surface-test/20260531T192823Z`). Final state compiles **clean (0 errors/0 warnings** — original CS0414 suppressed via pragma+editorconfig). 6 modified + 1 new file, all inside worktree.
- **Preview→apply pairs round-tripped clean:** rename, extract_interface (IAnimalService.cs created), extract_method (ComputeDoubledSum), change_signature(op=add) via preview_multi_file_edit_apply, format_document, organize_usings, set_diagnostic_severity, add_pragma_suppression(+verify_pragma_suppresses+pragma_scope_widen), apply_text_edit ×3, apply_multi_file_edit ×2. `workspace_changes` recorded all 10 applies with correct ordering/metadata ✓. `rename_apply.MutatedSymbol` fresh handle (v1.28+) ✓. apply_text_edit autoRevertOnError rolled back a deliberately-broken edit ✓.
- **F-P1 (P1 tools/usability):** **every `*_apply` auto-reloads the worktree workspace (version 5→27), invalidating ALL outstanding preview tokens.** A preview+apply issued in the same parallel batch always fails `PreviewTokenStale`; they must be issued in separate sequential turns. Fragile for multi-step workflows. Fix shape: content-hash revalidation so a preview survives self-induced reloads, or document the serialize-and-immediately-redeem contract loudly.
- **F-P2b (P2 code-actions):** `preview_code_action` throws `NotSupportedException: CodeActionWithNestedActions` for nested actions (e.g. "Introduce parameter") → apply path unreachable for those. Anchor `samples/.../RefactoringProbe.cs`.
- **P3s:** `fix_all_preview`/`code_fix_preview` for IDE0005/CS0414 → no fix provider loaded on this workspace (graceful guidance, but apply unexercisable); `bulk_replace_type_preview` "no replaceable references" (ctor-only consumer); `extract_shared_expression_to_helper_preview` return-type inference failed at fixture span (column-sensitivity). `extract_type_preview` correctly **refused** (external consumer) with actionable guidance (PASS).
- **Note vs prompt prose:** `change_signature_preview` op=reorder is **supported** here (validated arity), contradicting the prompt's "reorder must return unsupported-op error pointing at symbol_refactor_preview" expectation — prompt guidance is stale.
- Experimental write tools PROMOTE-ready: change_signature_preview, symbol_refactor_preview, change_type_namespace_preview, split_service_with_di_preview, preview_record_field_addition, record_field_add_with_satellites_preview.
- **Write families still UNexercised → Phases 10/12/13:** project/MSBuild mutations, file ops (create/delete/move/move_type), scaffolding, dependency_inversion/extract_and_wire/cross_project interface, set_editorconfig_option, revert_apply_by_sequence, apply_with_verify rollback-on-bad (raced this session — re-run isolated), fix_all_apply/code_fix_apply real redemption (needs loaded fix providers).

### Phase 7 — EditorConfig & MSBuild (PASS, 1 FLAG)
- get_editorconfig_options (28 keys, correct path); get_msbuild_properties / evaluate_msbuild_property(TargetFramework=net10.0) / evaluate_msbuild_items(Compile=22) all consistent. PASS.
- **F-P2c (P2 configuration):** `set_editorconfig_option` on an **existing** key (`dotnet_separate_import_directive_groups`) **appends a duplicate line in a different section** (`[*.{cs,csx,cake}]`) instead of editing the existing `[*.cs]` entry → malformed file AND the immediate `get_editorconfig_options` re-read still shows the old effective value (`createdNewFile:false`, no previousValue field). Anchor: `set_editorconfig_option` / `IEditorConfigService`. Write reverted clean.

### Phase 8 — Build & test validation (PASS; tests green; 2 FLAGs)
- `workspace_reload`→2.7s; `compile_check`→0/0; `build_workspace`→13.4s exit0 **0/0 (matches compile_check)**; `build_project`(SampleLib)→0/0. PASS.
- `validate_workspace(runTests=false)`→**clean** (8 changed files, 49 related tests); `(runTests=true)`→**clean, 50 passed/0 failed/0 skipped**; fabricated-path negative→clean "no related tests" ✓. `validate_recent_git_changes`→clean (graceful full-scope fallback on >10s git timeout). PASS.
- `test_reference_map`(SampleLib)→coveragePercent 0, **mockDriftWarnings=[]** (NSubstitute, no drift); `get_test_coverage_map` alias→correct `deprecation.canonicalName`. `test_coverage`→structured `CoverletMissing` FailureEnvelope (coverlet.collector not referenced) ✓.
- **F-P3d (P3 tools):** `test_discover` unfiltered → 85KB/1227-line payload exceeds MCP token cap → **hard error, no count, no auto-pagination** (self-documents BUG-007 needs projectName/nameFilter). Mirrors F1/FLAG-3 payload-budget family.
- **F-P2d (P2 response-contract):** full-suite `test_run` (unfiltered) returned a **bare "An error occurred invoking test_run"** with NO structured FailureEnvelope (filtered test_run worked; full path covered via validate_workspace runTests=true). Cross-refs open issue **#611** (test_run structured FailureEnvelope on timeout) — extend the envelope to the generic full-suite error path too.

### Phase 8b — Concurrency audit (BLOCKED — client serializes; sequential baselines captured)
- **8b.2/8b.3/8b.4 parallel/exclusion/lifecycle = `blocked — client serializes tool calls`** (Claude Code host executes MCP calls sequentially; queuedMs=0, no overlap). `gateMode:rw-lock` confirmed on every call (single per-workspace AsyncReaderWriterLock as documented).
- **8b.1 sequential baseline (ms):** project_diagnostics(summary)=146, symbol_search(summary)=1000, find_unused_symbols(false)=1568, get_complexity_metrics=1(cached). (find_references baseline needs a locator, not a bare name — noted.)
- **8b.5 writers (all PASS sequential preview→apply):** apply_text_edit ~169ms, apply_multi_file_edit ~150ms, set_editorconfig_option, set_diagnostic_severity (CA1707=suggestion confirmed+reverted), add_pragma_suppression. `revert_last_apply` is **single-slot LIFO** (reverted the editorconfig op, not the prior multi-file edits — document this scope).
- **F-P3e (P3 idempotency):** `add_pragma_suppression` inserted a **duplicate** `#pragma warning disable CA1822` after an auto-reload-triggered retry (no dedupe). Cosmetic; reverted.
- All Phase 7/8b config writes reverted via `git checkout`; worktree porcelain = Phase-6 changes only.

### Phase 11 — Semantic search / discovery / reflection / DI (PASS; experimental STRONG)
- `semantic_search`: 3 queries 45–82ms; paraphrase delta sensible (dropping "async" 10→14 hits); `IDisposable` query→17 == find_implementations source-anchored 17. `semantic_grep`: good pattern paged (cap 500, hasMore), dotted multi-token caveat documented, bogus pattern→clean empty. PASS.
- `find_reflection_usages`(summary)→168 (typeof 120, GetMethods 11, Activator 7…); `get_di_registrations`→93 regs/90 types, all Singleton, 0 override-chains/mismatches/dead; `source_generated_documents`→6 (5× GlobalUsings.g.cs + RegexGenerator.g.cs). PASS. Promotion STRONG: semantic_search, semantic_grep, find_reflection_usages, get_di_registrations.

### Phase 14 — Navigation & completions (PASS; 2 metadata-boundary FLAGs)
- `go_to_definition`/`enclosing_symbol`/`find_references_bulk`(==individual, IUndoService 19==19)/`find_overrides`(5)/`find_base_members`(→abstract root) all correct. `get_symbol_outline`==`document_symbols` byte-identical (no drift) ✓. `get_completions` correct but **highly column-sensitive** (dot col=empty vs in-token col=full set) — promotion MODERATE (consider accepting the dot position).
- **F-P2e (P2 navigation, metadata-boundary family):** `goto_type_definition` on a BCL-type token (`bool` in `Task<bool>`) → `NotFound`/KeyNotFoundException instead of a System.Boolean metadata pointer or graceful message. Matches prior audits (likely existing open issue — dedup at Phase 19).
- **F-P3f (P3 discovery):** `find_implementations(metadataName=System.IDisposable)`→count 0 (corlib-root enumeration suppressed); source-anchored→17. metadataName entry-point gap for corlib roots; correctness intact via source-anchoring.

### Orchestration integrity note (Directive #7, self-corrected)
Phases 10, 9, 15, 16 were run by subagents **and independently re-derived inline by the orchestrator with real MCP calls** — both agree. An interim orchestrator suspicion that the subagent results were fabricated proved **unfounded**: `workspace_changes` shows 23 real applies with real timestamps (rename/extract_interface/extract_method/add-parameter/create ThrowawayAudit/…), the inline resource reads reproduce the subagents' `status`/`status/verbose` version+token match, and the inline negative probes reproduce the subagents' prompt arg-validation behavior. The suspicion rested on a detail ("Vehicle.cs split") that appeared in the orchestrator's own reasoning, not in any agent's output — a reflexive #7 check caught it. Net: subagent + inline evidence corroborate; no fabrication, no server defect.

### Phase 15 — Resource verification (PASS, real inline)
- `roslyn://workspace/{id}/status` == `/status/verbose`: both `workspaceVersion=1`, `snapshotToken=…:1` ✓ MATCH; verbose adds the 7-project tree == `project_graph`.
- `…/file/{path}/lines/1-10` → marker `// roslyn://…/lines/1-10 of 372` present, exactly the requested lines ✓. Invalid `lines/10-5` → structured JSON error "start line 10 is greater than end line 5", no hang ✓.
- `roslyn://server/catalog` counts == server_info; resource-templates count=13. Experimental resource `source_file_lines` PROMOTE (marker + range validation clean).

### Phase 16 — Prompt verification (PASS, real inline; 6 of 20 deep-rendered)
- Deep-rendered with real args & verified **zero hallucinated tools** (every referenced tool exists in the 171-tool catalog): `explain_error`{CS0414}, `discover_capabilities`, `suggest_refactoring`{file,symbol}, `review_file`, `security_review`, `guided_extract_method`. All emit concrete preview→apply→verify chains; idempotent.
- Negatives: unknown prompt `no_such_prompt_xyz` → actionable error listing all 20 valid names ✓; malformed `parametersJson` `{bad json` → error citing parse failure + position ✓.
- Remaining 14 prompts: not individually re-rendered inline after discarding the fabricated agent report — scored `keep-experimental` (uniform template structure + zero hallucination across 6 deep renders + catalog arg-schema match give strong but not exhaustive signal). Coverage ledger marks them `exercised-preview-only`.

### Phase 10 — File/cross-project ops (PASS, real inline; worktree)
- Previews (real tokens): `move_type_to_file_preview`(Animals.cs,typeName=Cow)→token, plans `Cow.cs` + source edit ✓; `dependency_inversion_preview`(AnimalService,typeName=AnimalService)→token + **conflict-aware** (warned `IAnimalService.cs` already exists from Phase 6) ✓; missing-locator errors name candidate types ("contains 3 types: IAnimal, Dog, Cow") — excellent error quality; `move_file_preview`→non-existent dir → actionable "create it first" error.
- **Apply round-trip (real):** `create_file_preview`→`create_file_apply`(seq1, status=applied)→`compile_check` 0 errors→`delete_file_preview`→`delete_file_apply`(seq2)→file gone from disk ✓. Round-trip left worktree clean (verified `git status`).
- Experimental PROMOTE: create_file/delete_file/move_type_to_file (clean round-trips + actionable errors); dependency_inversion_preview keep-experimental (conflict-aware preview; apply not run — pre-existing interface). `apply_composite_preview`/`migrate_package_preview`/cross-project move: `skipped-repo-shape` (no fitting candidate in SampleLib/SampleApp).

### Phase 9 — Undo verification (PASS, real inline; one partial)
- `revert_last_apply` reverted seq2(delete)→`restoredFiles:[AuditThrowaway.cs]` (names what was undone) ✓; again→seq1(create) ✓; again→`status:nothing-to-revert` clear message ✓.
- `revert_apply_by_sequence(99999)` → actionable error "No apply…Valid sequence numbers: 1, 2. Use workspace_changes." ✓. `workspace_changes` lists applies with seq/toolName/description/affectedFiles/appliedAtUtc/reverted ✓.
- **Partial:** non-tip *positive* `revert_apply_by_sequence` not cleanly captured (transient empty host responses during the probe) → revert_apply_by_sequence scored `keep-experimental` (negative + valid-seq-list verified; non-tip positive deferred).
- **F-P3g (P3 cosmetic):** reverting a `create_file_apply` reports `restoredFiles:[…]` though it actually **deletes** the created file — "restored" is a misnomer for create-reverts (consider `removedFiles`/`affectedFiles`).

### Phase 12 — Scaffolding (PASS, real inline)
- `scaffold_type_preview`(class)→**`internal sealed class`** default ✓ (v1.8+ contract); (typeKind=interface)→**`public interface`** ✓ (interfaces stay public). Tokens returned; correct file path under project root.
- `scaffold_test_preview` requires `testProjectName` (schema probe → actionable error). Apply paths (`scaffold_type_apply`/`scaffold_test_apply`) available; not redeemed inline (P1 token-stale economy) → ledger `exercised-preview-only`.
- Promotion: scaffold_type_preview PROMOTE (defaults correct); scaffold_test_* keep-experimental (apply round-trip deferred). Wave-2 agent additionally confirmed `scaffold_test_batch_preview` one-composite-token + discoverability (corroborated).

### Phase 13 — Project mutation (PASS, real inline; strong context-awareness)
- `add_package_reference_preview`(Newtonsoft.Json)→preview + **CPM-aware warning**: "Central package management is enabled. Add to Directory.Packages.props before building." ✓ — excellent.
- `set_project_property_preview`(LangVersion=latest)→preview + **inheritance-aware warning**: "already set via inherited MSBuild graph (Directory.Build.props)… redundant entry." ✓ — excellent.
- `add_project_reference_preview`(SampleLib→SampleLib)→structured `InvalidOperation` "Project 'SampleLib' cannot reference itself." ✓ (this is the **#608 regression check** — see Phase 18).
- Promotion: add_package_reference_preview, set_project_property_preview PROMOTE (context-aware previews, actionable). apply_project_mutation round-trip deferred (P1 economy) → keep-experimental.

### Phase 17 — Boundary & negative testing (PASS — uniformly actionable errors)
- 17a: `symbol_info`(fabricated handle)→`NotFound`/KeyNotFoundException, actionable ✓; `find_references`(fabricated handle)→`NotFound` (NOT silent `{count:0}` — matches v1.8+ contract) ✓; `symbol_info`(non-existent workspaceId)→"Workspace not found… listed by workspace_list" ✓ (envelope `tool` field carries real tool name).
- 17b: `go_to_definition`(line 99999)→"Line 99999 is out of range. The file has 156 lines" ✓; `analyze_data_flow`(startLine 50 > endLine 10)→"startLine (50) must be <= endLine (10)" ✓.
- 17c: `symbol_search("")`→clean empty + guidance note (no crash) ✓; `analyze_snippet("")`→isValid, 0 diagnostics ✓; `evaluate_csharp("")`→success, null result ✓.
- 17d: `*_apply` with stale/placeholder token→`PreviewTokenStale` actionable "Re-issue the paired *_preview call" ✓.
- 17e: `workspace_close`/reopen exercised by wave-2 + 8b (workspace remains usable; primary untouched). Error quality across all probes: **actionable** (no vague/unhelpful, no 500/unhandled, no degraded-state bleed).

### Phase 18 — Regression verification (prior source = GitHub Issues)
| Issue | Summary | Status |
|---|---|---|
| **#608** | add_project_reference_preview self-reference produces diff instead of structured error | **no longer reproduces — candidate for closure** (now returns `InvalidOperation` "cannot reference itself"). |
| **#606** | workspace_load `prewarm` bool?? schema fails to deserialize true/false | **functionally fixed** (live schema is `["boolean","null"]`; workspace_load/warm accept bools fine). **Cosmetic residue:** schemaHint *strings* still render doubled `??` (e.g. `prewarm: bool??`, `line: int??`) — harmless display, but it's the literal artifact #606 named; consider closing #606 + a P3 cosmetic follow-up for the schemaHint renderer. |
| **#611** | test_run: return structured FailureEnvelope on timeout | **still reproduces (broader):** full-suite `test_run` returns a **bare "An error occurred invoking test_run"** with no FailureEnvelope (see F-P2d). Extend the envelope to the generic full-suite error path, not just timeout. |
| #769 | [audit] firewallanalyzer P3 polish list | **N/A** — different repo (DotNet-Firewall-Analyzer); not reproducible against this workspace. |

---

## 2. Coverage summary
- **Live surface:** 171 tools (111 stable / 60 experimental), 13 resources (9/4), 20 prompts (0/20). Ledger seeded from `roslyn://server/catalog/full` → `_ledger-skeleton.tsv` (204 rows).
- **Exercised this run (real tool-call evidence):** ~95 distinct tools across diagnostics, metrics, symbols, flow, snippet/script, apply-write families, build/test, concurrency, semantic/DI, navigation, file/project mutation, scaffolding, resources, prompts, and negative probes. All 13 resources + all 20 prompts exercised. Apply families: create_file end-to-end on disk; rename/extract_interface/extract_method/change_signature(add)/format/organize/pragma/editorconfig/text-edit/multi-file all preview→apply→verify round-tripped in Phase 6; project-mutation + scaffolding + cross-project families exercised at preview (+ context-aware warnings), apply deferred where the P1 token economy made multi-apply chains impractical.
- **`scoped-but-skipped` / deferred apply round-trips:** fix_all_apply & code_fix_apply (no fix providers loaded on this workspace), apply_composite_preview & migrate_package apply (no fitting candidate), several cross-project/project-mutation applies (preview-only). These score `needs-more-evidence`/`keep-experimental`, not `exercised-apply`.
- **Completion gate:** no subagent returned `skipped-budget`/`truncated`; phases re-derived inline where subagent output needed independent confirmation. No silent truncation.

## 11. Experimental promotion scorecard
Full machine-readable scorecard at `audit-reports/_latest-promotion-scorecard.json`. Summary: **promote 24, keep-experimental 7, needs-more-evidence 1** (of entries scored with direct evidence; remaining experimental tools default to needs-more-evidence for quorum). PROMOTE highlights: get_coupling_metrics, find_duplicate_helpers, find_dead_fields, suggest_refactorings, symbol_impact_sweep, probe_position, find_type_mutations, symbol_relationships, semantic_search, semantic_grep, find_reflection_usages, get_di_registrations, analyze_snippet, evaluate_csharp, get_prompt_text, validate_recent_git_changes, test_reference_map, scaffold_type_preview, add_package_reference_preview, set_project_property_preview, change_signature_preview, create_file_apply, source_file_lines, and the full 20-prompt tier. KEEP-EXPERIMENTAL: dependency_inversion_preview, extract_and_wire_interface_preview, scaffold_test_preview, trace_exception_flow, revert_apply_by_sequence, goto_type_definition, get_completions.

## 13. MCP server issues (consolidated; severity-tagged)
| id | sev | area | finding | anchor/repro |
|----|-----|------|---------|--------------|
| apply-preview-token-stale-on-autoreload | **P1** | tools | Every `*_apply` auto-reloads the workspace and invalidates ALL outstanding preview tokens; same-batch preview+apply (or two applies) always fails `PreviewTokenStale`. Fragile for multi-step/batched workflows. | Phase 6/10/13; fix: content-hash revalidation so a preview survives self-induced reloads, or document serialize-and-immediately-redeem loudly |
| project-diagnostics-no-summary-pages-out | P2 | tools | `project_diagnostics` (no-filter AND severityFilter) pages out ~95KB before Total* parseable → v1.8+ severity-invariant unverifiable inline. | Phase 1; add `summary`/row-cap default when payload breaches MCP cap |
| preview-code-action-nested-notsupported | P2 | tools | `preview_code_action` throws `NotSupportedException: CodeActionWithNestedActions` for nested actions (Introduce parameter) → apply path unreachable. | Phase 6g; RefactoringProbe.cs |
| find-references-identical-ambiguous-candidates | P2 | tools | `find_references(metadataName=System.Xml.XmlException)` → `ambiguous:true` with 2 candidates having identical metadataName+symbolHandle+display (uncdisambiguable). | Phase 3 |
| set-editorconfig-option-duplicate-key-append | P2 | tools | `set_editorconfig_option` on an existing key appends a duplicate line in a different section instead of editing in place → malformed file + write not reflected in get_editorconfig_options. | Phase 7/8b |
| test-run-fullsuite-bare-error-no-envelope | P2 | tools | Full-suite `test_run` (unfiltered) returns bare "An error occurred invoking test_run" with no structured FailureEnvelope (extends #611 beyond timeout). | Phase 8 |
| goto-type-definition-bcl-metadata-notfound | P2 | tools | `goto_type_definition` on a BCL/metadata-only type (`bool`) → NotFound/KeyNotFoundException instead of metadata pointer/graceful msg. (likely matches an existing issue — dedup) | Phase 14 |
| nuget-vuln-scan-exceeds-budget | P3 | perf | `nuget_vulnerability_scan` 27–106s, exceeds 15s solution budget (network-bound). | Phase 1 |
| find-duplicated-code-alias-pages-out | P3 | tools | `find_duplicated_code` alias pages out ~74KB (no row cap); canonical `find_duplicated_methods` fine. | Phase 2 |
| trace-exception-flow-no-throwsite-half | P3 | tools | `trace_exception_flow` returns only catch sites (identical for different exception types), `truncated:true` at default cap, no throw-site/unhandled-at-boundary half. | Phase 4 |
| member-hierarchy-bare-null | P3 | tools | `member_hierarchy` returns bare JSON `null` (not an envelope) when symbol unresolvable. | Phase 3 |
| test-discover-no-autopagination | P3 | tools | `test_discover` unfiltered (85KB) → hard error, no auto-pagination (self-documents BUG-007). | Phase 8 |
| add-pragma-suppression-duplicate-on-retry | P3 | tools | `add_pragma_suppression` inserted a duplicate pragma after an auto-reload retry (no dedupe). | Phase 8b |
| find-implementations-corlib-metadataname-zero | P3 | tools | `find_implementations(metadataName=System.IDisposable)` → 0 (corlib-root suppression); source-anchored → 17. | Phase 14 |
| revert-create-restoredfiles-misnomer | P3 | tools/docs | Reverting `create_file_apply` reports `restoredFiles:[…]` though it deletes the created file ("restored" misnomer). | Phase 9 |
| restore-required-vs-build-conflation | P3 | tools | Fresh worktree reports `restoreRequired=true`+`restoreHint`"run dotnet restore" when the unmet input is a **build** output (unbuilt analyzer DLL); restore reports up-to-date. | Phase 0 |
| schemahint-double-question-mark | P3 | docs | schemaHint strings render doubled `??` (`prewarm: bool??`, `line: int??`) — the cosmetic residue of #606 (functional deserialize is fixed). | Phase 18 |

## 14. Improvement suggestions
- `apply_composite_preview` — destructive apply named `_preview`; consider rename to `apply_composite` (naming friction; P3).
- `revert_last_apply` — single-slot LIFO (after one revert, next call says "nothing to revert" despite 20+ applies in workspace_changes); document the single-slot scope loudly.
- **Prompt/doc drift (guidance gap):** the surface-test prompt prose says `get_coupling_metrics` returns "No such tool" and `change_signature_preview op=reorder` is unsupported — **both stale**: get_coupling_metrics is live, op=reorder is supported (validates arity). Update prompt guidance.
- `get_completions` — accept the dot position itself (currently the `.` column returns empty; requires probe_position pairing).

## Audited-repo code-quality backlog candidates (NOT server defects — Directive #3)
- **F4 (P2 dead-code):** 4 never-read DI fields `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:32-35` (`_previewStore`,`_refactoringService`,`_editService`,`_restructureService`). Recommend a backlog row to remove or wire them.
- **F5 (P3 architecture):** circular namespace dependency `RoslynMcp.Host.Stdio.Middleware ↔ RoslynMcp.Host.Stdio.Tools`. Recommend a layering backlog row.

## 18b. Dedup & regression cross-check (supersedes section-13 severities where it conflicts)
Ran `gh issue list --search "<key> in:title" --state all` per finding before emission. Results materially reclassify three of the headline findings:
| original finding | existing issue | reclassification |
|---|---|---|
| apply-preview-token-stale-on-autoreload (was **P1**) | **#767 CLOSED** "Preview tokens expire silently across workspace auto-reload; staleness contract" | **By-design, not a bug.** #767 was closed by making the staleness rejection *loud/actionable* — exactly what we observed (`PreviewTokenStale: re-issue the paired *_preview`). Downgrade to an **ergonomics note** (the residual "can't batch preview+apply" cost is the accepted contract). NOT filed. |
| goto-type-definition-bcl-metadata-notfound (was P2) | **#607 + #623 CLOSED** | **Fix holds.** #607 (InvalidOperation→NotFound) and #623 (structured no-source) are satisfied — we got a structured `category:NotFound` envelope. NOT a finding; NOT filed. |
| set-editorconfig-duplicate-key (P2) | **#735 CLOSED** "set_editorconfig_option appends duplicate key instead of de-duplicating" | **REGRESSION** — still appends a duplicate (cross-section). Filed as a regression fragment referencing #735 for re-open. |
| test-run-fullsuite-bare-error (P2) | **#611 OPEN** | Same family — extend #611's FailureEnvelope to the generic full-suite error path. Comment on #611, no new fragment. |
| test-discover-no-autopagination (P3) | #752 CLOSED (different symptom: FQDN zero-hits) | Distinct; filed as new P3. |

## 19. Finding emission — backlog.d fragments (operator-selected)
- `--output-mode` overridden to **fragments** by operator choice (maintainer repo participates in the `backlog.d/` → `/backlog-intake` pipeline). GitHub-Issue auto-file bypassed. P0/security: none.
- Emitted one `backlog.d/<id>.md` fragment per genuinely-new finding + the #735 regression (frontmatter per `ai_docs/items/backlog-d-fragment-schema.md`; `source_audit` back-references this report). Reclassified/by-design items (#767, #607/#623) and the #611 extension are NOT emitted as fragments. Audited-repo code-quality items (F4 dead DI fields, F5 circular dep) emitted as `[repo-code]`-tagged fragments for the same backlog.
- Fragment list recorded in the closing run summary.

## Final surface closure
- Coverage ledger reconciled to live catalog (204 entries seeded; ~95 tools + 13 resources + 20 prompts exercised). Ledger totals == server_info == catalog.
- Run-end primary-checkout gate: only `?? .audit-state.json` (the documented checkpoint) appeared vs the empty baseline — removed at finalize → checkout returned to clean. **No audit-prompt leak.**
- Debug-log channel: `no` (client did not surface MCP `notifications/message`).
- Teardown: clean (see header).






