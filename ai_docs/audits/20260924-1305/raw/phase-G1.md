# Phase G1 evidence — Phases 1 + 2 (W_RO), Phase 1 steps 1-3 (W_RW)

W_RO = bbea96a6d4504f299a1d9c48d4329994 · W_RW = 4b02621ce1f44d3eadad0966849206b1 · server 4.2.1 (4eba2601). Paths sanitized: `<repo>`, `<user>`.

## Per-call log

| # | WS | Tool | Key inputs | elapsedMs | Verdict | Observation |
|---|---|---|---|---|---|---|
| 1 | RO | project_diagnostics | limit=20 | 24902 | FLAG | totals E0/W0/I4405; >15 s solution-scan budget (cold, concurrent with #2). Every CA* entry appears twice at identical location (see F5 — repo precondition). ~19.5 KB for 20 rows ⇒ default limit=200 ≈ 190 KB (>100 KB). MCP002 (generator diag) listed under `compilerDiagnostics`. |
| 2 | RO | project_diagnostics | summary=true | 25296 | PASS | 27 ids; all CA* counts even (doubled), MSTEST*/SYSLIB odd. |
| 3 | RO | project_diagnostics | diagnosticId=CA1507 | 176 | PASS | 2 rows, identical location — duplicate confirmed. |
| 4 | RO | project_diagnostics | severity=Warning, limit=5 | 246 | PASS | Invariant holds: totalInfo 4405 unchanged, filteredDiagnostics 0. |
| 5 | RO | project_diagnostics | projectName=RoslynMcp.Core, offset=2, limit=2 | 22 | PASS | Core has 0 diags (plausible). |
| 6 | RO | project_diagnostics | projectName=NoSuchProject, summary | 0 | FLAG | Silent all-zero result for unknown project (F6); compile_check throws InvalidArgument for same input. |
| 7 | RO | project_diagnostics | projectName=RoslynMcp.Host.Stdio, summary | 13 | PASS | 116 diags / 7 ids — filter works for valid name. |
| 8 | RO | compile_check | default | 1257 | PASS | 0 E/0 W, 1 Info MCP002 — agrees with project_diagnostics compilerDiagnostics (1). |
| 9 | RO | compile_check | severity=Error | 1426 | PASS | 0 diags. |
| 10 | RO | compile_check | file=ToolCallErrorWireContractTests.cs | 141 | PASS | requestedScope=files, 1 project compiled, MCP002 returned. |
| 11 | RO | compile_check | emitValidation=true | 6918 | PASS | Same single diag; 5.5x slower than GetDiagnostics path; no hang. |
| 12 | RO | compile_check | file=C:/nope/DoesNotExist.cs + projectName=NoSuchProject | 0 | PASS | InvalidArgument "No loaded project matches parameter 'projectName'. Use workspace_status..." — actionable. |
| 13 | RO | compile_check | file=C:/nope/DoesNotExist.cs | 0 | PASS | success=false + restoreHint explains file did not resolve — actionable. |
| 14 | RO | security_diagnostics | default | 20 | PASS | 0 findings; netAnalyzersPresent=true. |
| 15 | RO | security_analyzer_status | default | 0 | PASS | NetAnalyzers present; SecurityCodeScan absent (archived, not recommended). |
| 16 | RO | nuget_vulnerability_scan | default | 6405 | PASS | 6 projects, 0 CVEs, includesTransitive=false. |
| 17 | RO | nuget_vulnerability_scan | includeTransitive=true, projectName=RoslynMcp.Roslyn | 3469 | PASS | 1 project, 0 CVEs. |
| 18 | RO | list_analyzers | limit=5 | 32 | PASS | 18 analyzers / 462 rules; no LOAD_ERROR. Rules merged by assembly file name (hides the duplicate NetAnalyzers reference). |
| 19 | RO | list_analyzers | projectName=Host.Stdio, limit=1 | 2 | PASS | 16 analyzers / 388 rules. |
| 20 | RO | list_analyzers | offset=455, limit=20 | 39 | PASS | tail page: 7 rules, hasMore=false. RMCP001/002/010 present. |
| 21 | RO | list_analyzers | projectName=NoSuchProject | 0 | FLAG | Silent empty (F6). |
| 22 | RO | diagnostic_details | CA1507 @ PromptShimTools.cs:186:82 | 16 | PASS | Location exact; 1 fix (UseNameOfInPlaceOfStringFixer). |
| 23 | RO | diagnostic_details | MCP002 @ 350:30 (startLine/startColumn alias) | 5 | PASS | alias path works; guidance actionable. |
| 24 | RO | diagnostic_details | CS0103 at CA1507 location (negative) | 94 | PASS | found=false + actionable message. |
| 25 | RO | diagnostic_details | line AND startLine both =186 | 1 | PASS | Accepted although schema says "exactly one" — lenient when equal; not reported. |
| 26 | RW | project_diagnostics | limit=30 | 55 | PASS | E0/W2/I71; CA1822/CA1861 duplicated (same repo precondition — sample inherits root Directory.Build.props). |
| 27 | RW | compile_check | default | 5 | PASS | 2 CS0414 warnings = project_diagnostics compiler warnings (2). Agreement OK. (G4Fixture.cs was added by another runner mid-run.) |
| 28 | RW | project_diagnostics | severity=Warning | 76 | PASS | totals 82 (state moved via other runner), filtered 3 (2 CS0414 + MSTEST0032). Invariant holds. |
| 29 | RW | compile_check | severity=Error, files=[DiagnosticsProbe.cs] | 2 | PASS | 0 diags, files scope. |
| 30 | RW | diagnostic_details | CS0414 @ DiagnosticsProbe.cs:9:24 | 28 | FLAG | `description` = raw MessageFormat "The field '{0}' is assigned but its value is never used" (unformatted placeholder) (F9). |
| 31 | RO | get_complexity_metrics | limit=12 | 715 | PASS | Plausible; top CC 23. |
| 32 | RO | get_cohesion_metrics | minMethods=3, limit=8, excludeTestProjects | 36 | FLAG | JsonLinesFileLogger LCOM4=3 with fieldCount 0; Log() calls IsEnabled() and both BeginScope/Log use primary-ctor param `provider` ⇒ true LCOM4=1 (matches backlog `cohesion-direct-method-call-connectivity`). Source-gen partial exclusion OK (no LoggerMessage names in SharedFields). |
| 33 | RO | get_coupling_metrics | limit=8, excludeTestProjects | 2627 | FAIL | Ca=0 for WorkspaceManager, ProjectMutationService, WorkspaceValidationService — all referenced from ServiceCollectionExtensions (`AddSingleton<IProjectMutationService, ProjectMutationService>()` etc.). (F1) |
| 34 | RO | get_coupling_metrics | summary=true, includeInterfaces | 3104 | FAIL | Rollup covers only the top-100 (limit) slice: totalTypes=100, 3 of 6 projects, Core missing (F4). |
| 35 | RO | get_coupling_metrics | summary=true, limit=5000 | 2740 | FAIL | 1504 types: RoslynMcp.Roslyn 325 types stable=0 balanced=0; Host.Stdio 197 types stable=0 balanced=0 ⇒ every type in generator-emitting projects has Ca=0 (F1). Core/Tests/Analyzer have stable types. |
| 36 | RO | find_unused_symbols | includePublic=false, limit=25 | 38 | FAIL | 25/25 hits in Host.Stdio are live, all confidence=high: ServerSurfaceCatalog.Tool() (100+ call sites), PromptParameterIndex (used PromptShimTools.cs:93), CatalogTypeNameFormatter (ToolParameterIndex.cs:99) … (F1) |
| 37 | RO | find_unused_symbols | includePublic=true, projectName=RoslynMcp.Core, limit=8 | 1258 | PASS | 0 hits — Core (no generator output) behaves. |
| 38 | RO | find_unused_symbols | includePublic=true, projectName=RoslynMcp.Roslyn, excludeTestProjects, limit=6 | 179 | FAIL | ICodeFixProviderRegistry + members, EvictPolicy flagged unused — live (F1). 179 ms = reference scan short-circuits. |
| 39 | RO | find_duplicated_methods | projectFilter=RoslynMcp.Roslyn, limit=5 | 114 | PASS | 5 exact-structure pairs; StripLeadingTriviaFromFirstUsing + CaptureOriginalTextAsync are real copy-paste. FindReflectionUsagesAsync≈GetNuGetDependenciesAsync is gate-wrapper shape (structural, by design). |
| 40 | RO | find_duplicate_helpers | limit=15 | 23 | FLAG | Spot-checked 10: ≥5 clear FPs (FormatIdentityDrift builds a 4-call array; BuildEntries Where/Select/ToArray chain; AppendRow 5-call builder chain; FormatVersionCheckStatus Serialize+Trim; ResolvePathAsync Task.Run(domain)); rest bind private constants/state (CountByTier, PathsEqual, GenerateSchema, HasExplicitErrorCodeMapping) — not reinvented BCL. All marked confidence=high (F3). |
| 41 | RO | find_duplicated_code | summary=true, minLines=15, limit=10 | 240 | PASS | alias; deprecation block present; summary omits member arrays as documented. |
| 42 | RO | find_dead_locals | limit=10 | 3958 | FLAG | 5 hits; `server` (AnalysisToolsTests:343) TP; 4× `observed` FP — outer local captured and written in local function `ObservingHandler`, read in outer method (WorkspaceReloadedEventTests:60/86/133); containingMethod reports the local function (F7). |
| 43 | RO | find_dead_fields | limit=10 | 42 | FAIL | 10/10 live fields reported never-read, confidence=high, safelyRemovable=true: s_allTools (read at ServerSurfaceCatalog.cs:86), s_toolsByName (:95), AnalysisTools (:32) … would feed remove_dead_code_preview (F2). |
| 44 | RO | get_namespace_dependencies | circularOnly=true | 172 | FLAG | 4 cycles found (plausible). Edge filter is node-membership, not cycle-membership: returns Tools→Core.Models (34), ProtocolCompatibility→Core.Services, Tests.Helpers→Core.Models — not on any cycle (F8). |
| 45 | RO | get_nuget_dependencies | summary=true | 8 | PASS | 30 packages, isComplete; NetAnalyzers projectCount=6 (corroborates F5 precondition). |
| 46 | RO | suggest_refactorings | limit=8 | 718 | PASS | Ranked by complexity; recommendedTools exist (analyze_data_flow, extract_method_preview/apply, compile_check). Unused-symbol leg inherits F1. |

## Confirmation notes

- **F1 root cause.** `CompilationCache` default factory = `SourceGeneratorCompilation.CreateAsync` (`src/RoslynMcp.Roslyn/Services/CompilationCache.cs:61-62`), which reruns generators via `CSharpGeneratorDriver.RunGeneratorsAndUpdateCompilation` (`SourceGeneratorCompilation.cs:46-54`) and returns a NEW compilation not owned by the Solution. Consumers enumerate candidate symbols from that compilation and pass them to `SymbolFinder.FindReferencesAsync(symbol, solution)`; Roslyn cannot map the foreign symbol to a solution project ⇒ zero references. Affected: `UnusedCodeAnalyzer.cs:96,145,253` (find_unused_symbols), `:970` (find_dead_fields), `CouplingAnalysisService.cs:102,108,238` (Ca). Projects with no generator output (Core, Tests, analyzer) are unaffected — consistent with evidence. Introduced by #1402 (`92ac5a9c`), which is an ancestor of 4.2.1. None of these files changed 4.2.1→HEAD ⇒ not fixed at HEAD. Fix direction: enumerate/resolve symbols from `project.GetCompilationAsync()` (solution-owned) for SymbolFinder, keep the generator-rerun snapshot only for diagnostics; or `SymbolFinder.FindSourceDefinitionAsync`/SymbolKey-resolve into the solution compilation before searching. Audit the other 11 `_compilationCache.GetCompilationAsync` consumers for the same pattern.
- **F5 precondition proof.** `dotnet build SampleLib -p:ErrorLog=...sarif` ⇒ 74 results / 38 distinct (CA1822@ConventionFixtures.cs:43:17 ×2 …): csc itself double-reports. Cause: root `Directory.Build.props:27` adds `Microsoft.CodeAnalysis.NetAnalyzers` package while SDK `EnableNETAnalyzers=true` (AnalysisLevel latest) also injects the SDK copy (`Microsoft.NET.Sdk.Analyzers.targets:123-134`); package 10.0.401 ships no build/ props to disable the SDK copy. Repo defect, not server. Server could still dedupe analyzer refs by assembly identity (`CompilationCache.cs:247-249`) the way `list_analyzers` already merges by file name.
- Backlog search (`ai_docs/backlog.md`): no rows for F1/F2/F3/F4/F6/F7/F8/F9; F10 matches `cohesion-direct-method-call-connectivity`.

## Timing (check 5)

Budget 15 s solution scan. Only project_diagnostics exceeded (24.9 s / 25.3 s cold, two concurrent calls; warm 13–246 ms). compile_check emit 6.9 s, nuget scan 6.4 s, dead_locals 4.0 s, coupling 2.6–3.1 s — all within budget. Large default payload: project_diagnostics default limit=200 ≈ 190 KB (measured ~975 B/row; each row duplicates file/line fields in `location`).

## complexityTop (W_RO)

| symbol | filePath | line | CC |
|---|---|---|---|
| ParameterObjectService.ClassifyValueTypeMutation | src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs | 498 | 23 |
| WorkspaceDispatchValidationPrecedenceTests.InvokeScenarioAsync | tests/RoslynMcp.Tests/WorkspaceDispatchValidationPrecedenceTests.cs | 53 | 23 |
| SideEffectClassifier.ClassifyMethod | src/RoslynMcp.Roslyn/Helpers/SideEffectClassifier.cs | 87 | 22 |
| ConsumerAnalysisService.FindConsumersAsync | src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs | 20 | 21 |
| MutationAnalysisService.ClassifyTypeUsageAfterWalk | src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs | 665 | 21 |
| TypeMoveService.PreviewMoveTypeToFileAsync | src/RoslynMcp.Roslyn/Services/TypeMoveService.cs | 21 | 21 |
| ElicitationChoicePrompt.TryElicitChoiceCoreAsync | src/RoslynMcp.Host.Stdio/Elicitation/ElicitationChoicePrompt.cs | 138 | 20 |
| TestCoverageCoordinator.ParseAndAggregateCoberturaXml | src/RoslynMcp.Roslyn/Services/TestCoverageCoordinator.cs | 56 | 20 |
| ParameterObjectService.EnforceNewParameterNameIsAvailableAsync | src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs | 232 | 19 |
| ParameterObjectService.BindCallSite | src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs | 832 | 19 |
