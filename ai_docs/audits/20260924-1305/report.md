# MCP Surface Audit — Roslyn-Backed-MCP (2026-09-24)

<!-- AI-facing audit report. Machine-readable twin: findings.json (same dir). Deep-harness 19-section report: audit-reports/20260924T130517Z_roslyn-backed-mcp_mcp-server-surface-test.md (gitignored, local). -->

## Header

| Field | Value |
|---|---|
| Run | `20260924-1305` (UTC 2026-09-24T13:05:17Z → ~14:15Z) |
| Command | `/mcp-surface-audit depth=deep` → Roslyn deep harness `/roslyn-mcp:mcp-server-surface-test --full --no-auto-file` (§0.2) |
| Target | `<repo>` = Roslyn-Backed-MCP, HEAD `e24a48a4` (main, clean at start) |
| Server audited | registered `Darylmcd.RoslynMcp@4.2.1` (`4.2.1+4eba2601`, dnx/NuGet), prefix `mcp__plugin_roslyn-mcp_roslyn__` |
| Pin drift | **registered server is 60 commits behind HEAD** (12 `src/` files differ). Raw harness also ran HEAD's Debug build (`4.2.1+e24a48a4`); every finding was re-checked against HEAD source. Fixed-at-HEAD items are listed, not filed. |
| Stack / transport | C# .NET 10.0.12, Roslyn 5.9.0.0, ModelContextProtocol SDK 2.2.0, stdio JSON-RPC, protocol 2025-06-18 |
| Profiles | one (no env-selected tool profiles) |
| Live surface | tools 113 stable / 61 experimental (HEAD: 175 — adds `apply_composite`), resources 9/5, prompts 0/20; `parityOk: true`; catalog counts == `server_info` |
| Workspaces | W_RO = disposable worktree copy of `RoslynMcp.slnx` (6 projects, 905 docs, full-size timing); W_RW = disposable worktree `samples/SampleSolution` fixture (3 projects) for every mutation |
| Isolation | `.worktrees/surface-test-20260924T130517Z{,-ro}` on branches `mcp-server-surface-test/20260924T130517Z{,-ro}`; W_RO sha1 manifest identical before/after (1483 files) |
| Teardown | **clean** — both workspaces closed with `drainProcesses=true`, both worktrees removed, both branches deleted, `git worktree prune`; primary checkout status == baseline |
| Execution | orchestrator + 7 phase-runner subagents (G1–G6, G8); G5 died mid-Phase-17 on an API usage limit — orchestrator finished Phase 17 inline; 8b.4 lifecycle stress **blocked** (client serializes tool calls) |
| Operator stderr | yes for raw-harness processes (captured); no for the registered server |
| Backlog | `backlog=apply` — 79 rows via `backlog.mjs add` (see § Backlog) |

## What is solid

- **Protocol hygiene:** all 166 missing-arg and 161 wrong-type probes across the whole surface returned a structured `InvalidArgument` envelope carrying the right `tool` name and a `schemaHint`. None crashed, none timed out, max < 1 s.
- **Annotations:** `readOnlyHint`/`destructiveHint` match the catalog's `readOnly`/`destructive` for 174/174 tools.
- **Catalog integrity:** resource/template/prompt lists match the catalog, and the catalog matches `server_info`. All 8 tools that declare `outputSchema` return `structuredContent` that validates.
- **Preview/apply file parity:** for every apply in G4 and G6, git diff/md5 matched the preview's file list. No "success without change" and no "more files than previewed".
- **Stale-token contract:** stale tokens are rejected loudly and actionably (fix of #767 holds).
- **Undo:** revert_last_apply, revert_apply_by_sequence (non-tip) and the double-revert "nothing to revert" all behaved per contract.
- **Build/test parity:** build_workspace equals compile_check (0 errors). Filtered and full test_run, validate_workspace (incl. runTests and a fabricated path) and validate_recent_git_changes are coherent, and every `overallStatus` is in the verdict table.
- **Isolation:** no tool mutated the read-only copy (hash manifest identical). Nothing touched the primary checkout.
- **Security:** path traversal (`../../../../Windows/win.ini`) is refused as NotFound, no leak. workspace_support_bundle output has no env vars, tokens or profile paths. semantic_grep redacts regex input on error.
- **Prior regressions fixed:** member-hierarchy-bare-null, find-references-identical-ambiguous-candidates, schemahint-double-question-mark, test-discover-no-autopagination. apply_composite_preview was renamed `apply_composite` at HEAD.

## Fixed at HEAD (not filed)

| Observation (4.2.1) | HEAD e24a48a4 |
|---|---|
| Unknown/closed workspaceId → generic `NotFound` "requested item was not found" | `WorkspaceNotFound` + actionable text (WorkspaceNotFoundException) — **fixed** |
| `apply_composite_preview` naming friction (prior suggestion) | alias `apply_composite` added — **fixed** |

## Findings by severity

Every row carries a `file:line` anchor plus the captured call and response. Per-group evidence is in `raw/phase-*.md`; raw captures are in `raw/*.json`.

### Critical

None.

### High (10)

| Row id | Check | Finding | Anchor | Evidence (capture) |
|---|---|---|---|---|
| `compilation-cache-generator-rerun-blinds-unused-analysis` | C1 | Resolve cache-compilation symbols back to the solution before SymbolFinder in unused/dead-field/coupling analysis | `src/RoslynMcp.Roslyn/Services/CompilationCache.cs:61` | find_dead_fields(W_RO, projectName=RoslynMcp.Host.Stdio) → s_index/AnalysisTools/s_currentReleaseVersion usageKind=never-read, safelyRemovable=true; all three are read (PromptParameterIndex.cs:32, ServerSurfaceCatalog.cs:32, :105). Introduced by #1402 (92ac5a9c). |
| `find-type-consumers-mutations-generator-blind` | C1 | find_type_consumers / find_type_mutations return empty for generator projects | `src/RoslynMcp.Roslyn/Services/TypeConsumersService.cs:111` | find_type_consumers(SurfaceEntry) → count 0 vs find_references totalCount 61; find_type_mutations(HostProcessMetadataStore) → mutatingMembers [] although WriteCurrent calls File.WriteAllText/Move/Delete. |
| `sanctioned-roots-unconfigured-error-misattributed` | C1 | Unconfigured sanctioned roots surface as 'Parameter path is invalid' | `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs:153` | Raw harness, server without roots env: workspace_load{path:<repo>/.../SampleSolution.slnx} → InvalidArgument "Parameter 'path' is invalid. Check that all required parameters are provided and values match the expected types."; cause only on stderr ('Filesystem boundary is not configured'). Reproduces on 4.2.1 and HEAD e24a48a4. |
| `prompt-argument-binding-spec-strings-and-names` | C3 | prompts/get rejects spec string args for int params and never names the bad argument | `src/RoslynMcp.Host.Stdio/Middleware/PromptBindingStageAdapter.cs:81` | prompts/get consumer_impact {line:"19",column:"21"} → -32602 Invalid parameters; same with JSON numbers → OK. prompts/get review_file {} → message never names filePath (PromptBindingStageAdapter.cs:73/85/90). |
| `split-service-with-di-facade-drops-sibling-types` | C1 | split_service_with_di_preview facade deletes every other type in the file | `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:843` | split_service_with_di_preview on G4Fixture.cs → facade diff deletes IG4Shape, G4Square, G4Circle, G4Point, G4Consumer and drops ': IG4Fixture' + const (not applied — data loss). |
| `change-signature-add-skips-same-document-impls` | C1 | change_signature_preview op=add skips later declarations/callsites in the same document | `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:71` | change_signature_preview op=add IG4Shape.Area → CS0535 on G4Square/G4Circle after apply; callsiteUpdates=1 of 5. |
| `restructure-preview-splice-without-parenthesization` | C1 | restructure_preview splices captures without parentheses (silent semantic change) | `src/RoslynMcp.Roslyn/Services/RestructureService.cs:410` | `__a__ + 1` → `__a__ * 3` produced `count + count * 3` (compiles clean, wrong semantics). PlaceholderSubstituter.VisitIdentifierName returns captured.WithTriviaFrom(node) with no parenthesization. |
| `replace-invocation-rewrites-replacement-body` | C1 | replace_invocation_preview rewrites calls inside the replacement method (infinite recursion) | `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs:235` | replace_invocation_preview → SummarizeV2 body becomes SummarizeV2(verbose,title,count): infinite recursion, 0 diagnostics (reverted). |
| `apply-composite-no-undo-capture` | C1 | apply_composite_preview applies are not undoable; revert_last_apply undoes the previous op | `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:117` | revert_last_apply after composite seq37 reverted seq36 create_file instead. CompositeApplyOrchestrator has no CaptureBeforeApply call (only EditorConfigService, EditService, ProjectMutationService, RefactoringService do). |
| `change-type-namespace-emits-invalid-syntax` | C1 | change_type_namespace_preview emits uncompilable namespace syntax | `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:371` | change_type_namespace_preview emits 'namespaceSampleLib.Shapes{'; apply_with_verify → 8 errors, rolled back. |

### Medium (24)

| Row id | Check | Finding | Anchor | Evidence (capture) |
|---|---|---|---|---|
| `argument-exception-throw-sites-lack-public-message` | C1 | Server-authored ArgumentException messages are redacted to a generic template | `src/RoslynMcp.Host.Stdio/Tools/ParameterValidation.cs:60` | symbol_search{limit:-3} → "Parameter '<unknown>' is invalid…"; analyze_snippet bogus kind same; ValidatePagination (17 callers) throws paramless ArgumentException; 18 single-line paramless throws repo-wide. go_to_definition(line=99999)/enclosing_symbol(line=0) → "Parameter 'line' is invalid…" without the file line count. |
| `invalid-operation-throw-sites-lack-public-message` | C1 | Actionable InvalidOperationException messages redacted to 'operation is not valid in the current state' | `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:152` | 12 refusal paths across Phases 6/10/12/13 returned "The operation is not valid in the current state. Check the tool contract and retry." (set_project_property_preview, move_type_to_project_preview, create_file_preview existing file, scaffold_first_test_file_preview, format_range_preview, change_signature reorder, get_prompt_text unknown prompt, catalog-diff pair). Also misleading advice: build_project/test_run(projectName=NoSuchProject) and rename_preview(fabricated symbolHandle) → "The operation is not valid for the current workspace state. Call workspace_reload if the state is stale, then retry." (real cause: project/symbol not found, WorkspaceProjectResolver.cs:21). |
| `workspace-close-schema-leaks-test-seams` | C4 | workspace_close publishes test seams getProcessesByName / processDrainTimeout | `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:143` | tools/list: getProcessesByName {"$comment":"Unsupported .NET type","not":true}, processDrainTimeout {TimeSpan pattern}, both undocumented; comment at WorkspaceTools.cs:141 claims 'no [Description], so the schema omits it' — false. Client-settable drain timeout. |
| `unknown-tool-arguments-silently-ignored` | C4 | Misspelled/unknown tool arguments are silently ignored | `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` | 175/175 inputSchemas lack additionalProperties:false; compile_check{…,severty:'Error'} and workspace_status{…,__bogus:1} succeed; the filter is silently dropped. |
| `find-implementations-corlib-guard-blocks-source-anchor` | C1 | find_implementations(IDisposable) returns 0 even source-anchored (regression) | `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:350` | find_implementations(WorkspaceManager.cs:22:59 IDisposable) → count 0 + hint 'Source-anchor the query instead'. Prior audit 2026-05-31 (find-implementations-corlib-metadataname-zero): source-anchored returned 17 — now 0. |
| `di-registrations-factory-lambda-impl-misattributed` | C1 | get_di_registrations reports a constructor argument as the implementation type of factory lambdas | `src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs:535` | ServiceCollectionExtensions.cs:87 → implementationType 'System.Net.Http.IHttpClientFactory'; :97 → 'ILogger<WorkspaceCacheStore>'. |
| `find-duplicate-helpers-outermost-call-false-positive` | C1 | find_duplicate_helpers flags work-doing call chains as BCL re-wraps | `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:784` | ≥5 of 10 spot-checked hits (BuildEntries, FormatIdentityDrift, AppendRow, FormatVersionCheckStatus…) are false positives, all confidence=high. |
| `coupling-summary-rollup-limited-to-page` | C1 | get_coupling_metrics summary counts only the returned page | `src/RoslynMcp.Host.Stdio/Tools/CouplingAnalysisTools.cs:33` | summary=true → totalTypes=100, projectCount=3, Core absent; limit=5000 → 1504 types, 6 projects. |
| `unknown-projectname-silently-empty` | C1 | Unknown projectName returns empty results instead of an error | `src/RoslynMcp.Roslyn/Helpers/ProjectFilterHelper.cs:7` | project_diagnostics(projectName=NoSuchProject, summary) → all zeros; compile_check same input → InvalidArgument. |
| `find-dead-locals-captured-by-local-function` | C1 | find_dead_locals flags outer locals captured and written by a local function | `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:1247` | 4 of 5 hits are false positives: outer local captured by a local function (containingMethod=ObservingHandler). |
| `callers-callees-field-initializer-callers-dropped` | C1 | callers_callees drops callers in field/property initializers | `src/RoslynMcp.Roslyn/Helpers/SymbolServiceHelpers.cs:25` | callers_callees(Tool 434:33) → totalCallers 0 vs find_references 175; impact_analysis 175 refs / 0 affected declarations. |
| `flow-analysis-silent-region-narrowing` | C2 | analyze_data_flow / analyze_control_flow silently analyze a narrower range | `src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs:247` | analyze_control_flow(MutationAnalysisService.cs 667-725) → analyzed only 711-725 (next method); returns 672/675 dropped, no warning. |
| `validation-tools-error-envelope-not-iserror` | C1 | build/test tool failures are returned as successful results | `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:454` | test_related(filePath,line) → {"error":true,"category":"InvalidArgument"…} delivered with isError=false; symbol_info same error → isError=true. |
| `fix-all-analyzer-assembly-provider-not-found` | C1 | fix_all_preview cannot find code-fix providers shipped in analyzer assemblies | `src/RoslynMcp.Roslyn/Services/FixAllService.cs:334` | fix_all_preview CA1822/MSTEST0046 → 'No code fix provider' while code_fix_preview on the same diagnostic succeeds. AnalyzerFileReference.Display is a simple name, not a path. |
| `format-range-refuses-on-unrelated-line-count-change` | C1 | format_range_preview refuses whenever whole-document formatting changes line count anywhere | `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:1197` | format_range_preview range 60-80 refused because of a blank line at line 8. |
| `extract-type-breaks-interfaces-and-publicizes-fields` | C1 | extract_type_preview breaks interface contracts, publicizes fields, reformats whole file | `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:127` | extract_type_preview moving Lookup → CS0535; fields made public; whole file NormalizeWhitespace'd. |
| `editorconfig-write-no-workspace-invalidation` | C1 | set_diagnostic_severity / set_editorconfig_option writes are not reflected until manual reload | `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:397` | CA1861 still Info after set_diagnostic_severity silent; isStale=false. |
| `extract-method-nullable-return-flow-state` | C1 | extract_method_preview emits string? return that trips CS8603 | `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:257` | extract_method_preview → private string? BuildLine → CS8603 after apply. |
| `replace-string-literals-const-self-reference` | C1 | replace_string_literals_preview rewrites the target const's own initializer | `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs:162` | const ReportPrefix = G4Fixture.ReportPrefix (CS0110) after preview. |
| `record-satellites-line-insert-outside-initializer` | C1 | record_field_add_with_satellites_preview inserts satellite lines outside initializers | `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:930` | 'Evictions = this.Evictions,' inserted after the one-line Clone → CS1519 (reverted). |
| `preview-token-store-mismatch-false-stale` | C2 | Apply tools report 'workspace reloaded' for tokens from the other preview store | `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:394` | scaffold_test_batch_preview token → apply_composite_preview 'PreviewTokenStale: workspace was reloaded'; same token → preview_multi_file_edit_apply succeeds (3 files). symbol_refactor token → preview_multi_file_edit_apply reported 'reloaded'. Consumed token: rename_apply(token) twice → 2nd call "expired because the workspace was reloaded" (it was consumed). |
| `migrate-package-removes-shared-central-version` | C1 | migrate_package / remove_central_package_version drop PackageVersion entries other projects need | `src/RoslynMcp.Roslyn/Services/PackageMigrationOrchestrator.cs:164` | migrate_package_preview diff removes <PackageVersion Include="MSTest.TestAdapter"> from <repo>/Directory.Packages.props although tests/RoslynMcp.Tests and 2 more csproj reference it; warnings:null. |
| `build-output-parser-drops-locationless-errors` | C1 | build_project reports errorCount 0 for project-level NU/MSB errors | `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs:12` | build_project(SampleLib.Tests) → exitCode 1, stdout 'SampleLib.Tests.csproj : error NU1201: …', errorCount 0, diagnostics []. |
| `netanalyzers-package-duplicates-sdk-analyzers` | C6 | NetAnalyzers package + SDK built-in analyzers both run (every CA diagnostic reported twice) | `Directory.Build.props:27` | csc SARIF build of SampleLib: 74 results / 38 distinct; CA1822 64 = 32 unique ×2; list_analyzers 10 analyzers vs 6 assemblies. Package 10.0.401 ships no build/ props to disable the SDK copy (EnableNETAnalyzers defaults true). |

### Low (43)

| Row id | Check | Finding | Anchor | Evidence (capture) |
|---|---|---|---|---|
| `compilation-cache-dedupe-analyzer-references` | C1 | Server runs duplicate analyzer assemblies twice | `src/RoslynMcp.Roslyn/Services/CompilationCache.cs:247` | CA1822 reported 64 times for 32 unique occurrences on the sample solution. |
| `logging-capability-parity` | C6 | initialize advertises logging capability but server_info says logging:false | `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs:150` | initialize → capabilities.logging {}; logging/setLevel → {} success; server_info capabilities.logging=false; no notifications/message observed. |
| `nuget-vulnerability-scan-openworld-annotation` | C4 | nuget_vulnerability_scan annotated openWorldHint=false despite network access | `src/RoslynMcp.Host.Stdio/Tools/SecurityTools.cs:60` | annotations openWorldHint=false; tool shells `dotnet list package --vulnerable` against NuGet. No tool in the 174-tool surface sets openWorldHint=true. |
| `catalog-page-slots-silently-clamped` | C2 | Catalog paging resources clamp invalid offset/limit instead of erroring | `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs:272` | catalog/tools/-1/0 → OK offset 0 limit 1; catalog/tools/0/999 → limit 200; catalog/tools/abc → generic redacted error. |
| `catalog-diff-advertised-pair-rejected` | C2 | catalog-diff description advertises a version pair that is rejected | `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs:80` | catalog-diff/v2.3.1/v2.3.2 → 'Unsupported catalog diff. Request one of the version pairs advertised by the catalog resource.' — the catalog lists no pairs; only 2.3.1→current/latest works. |
| `tool-output-schema-coverage` | C4 | Only 8 of 175 tools declare outputSchema / structuredContent | `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` | tools/list: outputSchema on 8 tools (server_*, workspace_* status family), all 8 validate clean; 167 return JSON as text only. |
| `get-source-text-notfound-message-generic` | C1 | get_source_text NotFound does not say the file isn't in the workspace | `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:568` | get_source_text nonexistent path → "The requested item was not found. Ensure the workspace is loaded and the identifier is correct." |
| `di-registrations-tryadd-omitted-claims-complete` | C2 | get_di_registrations omits TryAdd* but reports totalCountMeaning=complete | `src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs:181` | get_di_registrations(Host.Stdio) → 14, totalCountMeaning 'complete'; source has 17. |
| `semantic-grep-comment-hit-location` | C1 | semantic_grep comment-scope hits report trivia start, not match position | `src/RoslynMcp.Roslyn/Services/SemanticGrepService.cs:233` | semantic_grep('TODO/FIXME', scope=comments) → RecordFieldAdditionImpactDto.cs line 64 col 4; actual TODO at line 70, not in snippet. |
| `schema-hint-truncates-on-abbreviation` | C4 | schemaHint first-sentence trim cuts 'e.g.' and drops constraints | `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:788` | recommend_workflow(task='') → schemaHint '…Natural-language task, e.g)'. |
| `analyze-snippet-declared-symbols-drop-fields` | C1 | analyze_snippet declaredSymbols omits fields, enums, events, ctors | `src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs:105` | analyze_snippet(kind=members) → CS0414 on _name but declaredSymbols lacks it. |
| `source-generated-documents-origin-and-meta` | C1 | source_generated_documents conflates MSBuild .g.cs with generator output and lacks _meta | `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:840` | source_generated_documents(W_RW) → bare array of 3 GlobalUsings.g.cs (MSBuild), no _meta. |
| `discover-capabilities-unknown-category-matches-all` | C3 | discover_capabilities returns the full surface for an unknown category | `src/RoslynMcp.Host.Stdio/Prompts/PromptMessageBuilder.cs:87` | discover_capabilities{taskCategory:'nonsense-category'} → 'capabilities relevant to nonsense-category: Tools (174)…'. |
| `workspace-status-verbose-not-superset` | C2 | roslyn://workspace/{id}/status/verbose lacks readiness fields the summary has | `src/RoslynMcp.Host.Stdio/Resources/WorkspaceResources.cs:64` | status has isReady/analyzersReady/restoreHint/solutionFileName/workspaceErrorCount; status/verbose has none of them. |
| `analyze-dependencies-prompt-unranked-node-cap` | C3 | analyze_dependencies prompt keeps 50 low-value external nodes | `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.cs:247` | Rendered nodes = 50 external namespaces (typeCount 0); RoslynMcp.Host.Stdio.Tools appears in cycles but not nodes. |
| `get-prompt-text-description-cites-list-prompts` | C3 | get_prompt_text description cites a nonexistent list_prompts | `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs:57` | promptName description: 'Use list_prompts on the resources channel…'. |
| `project-diagnostics-cold-scan-over-budget` | C1 | project_diagnostics cold scan 25 s and default page ~190 KB | `src/RoslynMcp.Roslyn/Services/DiagnosticProjectAnalyzer.cs:70` | project_diagnostics(limit=20) cold elapsedMs=24902 (budget 15 s); default limit=200 ≈190 KB; diagnostics resource cold 19.3 s. |
| `namespace-deps-circular-only-edge-leak` | C2 | get_namespace_dependencies circularOnly returns edges not on any cycle | `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:316` | circularOnly=true returns Host.Stdio.Tools→Core.Models (34), which is on no cycle. |
| `diagnostic-details-unformatted-description` | C1 | diagnostic_details returns the raw MessageFormat with {0} | `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs:132` | CS0414 → description "The field '{0}' is assigned but its value is never used". |
| `test-related-private-member-no-container-fallback` | C1 | test_related finds no tests for private members | `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs:309` | test_related(ClassifyValueTypeMutation 498:28) → 0; test_related(ParameterObjectService) → 45. |
| `trace-exception-flow-catch-ranking` | C1 | trace_exception_flow ranks catch(Exception) equal to nearer base catches | `src/RoslynMcp.Roslyn/Services/ExceptionFlowService.cs:172` | trace(PublicInvalidOperationException, max 5) → 5× catch(System.Exception) although 9 catch(InvalidOperationException) sites exist. Prior trace-exception-flow-no-throwsite-half partially fixed (throw sites now present). |
| `callers-callees-counts-cref-as-caller` | C1 | callers_callees counts doc-comment cref references as callers | `src/RoslynMcp.Roslyn/Services/SymbolRelationshipService.cs:367` | callers include `<see cref="GetCompilationAsync"/>` at ICompilationCache.cs:32,101. |
| `find-type-consumers-no-pagination-metadata` | C2 | find_type_consumers truncates without totalCount/hasMore | `src/RoslynMcp.Roslyn/Services/TypeConsumersService.cs:93` | find_type_consumers(limit=3) → {count:3, items:[…]} with no totalCount/hasMore; 34 files exist. |
| `workspace-changes-missing-revert-status` | C1 | workspace_changes lists reverted/rolled-back applies as applied | `src/RoslynMcp.Core/Services/IChangeTracker.cs:17` | seq 7/28/33 rolled back and 31/35/36 reverted are listed as plain applies; mixed path separators. validate_workspace(changedFilePaths=null) auto-scope lists G4Fixture.cs/G4CacheStore.cs/G4Triangle.cs twice (backslash vs forward-slash) — ChangeTracker stores unnormalized paths; validate_recent_git_changes has no dupes. |
| `bulk-replace-type-redundant-using` | C1 | bulk_replace_type_preview adds a using for the enclosing namespace | `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs:114` | bulk_replace_type_preview adds 'using SampleLib;' inside namespace SampleLib. |
| `refactor-preview-trivia-nits` | C1 | Refactor previews leave trivia defects | `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:133` | 'class G4Fixture\n : IG4Fixture'; dead-code removal leaves a '    ' line. |
| `set-editorconfig-option-creates-parallel-section` | C1 | set_editorconfig_option adds a new [*.{cs,csx,cake}] section beside an existing [*.cs] | `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:397` | set_editorconfig_option added [*.{cs,csx,cake}] next to the existing [*.cs] section (Phase 6/7). Related prior #735 (duplicate key) — no duplicate key this run. |
| `scaffold-type-mixed-line-endings` | C1 | scaffold_type_apply writes mixed CRLF/LF line endings | `src/RoslynMcp.Roslyn/Services/TypeScaffolder.cs:192` | scaffold_type_apply → G6Widget.cs LF lines with 2 CRLF blank separators. |
| `new-file-apply-writes-bom-ignoring-editorconfig` | C1 | New files written via TryApplyChanges get a BOM regardless of .editorconfig | `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1031` | move_type_to_file_apply → EF BB BF + CRLF with .editorconfig end_of_line=lf charset=utf-8; apply_composite writes no BOM but CRLF. Distinct from composite-apply-encoding-hygiene-consolidated (test/doc hygiene). |
| `move-type-to-project-unconditional-reference` | C1 | move_type_to_project_preview always adds a source→target reference | `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:94` | move_type_to_project_preview(TrulyUnusedConcreteType → SampleApp) → InvalidOperation (cycle). |
| `workspace-reload-restore-failure-not-flagged` | C1 | workspace_reload leaves restoreRequired=false after a restore failure | `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs` | workspace_reload after NU1201 → workspaceErrorCount 1, isReady false, restoreRequired false, restoreHint null. Root cause not traced — confirm anchor during deepening. |
| `mcpb-manifest-stale-or-retire` | C5 | manifest.json (MCPB 0.3) lists 8/174 tools and an unrunnable binary entry | `manifest.json` | Checked field-by-field against MCPB MANIFEST.md v0.3 (no validator installed): required fields present; tools 8/174, no tools_generated; server.type binary entry_point 'RoslynMcp.Host.Stdio' not a bundled path; mcp_config.command 'roslynmcp' (global tool). Nothing packages it (docs/release-policy.md:75 'legacy DXT-style'); it ships inside the plugin payload. |
| `consumer-analysis-cancellation-returns-partial` | C1 | ConsumerAnalysisService breaks on cancellation and returns partial results | `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs:51` | Code read during Phase 3 (bad-code sighting, Directive #3). |
| `side-effect-classifier-stale-comments` | C1 | SideEffectClassifier comments no longer match the code | `src/RoslynMcp.Roslyn/Helpers/SideEffectClassifier.cs:78` | Code read during Phase 3 (bad-code sighting, Directive #3). |
| `root-sample-solution-tree-duplicate` | C6 | Stale duplicate sample tree at repo root | `SampleSolution.slnx` | git ls-files → 19 tracked files last changed in 157fdded (1.14.0); root Program.cs differs from samples/SampleSolution copy; no eng/.github/justfile references; 2026-08-25 logging audit run2 stdout shows 2 candidate solutions (RoslynMcp.slnx, SampleSolution.slnx). |
| `shared-expression-probe-stale-comment` | C1 | SharedExpressionProbe fixture comment is stale | `samples/SampleSolution/SampleLib/SharedExpressionProbe.cs` | Phase 6k runner read (bad-code sighting, Directive #3). |
| `find-references-closest-matches-rank-anonymous-members` | C1 | NotFound closestMatches ranks anonymous-type members above the real symbol | `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs:493` | closestMatches = <>f__AnonymousType0.i/.p/.r, <>f__AnonymousType1.m/.n; real type absent. query.Contains(simpleName) scores 1-char names; ordinal tiebreak (:254) sorts <>f__ first. 7.8 s cold. |
| `test-run-zero-match-filter-silent-success` | C1 | test_run with a filter matching zero tests reports success silently | `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs` | test_run(filter=NoMatch, compact) → total 0, succeeded true, no warning. |
| `tool-alias-deprecation-stale-removal-major` | C2 | Alias deprecation metadata says removal in major 2 on a 4.x server | `src/RoslynMcp.Host.Stdio/Catalog/ToolAliasDeprecation.cs:49` | get_test_coverage_map deprecation.earliestRemovalMajor=2; server 4.2.1. |
| `revert-by-sequence-already-reverted-indistinct` | C1 | revert_apply_by_sequence cannot tell already-reverted from unknown | `src/RoslynMcp.Roslyn/Services/UndoService.cs:164` | revert_apply_by_sequence(60) twice → 2nd: reason=unknown-sequence "Either the sequence is from before this session, or the apply did not produce a revertable snapshot". |
| `structured-content-tools-omit-meta` | C4 | Status-family tools drop _meta from structuredContent | `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs:103` | Registered client shows no _meta for workspace_list, workspace_status, server_info, server_heartbeat, workspace_health, workspace_drift_check (structuredContent channel, UseStructuredContent=true). |
| `test-reference-map-counts-synthesized-members` | C1 | test_reference_map denominator includes compiler-synthesized members | `src/RoslynMcp.Roslyn/Services/TestReferenceMapService.cs` | test_reference_map(W_RW) → covered 6 / uncovered 148 (3.9%), uncovered includes AnimalRecord.<Clone>$(), PrintMembers, Cat.Cat(), IAnimal.Speak(). |
| `symbol-locator-mixed-input-silently-accepted` | C4 | Symbol locator silently accepts mixed/partial locator input | `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorFactory.cs:48` | test_related(metadataName=SampleLib.Cat, filePath=…) accepted; metadataName wins. Related test-only row symbollocatorfactory-drift-tool-test-gap. |

## Per-check summary

| Check | Artifact | Rows |
|---|---|---|
| C1 Tools | `02_mcp_tools.md` | 53 |
| C2 Resources / response contracts | `03_mcp_resources.md` | 9 |
| C3 Prompts & copy | `04_mcp_prompts.md` | 4 |
| C4 Schemas | `05_mcp_schemas.md` | 7 |
| C5 Manifest | `06_mcp_manifest.md` | 1 |
| C6 Parity / repo | `07_mcp_parity.md` | 3 |

## Coverage

174 tools, 14 resources and 20 prompts. Every entry has at least one recorded call: 175 exercised, 33 exercised-apply, 0 scoped-but-skipped. The full ledger is in `01_inventory_mcp.md`.

| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Scoped-but-skipped |
|---|---|---|---|---|---|---|
| prompt | prompts | 0 | 20 | 20 | 0 | 0 |
| resource | analysis | 1 | 0 | 1 | 0 | 0 |
| resource | server | 2 | 4 | 6 | 0 | 0 |
| resource | workspace | 6 | 1 | 7 | 0 | 0 |
| tool | advanced-analysis | 13 | 4 | 17 | 0 | 0 |
| tool | analysis | 13 | 3 | 16 | 0 | 0 |
| tool | code-actions | 3 | 0 | 2 | 1 | 0 |
| tool | configuration | 3 | 0 | 1 | 2 | 0 |
| tool | cross-project-refactoring | 0 | 3 | 3 | 0 | 0 |
| tool | dead-code | 1 | 2 | 2 | 1 | 0 |
| tool | editing | 3 | 3 | 1 | 5 | 0 |
| tool | file-operations | 3 | 3 | 3 | 3 | 0 |
| tool | orchestration | 0 | 5 | 4 | 1 | 0 |
| tool | project-mutation | 12 | 2 | 11 | 3 | 0 |
| tool | prompts | 0 | 1 | 1 | 0 | 0 |
| tool | refactoring | 13 | 20 | 22 | 11 | 0 |
| tool | scaffolding | 1 | 5 | 4 | 2 | 0 |
| tool | scripting | 1 | 0 | 1 | 0 | 0 |
| tool | security | 3 | 0 | 3 | 0 | 0 |
| tool | server | 2 | 0 | 2 | 0 | 0 |
| tool | symbols | 17 | 3 | 20 | 0 | 0 |
| tool | syntax | 1 | 0 | 1 | 0 | 0 |
| tool | undo | 2 | 1 | 0 | 3 | 0 |
| tool | validation | 10 | 4 | 13 | 1 | 0 |
| tool | workspace | 12 | 2 | 14 | 0 | 0 |

## Experimental promotion scorecard

Summary: promote 9 · keep-experimental 75 · needs-more-evidence 2 · deprecate 0. JSON: `audit-reports/_latest-promotion-scorecard.json` (tracked). Promote requires every signal observed, including an actionable negative probe, and no row filed against the entry. A single workspace is not a quorum.

| Kind | Name | Category | Recommendation | Evidence / blockers |
|---|---|---|---|---|
| tool | `workspace_warm` | workspace | **keep-experimental** | no negative probe recorded (unknown project silently skipped by design) |
| tool | `workspace_drift_check` | workspace | **keep-experimental** | no negative probe recorded |
| tool | `find_overloads` | symbols | **promote** | closure probe: base 2 overloads, derived+includeInherited 1 (private excluded), includeInherited=false 0; negative: unknown type -> structured NotFound; p50 7 ms |
| tool | `find_type_consumers` | symbols | **keep-experimental** | find-type-consumers-mutations-generator-blind (High); find-type-consumers-no-pagination-metadata |
| tool | `probe_position` | symbols | **promote** | G2/G3: identifier + whitespace trivia correct; agrees with symbol_info; negative: whitespace probe reported as Whitespace (actionable); p50 5 ms |
| tool | `trace_exception_flow` | advanced-analysis | **keep-experimental** | trace-exception-flow-catch-ranking |
| tool | `find_duplicate_helpers` | advanced-analysis | **keep-experimental** | find-duplicate-helpers-outermost-call-false-positive (>=50% FP) |
| tool | `find_dead_locals` | advanced-analysis | **keep-experimental** | find-dead-locals-captured-by-local-function (4/5 false positives) |
| tool | `find_dead_fields` | advanced-analysis | **keep-experimental** | compilation-cache-generator-rerun-blinds-unused-analysis (High: live fields safelyRemovable) |
| tool | `symbol_impact_sweep` | analysis | **keep-experimental** | no negative probe; mapper heuristic false positive on *Serializer param (unfiled, unconfirmed) |
| tool | `test_reference_map` | validation | **keep-experimental** | test-reference-map-counts-synthesized-members |
| tool | `validate_workspace` | validation | **keep-experimental** | workspace-changes-missing-revert-status (duplicate changedFilePaths by separator) |
| tool | `validate_recent_git_changes` | validation | **promote** | G5 #29: clean status, 8 git-derived paths normalized, composes compile_check + diagnostics + related tests; p50 698 ms |
| tool | `workspace_fork_apply` | validation | **keep-experimental** | preview-token-store-mismatch-false-stale (false stale for project-mutation tokens; leaves empty forks dir) |
| tool | `preview_record_field_addition` | analysis | **keep-experimental** | no negative probe recorded |
| tool | `semantic_grep` | analysis | **keep-experimental** | semantic-grep-comment-hit-location |
| tool | `format_check` | refactoring | **keep-experimental** | no negative probe recorded |
| tool | `restructure_preview` | refactoring | **keep-experimental** | restructure-preview-splice-without-parenthesization (High) |
| tool | `replace_string_literals_preview` | refactoring | **keep-experimental** | replace-string-literals-const-self-reference |
| tool | `change_signature_preview` | refactoring | **keep-experimental** | change-signature-add-skips-same-document-impls (High) |
| tool | `parameter_object_preview` | refactoring | **keep-experimental** | no negative probe recorded |
| tool | `symbol_refactor_preview` | refactoring | **keep-experimental** | preview-token-store-mismatch-false-stale (apply route undocumented) |
| tool | `split_service_with_di_preview` | refactoring | **needs-more-evidence** | skipped-repo-shape in G6 (no DI); G4 preview showed data loss (not applied) |
| tool | `record_field_add_with_satellites_preview` | refactoring | **keep-experimental** | record-satellites-line-insert-outside-initializer |
| tool | `move_type_to_file_apply` | refactoring | **keep-experimental** | new-file-apply-writes-bom-ignoring-editorconfig |
| tool | `change_type_namespace_preview` | refactoring | **keep-experimental** | change-type-namespace-emits-invalid-syntax (High) |
| tool | `extract_interface_preview` | refactoring | **keep-experimental** | refactor-preview-trivia-nits |
| tool | `extract_interface_apply` | refactoring | **keep-experimental** | no negative probe recorded |
| tool | `bulk_replace_type_apply` | refactoring | **keep-experimental** | bulk-replace-type-redundant-using |
| tool | `replace_invocation_preview` | refactoring | **keep-experimental** | replace-invocation-rewrites-replacement-body (High) |
| tool | `extract_type_apply` | refactoring | **keep-experimental** | extract-type-breaks-interfaces-and-publicizes-fields |
| tool | `extract_method_apply` | refactoring | **keep-experimental** | extract-method-nullable-return-flow-state |
| tool | `extract_shared_expression_to_helper_preview` | refactoring | **keep-experimental** | preview-token-store-mismatch-false-stale (no documented apply route) |
| tool | `apply_with_verify` | undo | **promote** | G4 6l: good preview status=applied; 4 known-bad previews rolled_back cleanly with introducedErrors; p50 1620 ms (writer budget 30 s) |
| tool | `fix_all_preview` | refactoring | **keep-experimental** | fix-all-analyzer-assembly-provider-not-found |
| tool | `fix_all_apply` | refactoring | **keep-experimental** | no negative probe recorded |
| tool | `format_range_apply` | refactoring | **keep-experimental** | format-range-refuses-on-unrelated-line-count-change |
| tool | `apply_multi_file_edit` | editing | **keep-experimental** | no negative probe recorded |
| tool | `preview_multi_file_edit` | editing | **promote** | G4 6h: per-file diffs + one token; stale-token negative rejected with actionable message; p50 < 1.6 s |
| tool | `preview_multi_file_edit_apply` | editing | **promote** | G4/G6: redeemed multi-file, batch-scaffold and dependency_inversion tokens; stale probe actionable; p50 514 ms |
| tool | `create_file_apply` | file-operations | **keep-experimental** | new-file-apply-writes-bom-ignoring-editorconfig; invalid-operation-throw-sites-lack-public-message |
| tool | `delete_file_apply` | file-operations | **keep-experimental** | invalid-operation-throw-sites-lack-public-message (vague negative) |
| tool | `move_file_apply` | file-operations | **keep-experimental** | no negative probe recorded |
| tool | `remove_dead_code_apply` | dead-code | **keep-experimental** | refactor-preview-trivia-nits |
| tool | `remove_interface_member_preview` | dead-code | **keep-experimental** | no negative probe recorded |
| tool | `get_prompt_text` | prompts | **keep-experimental** | invalid-operation-throw-sites-lack-public-message (unknown prompt / missing arg vague); get-prompt-text-description-cites-list-prompts |
| tool | `recommend_workflow` | orchestration | **keep-experimental** | schema-hint-truncates-on-abbreviation; argument-exception-throw-sites-lack-public-message |
| tool | `add_central_package_version_preview` | project-mutation | **keep-experimental** | invalid-operation-throw-sites-lack-public-message (existing version refusal redacted) |
| tool | `apply_project_mutation` | project-mutation | **keep-experimental** | no negative probe recorded |
| tool | `scaffold_type_preview` | scaffolding | **keep-experimental** | argument-exception-throw-sites-lack-public-message (typeKind=struct vague) |
| tool | `scaffold_type_apply` | scaffolding | **keep-experimental** | scaffold-type-mixed-line-endings |
| tool | `scaffold_test_batch_preview` | scaffolding | **keep-experimental** | preview-token-store-mismatch-false-stale (description names wrong apply tool) |
| tool | `scaffold_first_test_file_preview` | scaffolding | **keep-experimental** | invalid-operation-throw-sites-lack-public-message |
| tool | `scaffold_test_apply` | scaffolding | **keep-experimental** | new-file-apply-writes-bom-ignoring-editorconfig |
| tool | `move_type_to_project_preview` | cross-project-refactoring | **keep-experimental** | move-type-to-project-unconditional-reference |
| tool | `extract_interface_cross_project_preview` | cross-project-refactoring | **keep-experimental** | preview-token-store-mismatch-false-stale (no apply route; same-project target accepted) |
| tool | `dependency_inversion_preview` | cross-project-refactoring | **keep-experimental** | preview-token-store-mismatch-false-stale (apply only via undocumented tool) |
| tool | `migrate_package_preview` | orchestration | **keep-experimental** | migrate-package-removes-shared-central-version |
| tool | `split_class_preview` | orchestration | **keep-experimental** | no negative probe recorded; mixed path separators in response |
| tool | `extract_and_wire_interface_preview` | orchestration | **needs-more-evidence** | fixture has no DI registrations; applied via composite but DI rewiring unexercised |
| tool | `apply_composite_preview` | orchestration | **keep-experimental** | apply-composite-no-undo-capture (High) |
| resource | `server_catalog_full` | server | **promote** | G8: 165 KB full catalog; prompt params match prompts/list; counts match server_info |
| resource | `server_catalog_tools_page` | server | **keep-experimental** | catalog-page-slots-silently-clamped |
| resource | `server_catalog_prompts_page` | server | **promote** | G8: 0/5 and 18/10 pages correct, hasMore correct |
| resource | `server_catalog_version_diff` | server | **keep-experimental** | catalog-diff-advertised-pair-rejected |
| resource | `source_file_lines` | workspace | **promote** | G8: // roslyn://... marker present, end clamped, 10-5 and 99999-100000 rejected in ~1 ms (actionable); p50 58 ms |
| prompt | `explain_error` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument; int args reject spec strings) |
| prompt | `suggest_refactoring` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument; int args reject spec strings) |
| prompt | `review_file` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `analyze_dependencies` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument); analyze-dependencies-prompt-unranked-node-cap |
| prompt | `debug_test_failure` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `refactor_and_validate` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument; int args reject spec strings) |
| prompt | `fix_all_diagnostics` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `guided_package_migration` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `guided_extract_interface` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `security_review` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `discover_capabilities` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument); discover-capabilities-unknown-category-matches-all |
| prompt | `dead_code_audit` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `review_test_coverage` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `review_complexity` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `cohesion_analysis` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `consumer_impact` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument; int args reject spec strings) |
| prompt | `guided_extract_method` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument; int args reject spec strings) |
| prompt | `msbuild_inspection` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `session_undo` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |
| prompt | `refactor_loop` | prompts | **keep-experimental** | prompt-argument-binding-spec-strings-and-names (missing-arg error never names the argument) |

## Cross-run dedup

| Prior source | Result |
|---|---|
| `ai_docs/audits/20260825-1440/findings.json` (logging audit, scope observability) | different scope; no fingerprint overlap |
| `audit-reports/20260531T192823Z_…_mcp-server-surface-test.md` | 4 no longer reproduce (resolved set above); `trace-exception-flow-no-throwsite-half` partially fixed → residual filed as `trace-exception-flow-catch-ranking`; `find-implementations-corlib-metadataname-zero` **regressed** (source-anchored now also 0) → `find-implementations-corlib-guard-blocks-source-anchor` |
| live backlog | `cohesion-direct-method-call-connectivity` already covers the JsonLinesFileLogger LCOM4 finding → carried, not refiled. Related, not duplicates: `symbollocatorfactory-drift-tool-test-gap` (tests only), `composite-apply-encoding-hygiene-consolidated` (test/doc hygiene), `dockerfile-run-comment-and-sanctioned-roots-env` / `repo-local-mcp-registrations-decision` (config, not the error text) |

## Info (no row)

- `find_overloads` with an empty `memberName` returns a silent empty result.
- `test_coverage.failureEnvelope.missingPackages` holds project names rather than package names.
- `workspace_warm` silently skips unknown project names (documented).
- `project_diagnostics` rows are unsorted within a file.
- `symbol_search` totalCount caps at exactly 1000 (not confirmed as a clamp).
- `member_hierarchy` lists no implementations for an interface member (unconfirmed).

## Backlog

`backlog=apply`: 79 rows written through `backlog.mjs add` (10 High / 25 Medium / 44 Low: the 77 finding rows plus `surface-test-skill-prompt-drift` (Medium, shipped-skill drift) and `mcp-surface-audit-2026-09-24-info-digest` (Low, Info + unconfirmed signals)), with one root cause per row. The ids appear in the severity tables above and in `findings.json` → `backlog_id`. Nothing was committed, branched or opened as a PR.
