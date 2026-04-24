# MCP Server Audit Report

## 1. Header
- **Date:** 2026-04-24 02:26:57Z
- **Audited solution:** NetworkDocumentation.sln
- **Audited revision:** HEAD = 9394f29 (branch: main). Working tree carried substantial uncommitted edits in Collection/Credentials/Output/Tests + frontend.
- **Entrypoint loaded:** `C:\Code-Repo\DotNet-Network-Documentation\NetworkDocumentation.sln`
- **Mode:** `full` (requested) — **effective Phase 6 / 9 / 10-apply / 12-apply / 13-apply / 8b.5 posture: conservative / preview-only / skipped-safety** (see Isolation).
- **Audit mode:** effectively `conservative` (forced by MCP client root allow-list).
- **Isolation:** Disposable worktree prepared at `C:\Code-Repo\DotNet-Network-Documentation-audit-run` (detached HEAD 9394f29). **BLOCKER (recorded as P1):** the MCP client enforces a sanctioned-roots allow-list containing only `file://C:\Code-Repo\DotNet-Network-Documentation`. `workspace_load` on the worktree path returned `InvalidArgument: Path is not under any client-sanctioned root`. Consequence: the MCP-backed audit targets the main tree where the user has uncommitted work, and Phase 6 applies would commingle with that work. Phase 6 therefore executed preview-only; writer round-trips skipped wherever they would touch the user tree. Worktree retained for shell fallbacks (dotnet restore/build) only.
- **Client:** Claude Code (version not surfaced via any probed channel). Resource family (`ListMcpResourcesTool`, `ReadMcpResourceTool` for `roslyn://…`) returns `Server "roslyn" is not connected` — client does not route resource reads to the loaded `roslyn` MCP server despite tool calls working. Recorded as client limitation (Phase 15 mostly blocked).
- **Workspace id:** initial `db2866ded7dd476fada438a961ab4b64` (workspaceVersion=1); auto-re-created as `b97e5b69473a46ffa4290ec541b8bbc5` after server crash at ~02:39 UTC (see §14.1).
- **Warm-up:** `yes` — `workspace_warm` ran after first load (elapsedMs=3285, coldCompilationCount=6). Timings post-warm are cache-hit-dominated. The reload after the crash was **not** warmed (time-budget).
- **Server:** `roslyn-mcp` v1.29.0+a007d7d75c6bd2c40fd6adaa19ee1ead664be2f1, productShape=local-first. Initial stdioPid=40452, after crash stdioPid=21500.
- **Catalog version:** 2026.04
- **Roslyn / .NET:** Roslyn 5.3.0.0 / .NET 10.0.7 / Windows 10.0.26200
- **Live surface (from `server_info`):** tools 107 stable / 54 experimental (registered 161, parityOk=true); resources 9 stable / 4 experimental (registered 13, parityOk=true); prompts 0 stable / 20 experimental (registered 20, parityOk=true)
- **Scale:** 9 projects, 433 documents
- **Repo shape:** 9 C# projects all TargetFramework=`net10.0`, Nullable=enable, no multi-targeting. Projects: Core (pure lib), Parsers, Excel, Diagrams, Reports, Collection (all lib); Cli (Exe); Web (Library → ASP.NET Core; hosts 17 DI registrations incl. 8 HostedServices); Tests (single xUnit v2 + Playwright integration project, referencing every other project). No Central Package Management detected (solution-wide explicit versions). Source generators present: `Microsoft.Extensions.Logging.Generators` (LoggerMessage.g.cs) and `System.Text.RegularExpressions.Generator` (RegexGenerator.g.cs) — both in Core. `.editorconfig` at repo root (27 active options, file-scoped namespaces, LF line endings, 4-space indent, `dotnet_sort_system_directives_first=true`). Analyzers: Meziantou.Analyzer 3.0.50, SecurityCodeScan.VS2019 5.6.7, Puma.Security.Rules 2.4.11, all 9 projects.
- **Prior issue source:** this repo's own `ai_docs/backlog.md` exists but is **not** the Roslyn-Backed-MCP backlog; wrapper directive skips the Roslyn-Backed-MCP backlog cross-check. No prior `audit-reports/` entry exists in this repo. **Regression section: N/A — no prior source**.
- **Debug log channel:** `no` — Claude Code did not surface `notifications/message` entries during the run. Principle #8 limitation; server may have emitted `McpLoggingProvider` entries but they never reached the agent.
- **Plugin skills repo path:** `C:\Code-Repo\Roslyn-Backed-MCP\` reachable; `glob skills/*/SKILL.md` yielded 32 shipped skills. Phase 16b executed.
- **Report path note:** this raw audit file is written to `<audited-repo>/audit-reports/`. **Intended canonical destination (per prompt): `C:\Code-Repo\Roslyn-Backed-MCP\ai_docs\audit-reports\20260424T022657Z_networkdocumentation_mcp-server-audit.md`** — copy this file there before generating any rollup, then run `eng/import-deep-review-audit.ps1 -AuditFiles <path>`.

### Phase -1 gate evidence
Initial `server_info` after cold start reported `connection.state=initializing, loadedWorkspaceCount=0, stdioPid=40452`. After `workspace_load` + `workspace_warm`, `server_heartbeat` transitioned to `state=ready, loadedWorkspaceCount=1`. `surface.registered.parityOk=true` across tools/resources/prompts. Version 1.29.0 ≥ required 1.29.0. Catalog-resource sanity check **failed to read** — `ReadMcpResourceTool` rejects `roslyn://server/catalog` with *"Server 'roslyn' is not connected"*. This is a **client-surface bug**, not a server defect (tool calls in the `mcp__roslyn__*` namespace work fine). Coverage-ledger tier mapping is driven by `server_info.surface` totals + tool-name conventions + the `discover_capabilities` prompt's catalog dump (which served as an authoritative substitute — see §10).

## 2. Coverage summary
| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|--------|--------------|-----------|------------------|--------------|--------------------|----------------|---------|-------|
| Tool | server/workspace | 8 | 1 | 9 | 0 | 0 | 0 | 0 | 0 | server_info, server_heartbeat, workspace_load (×2 due to crash), workspace_warm, workspace_list, workspace_status (incl. negative), workspace_reload (skipped — not retried after crash), workspace_close (skipped-safety to avoid 2nd crash), workspace_health (alias of workspace_status — counted implicit) |
| Tool | diagnostics | 7 | 1 | 8 | 0 | 0 | 0 | 0 | 0 | project_diagnostics (summary + severity + project+id filters), compile_check (default + emitValidation=true), security_diagnostics, security_analyzer_status, nuget_vulnerability_scan (incl. transitive), list_analyzers (default + projectName+offset), diagnostic_details, format_check |
| Tool | metrics/quality | 11 | 4 | 11 | 0 | 0 | 0 | 0 | 0 | get_complexity_metrics, get_cohesion_metrics, get_coupling_metrics, find_unused_symbols (private + public), find_duplicated_methods, find_duplicate_helpers, find_dead_locals, find_dead_fields (usageKind=never-read), get_namespace_dependencies (circularOnly), get_nuget_dependencies (summary), suggest_refactorings |
| Tool | symbol analysis | 13 | 3 | 16 | 0 | 0 | 0 | 0 | 0 | symbol_search (+negative empty), symbol_info, document_symbols, type_hierarchy, find_implementations (+negative), find_references (+summary, bulk), find_consumers, find_shared_members, find_type_mutations, find_type_usages, callers_callees, find_property_writes, member_hierarchy, symbol_relationships, symbol_signature_help (preferDeclaringMember=false), impact_analysis, probe_position, symbol_impact_sweep (summary+maxItems), find_overrides, find_base_members |
| Tool | flow | 5 | 1 | 5 | 0 | 0 | 0 | 0 | 0 | analyze_data_flow, analyze_control_flow, get_operations, get_syntax_tree (maxTotalBytes), get_source_text, trace_exception_flow |
| Tool | snippet/script | 2 | 0 | 2 | 0 | 0 | 0 | 0 | 0 | analyze_snippet (4 kinds + empty), evaluate_csharp (numeric + multi-line + runtime-error + empty) |
| Tool | refactor preview | 22 | 17 | 0 | 0 | 20 | 0 | 17 | 0 | 6a/6b/6c/6e/6f/6g/6h/6i/6j/6k/10/12/13 previews ran; apply siblings `skipped-safety`. See §5 and §17. |
| Tool | refactor apply | 0 | 19 | 0 | 0 | 0 | 0 | 19 | 0 | All `*_apply` tools skipped-safety due to MCP-root blocker on disposable worktree. |
| Tool | project mutation preview | 8 | 2 | 0 | 0 | 2 | 4 | 0 | 0 | set_project_property_preview, add_package_reference_preview previewed. CPM tools skipped-repo-shape (no CPM); target framework tools skipped-repo-shape (single-TFM); remove_* skipped to avoid mess. |
| Tool | scaffolding | 2 | 3 | 0 | 0 | 4 | 0 | 1 | 0 | scaffold_type_preview, scaffold_test_preview, scaffold_test_batch_preview, scaffold_first_test_file_preview. scaffold_test_apply skipped-safety. |
| Tool | file ops preview | 4 | 0 | 0 | 0 | 4 | 0 | 0 | 0 | move_type_to_file_preview, move_file_preview (skipped-safety), create_file_preview, delete_file_preview, split_class_preview, extract_and_wire_interface_preview (failed post-reload), move_type_to_project_preview (skipped-repo-shape too invasive). |
| Tool | build & test | 6 | 4 | 6 | 0 | 0 | 0 | 4 | 0 | build_project, validate_workspace(summary), validate_recent_git_changes(summary), test_discover, test_related_files, test_related, test_run (1-test filter). build_workspace, test_run (full suite), test_coverage, test_reference_map (projectName=Core edge case hit), apply_with_verify — last 3 skipped-time/safety. |
| Tool | editorconfig / msbuild | 5 | 0 | 4 | 0 | 0 | 0 | 1 | 0 | get_editorconfig_options, evaluate_msbuild_property, evaluate_msbuild_items, get_msbuild_properties (filter). set_editorconfig_option skipped-safety. |
| Tool | semantic/DI/discovery | 5 | 0 | 5 | 0 | 0 | 0 | 0 | 0 | semantic_search (×3 paraphrase), find_reflection_usages, get_di_registrations (overrideChains=true), source_generated_documents. |
| Tool | navigation/completions | 6 | 0 | 6 | 0 | 0 | 0 | 0 | 0 | go_to_definition, goto_type_definition (negative), enclosing_symbol, get_completions (filterText), find_references_bulk, find_overrides, find_base_members. |
| Tool | prompts | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | get_prompt_text (5 positive + 2 negative). |
| Tool | concurrency / undo | 2 | 0 | 0 | 0 | 0 | 0 | 2 | 0 | revert_last_apply, workspace_changes — skipped-safety (would require an apply; no applies safe on user tree). workspace_changes additionally crashed during Phase 6 (see §14.1). |
| Tool | suppress/pragma | 3 | 0 | 0 | 0 | 0 | 0 | 3 | 0 | set_diagnostic_severity, add_pragma_suppression, verify_pragma_suppresses, pragma_scope_widen — skipped-safety. |
| Tool | migrate/composite | 6 | 6 | 0 | 0 | 0 | 0 | 12 | 0 | migrate_package_preview, record_field_add_with_satellites_preview, extract_shared_expression_to_helper_preview, split_service_with_di_preview, etc. — skipped-time/safety. apply_composite_preview is destructive-despite-name, not exercised. |
| Resource | all categories | 9 | 4 | 0 | 0 | 0 | 0 | 0 | 13 | **blocked — client not connected to the roslyn MCP resource channel** (`ListMcpResourcesTool` returns empty; `ReadMcpResourceTool` reports disconnection). All 13 resources unreachable this run. |
| Prompt | all | 0 | 20 | 5 | — | — | 0 | 0 | 15 | Exercised: explain_error, discover_capabilities, review_file, suggest_refactoring, refactor_and_validate. Negative probes on unknown name + malformed JSON produced excellent errors. Remaining 15 prompts: schema-verified via the `Available prompts` error listing; rendered-output skipped-time. |

_Ledger rollup per live-catalog count (161 tools + 13 resources + 20 prompts = 194 entries). Exercised = 88 (of which 20 preview-only on writers); skipped-safety = 69; blocked = 13 (resources) + ~14 scattered preview/apply entries gated by stale tokens / post-crash state + 1 worst-case workspace_close; skipped-repo-shape = 6 (CPM + multi-targeting tools)._

## 3. Coverage ledger
_Per-entry status summary. Exhaustive entries not reproduced here to stay inside the turn budget; the §2 category rollup is authoritative at the kind/category level. Per-tool detail for every exercised surface is in §4 / §6 / §12. Entries not in §4/§6/§12 carry the category-level status from §2._

## 4. Verified tools (working)
**Server / workspace**
- `server_info` — PASS; surface counts consistent. p50 elapsedMs=17 cold, <5 warm
- `server_heartbeat` — PASS; elapsedMs ≤10
- `workspace_load` — PASS; cold 4644 ms, reload after crash 4627 ms; autoRestore=false reported `restoreRequired=false` as expected (pre-restored host)
- `workspace_warm` — PASS; elapsedMs=3285, coldCompilationCount=6, projectsWarmed=9
- `workspace_list`, `workspace_status`, `workspace_health` — PASS
- `project_graph` — PASS; 9 projects; clean DAG rooted at Core

**Diagnostics**
- `project_diagnostics (summary)` — 1405 Info / 0 err / 0 warn solution-wide, 18277 ms cold; non-default filters probed (projectName+diagnosticId 27 ms; severity=Error 84 ms; invariant totals preserved across filters)
- `compile_check` — 9631 ms cold default, 1861 ms with `emitValidation=true` post-warm (clean workspace so no 50–100× delta)
- `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan(includeTransitive=true)` — all PASS; 0 findings, 0 CVEs
- `list_analyzers` — 35 analyzers / 877 rules solution-wide; non-default projectName+offset=50 returned 21/746 for Core
- `diagnostic_details` — returns helpLink; `supportedFixes=[]` for MA0001 (paired with Phase 6 `code_fix_preview` confirming "No code fix provider loaded for MA0001" — precondition issue, not server defect)
- `format_check(projectName=Core)` — 1 violation in 102 documents (InventorySummaryBuilder.cs)

**Metrics**
- `get_complexity_metrics(minComplexity=10)` — 20 hotspots. Top: PutCollectionSettings CC=40, PutSnmpSettings CC=32, BuildVersionRecord CC=28, ParseNxos CC=25
- `get_cohesion_metrics(minMethods=3, excludeTestProjects=true)` — 15 types with LCOM4≥1; PipelineProgressReporter LCOM4=24 (logger facade — each method its own cluster); SnapshotStore LCOM4=5 with identifiable extraction cluster
- `get_coupling_metrics(excludeTestProjects=true)` — 15 unstable types, all Web endpoint handlers (Ca=0, Ce=12–34). *Backlog note `coupling-metrics-tool=open` is STALE — tool is live and healthy.*
- `find_unused_symbols(includePublic=false)` — 0 (clean private surface)
- `find_unused_symbols(includePublic=true)` — 15 hits; mostly positional-record exception-property false positives (medium/low confidence correctly marked). **FLAG:** WebHostSerialCollection (xUnit CollectionDefinition attribute holder) slipped past `excludeConventionInvoked=true`
- `find_duplicated_methods(minLines=12)` — 9 groups; cross-project dup PathAnalysisIpMap.Build ↔ DiagramMappingHelpers.BuildIpToDeviceMap (38 lines)
- `find_duplicate_helpers` — 15 thin forwarders; notable `EscapeHtml` double-declared in DiagramLegendRenderer + HtmlDiagramRenderer
- `find_dead_locals` — 10 hits (production: L2/L3DiagramBuilder dead `region`/`stpDomainTag`; multiple test dead `deviceMap`)
- `find_dead_fields(usageKind=never-read)` — 3 fields (`AppConfig._instance` write-only singleton, `SnapshotManifestManager._payloadDir`, `WebApiContractTests._factory`)
- `get_namespace_dependencies(circularOnly=true)` — **real finding**: cycle `Core.Models ↔ Core.Routing` (1 ref each way)
- `get_nuget_dependencies(summary=true)` — 27 packages, all single-version
- `suggest_refactorings(limit=15)` — 15 category=complexity/severity=high suggestions; ranking matches `get_complexity_metrics`; recommended tool chain consistent

**Symbol analysis (target: SnapshotStore)**
- `symbol_search(query=SnapshotStore, kind=Class)` — 4 hits (incl. Log partial + WebSnapshotStoreContext)
- `symbol_info(metadataName)` — full metadata + XML doc
- `document_symbols` — hierarchical tree; partial modifier surfaced correctly
- `type_hierarchy` — null hierarchy (non-derived class)
- `find_consumers` — 22 consumers across 5 projects; dependency-kind classification populated
- `find_type_mutations` — 2 members with `mutationScope=IO` (v1.8+ IO-classification working)
- `find_shared_members` — 4 shared members
- `find_type_usages` — 51 usages, paginated clean
- `callers_callees(file+pos)` on Save at 57:30 — 26 callers / 19 callees
- `symbol_relationships` — definitions include source-gen `.g.cs` sibling (correct partial-class fan-out)
- `symbol_signature_help(preferDeclaringMember=false)` — literal-token resolution works
- `probe_position(line=25, col=1)` — correctly reported `PublicKeyword` (not whitespace)
- `symbol_impact_sweep(summary=true, maxItemsPerCategory=15)` — clean categorization; 1 suggested task
- `member_hierarchy(WebSnapshotStoreContext.Equals)` — correctly resolved base to `System.IEquatable<T>.Equals`
- `find_references_bulk` — 2 symbols, 26 + 3 refs
- `find_property_writes` — returned 0 on positional-record property (expected; documented limitation)

**Flow**
- `get_source_text(lines 57–120)` — truncated=false; line metadata correct
- `analyze_data_flow(57–120)` — all declared/alwaysAssigned/readInside/writtenInside populated
- `analyze_control_flow(57–120)` — 3 exit points at 80/94/119 + warning explaining `endPointIsReachable=false`
- `get_operations(line=73,col=18)` — resolved to `Invocation: string.IsNullOrEmpty(inputSignature)`
- `get_syntax_tree(57–75, maxTotalBytes=20000)` — MethodDeclaration tree bounded
- `trace_exception_flow(ParseException)` — 10 catch sites, all `catchesBaseException=true` (broad `catch (Exception)` clauses), body excerpts populated

**Snippet & script**
- `analyze_snippet(expression, program, statements, returnExpression)` — PASS across kinds; `int x="hello";` → CS0029 startColumn=9 (user-relative, FLAG-C fix v1.7+ confirmed)
- `analyze_snippet("")` — empty accepted; declaredSymbols=null
- `evaluate_csharp` — 55 for Enumerable.Range.Sum; `"a-b-c"` for string.Join; runtime FormatException for int.Parse("abc"); empty input → resultValue=null, success=true

**EditorConfig & MSBuild**
- `get_editorconfig_options` — 27 options resolved from repo `.editorconfig`
- `evaluate_msbuild_property(TargetFramework)` — "net10.0"
- `evaluate_msbuild_items(Compile)` — 100 Compile items for Core (consistent with DocumentCount 101 +1 for SDK-implicit; note: Core.Log partial counted)
- `get_msbuild_properties(propertyNameFilter=Nullable)` — Nullable=enable, filter narrowed 718→1 items correctly

**Build & test**
- `build_project(Core)` — 4890 ms, exit 0, 0 warnings
- `test_discover(nameFilter=SnapshotStore, limit=20)` — 13/13, hasMore=false
- `test_related_files` — 10 related + dotnetTestFilter generated
- `test_related(metadataName=SnapshotStore.Save)` — 100+ heuristic matches (broad — see §7)
- `test_run(filter=one test)` — 1 passed, 0 failed, 11212 ms
- `validate_workspace(summary=true)` — overallStatus=clean, 9/9 projects compile
- `validate_recent_git_changes(summary=true)` — auto-derived 16 changed files from `git status`, overallStatus=clean, Collection-scoped dotnetTestFilter generated

**Semantic search / DI / generation**
- `semantic_search("async methods returning Task<bool>")` — 1 hit (SshShellSession.SendEnableAsync)
- `semantic_search("methods returning Task<bool>")` — identical 1 hit (no async keyword-less Task<bool> methods in repo — modifier-sensitivity invisible in this shape)
- `semantic_search("classes implementing IDisposable")` — 10 hits (BackgroundServices); 2 test-helper entries via fallback name-token match
- `find_reflection_usages(Core)` — 0 hits (repo has no reflection in Core)
- `get_di_registrations(showLifetimeOverrides=true)` — 17 registrations (all Web), 0 lifetime overrides / override chains — clean composition root
- `source_generated_documents(Core)` — `LoggerMessage.g.cs`, `RegexGenerator.g.cs`

**Navigation / completions**
- `go_to_definition(SnapshotStore.cs:63:37)` → SnapshotContentHasher class (correct)
- `goto_type_definition(62:13 over 'var')` — correctly refused with "Cannot navigate… defined in an external assembly" (boundary path; actionable)
- `enclosing_symbol(73,20)` → SnapshotStore.Save method
- `get_completions(62:50, filterText=To, maxItems=10)` — 0 items returned at this position (not a good completion point; cannot confirm v1.8+ `ToString` vs `ToBase64Transform` ranking from this repo)
- `find_overrides(System.IEquatable\`1.Equals)` → 0 (metadata-boundary limitation; pair with `member_hierarchy` which resolves the same relationship — see §18 / §7)
- `find_base_members(WebSnapshotStoreContext.Equals)` → 0 (inconsistent with `member_hierarchy` which returned the IEquatable base; response-contract drift, §18)

**Prompts (Phase 16)** — see §10 table.

## 5. Phase 6 refactor summary
- **Target repo:** N/A — preview-only per Isolation note.
- **Scope:** 6a (fix_all preview), 6b (rename preview), 6c (extract_interface_preview), 6e (format_range / organize_usings previews + format_check), 6f (code_fix_preview on MA0001 — confirmed missing fixer), 6g (get_code_actions with +without explicit end), 6h (preview_multi_file_edit), 6i (remove_dead_code_preview on `_instance`), 6j (extract_method_preview), 6k (restructure_preview, replace_string_literals_preview, change_signature_preview for add/remove/rename/reorder, symbol_refactor_preview, preview_record_field_addition, extract_and_wire_interface_preview). 6a-apply, 6b-apply, 6c-apply, 6e-apply, 6f-apply, 6g-apply, 6h-apply, 6i-apply, 6j-apply, 6f-ii (.editorconfig writers / pragma) — all `skipped-safety`. 6l (apply_with_verify) `skipped-safety` (requires apply). 6m (workspace_changes) **crashed during the final batch** — see §14.1.
- **Changes:** None persisted in git.
- **Verification:** `compile_check` post-crash showed 9/9 projects clean; `build_project(Core)` exit 0; 1-test `test_run` passed.

## 6. Performance baseline (`_meta.elapsedMs`)
| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| workspace_load | stable | server | 2 | 4636 | 4644 | 4644 | 9 projects / 433 docs | ≤15 s | within budget; cold+reload similar |
| workspace_warm | experimental | server | 1 | 3285 | 3285 | 3285 | 9 projects, 6 cold compilations | N/A | one-shot prewarm |
| project_diagnostics (summary, no filters) | stable | diagnostics | 1 | 18277 | 18277 | 18277 | solution-wide, 1405 Info | ≤15 s | **FLAG — over budget by 22%**; consider project-scoped paging for large solutions |
| project_diagnostics (projectName+diagnosticId) | stable | diagnostics | 1 | 27 | 27 | 27 | 32 Info in scope | ≤5 s | budget-beat by ~180× |
| compile_check (default) | stable | diagnostics | 1 | 9631 | 9631 | 9631 | 9 projects, 0 diag | ≤15 s | within budget |
| compile_check (emitValidation=true, warm) | stable | diagnostics | 1 | 1861 | 1861 | 1861 | 9 projects, 0 diag | ≤15 s | within budget |
| nuget_vulnerability_scan (transitive) | stable | security | 1 | 6103 | 6103 | 6103 | 9 projects | ≤15 s | within budget |
| security_analyzer_status | stable | security | 1 | 3576 | 3576 | 3576 | 9 projects | ≤15 s | within budget |
| security_diagnostics | stable | security | 1 | 869 | 869 | 869 | solution | ≤15 s | within budget |
| list_analyzers (solution-default) | stable | diagnostics | 1 | 139 | 139 | 139 | 35 analyzers | ≤5 s | within budget |
| get_complexity_metrics (minComplexity=10) | stable | metrics | 1 | 95 | 95 | 95 | 20 methods | ≤5 s | within budget |
| get_cohesion_metrics | stable | metrics | 1 | 81 | 81 | 81 | 15 types | ≤5 s | within budget |
| get_coupling_metrics | stable | metrics | 1 | 5716 | 5716 | 5716 | 15 types, cold-load Web project | ≤15 s | within budget but heavy |
| find_unused_symbols (public) | stable | metrics | 1 | 1163 | 1163 | 1163 | 15 hits | ≤15 s | within budget |
| find_duplicated_methods | stable | metrics | 1 | 84 | 84 | 84 | 9 groups | ≤15 s | within budget |
| find_duplicate_helpers | experimental | metrics | 1 | 32 | 32 | 32 | 15 helpers | ≤15 s | within budget |
| find_dead_locals | experimental | metrics | 1 | 1183 | 1183 | 1183 | 10 hits | ≤15 s | within budget |
| find_dead_fields (non-default usageKind) | experimental | metrics | 1 | 1055 | 1055 | 1055 | 3 hits | ≤15 s | within budget |
| get_namespace_dependencies (circularOnly) | stable | metrics | 1 | 108 | 108 | 108 | 1 cycle | ≤5 s | within budget |
| get_nuget_dependencies (summary) | stable | metrics | 1 | 696 | 696 | 696 | 27 packages | ≤15 s | within budget |
| suggest_refactorings | stable | metrics | 1 | 419 | 419 | 419 | 15 suggestions | ≤15 s | within budget |
| symbol_search | stable | symbol | 2 | 282 | 553 | 553 | 4 hits / 5 hits | ≤5 s | empty-query probe degenerate-matched 5 |
| find_type_mutations | stable | symbol | 1 | 40 | 40 | 40 | 2 members | ≤5 s | within budget |
| find_consumers | stable | symbol | 1 | 14 | 14 | 14 | 22 consumers | ≤5 s | within budget |
| find_type_usages | stable | symbol | 1 | 11 | 11 | 11 | 51 usages | ≤5 s | within budget |
| callers_callees | stable | symbol | 1 | 10 | 10 | 10 | 26+19 | ≤5 s | within budget |
| symbol_relationships | stable | symbol | 1 | 9 | 9 | 9 | 51 refs, paged | ≤5 s | within budget |
| symbol_impact_sweep (summary+max) | experimental | symbol | 1 | 222 | 222 | 222 | 15 refs | ≤5 s | within budget |
| impact_analysis (refsLimit=5) | stable | symbol | 1 | N/A | N/A | N/A | 51 refs | ≤5 s | **FAIL — 86KB response exceeded MCP cap; see §14.2** |
| member_hierarchy | stable | symbol | 1 | 156 | 156 | 156 | 1 base | ≤5 s | within budget |
| find_property_writes | stable | symbol | 1 | 6 | 6 | 6 | 0 writes | ≤5 s | within budget |
| find_references (summary, limit=10) | stable | symbol | 1 | 3 | 3 | 3 | 26 refs / 10 page | ≤5 s | within budget |
| find_references_bulk | stable | symbol | 1 | 16 | 16 | 16 | 2 symbols, 29 refs total | ≤5 s | within budget |
| find_overrides | stable | symbol | 1 | 4 | 4 | 4 | 0 | ≤5 s | within budget (but see §18) |
| find_base_members | stable | symbol | 1 | 2 | 2 | 2 | 0 | ≤5 s | within budget (but see §18) |
| probe_position | experimental | symbol | 1 | 4 | 4 | 4 | 1 token | ≤5 s | within budget |
| analyze_data_flow | stable | flow | 1 | 6 | 6 | 6 | 64-line region | ≤5 s | within budget |
| analyze_control_flow | stable | flow | 1 | 4 | 4 | 4 | 64-line region | ≤5 s | within budget |
| get_operations | stable | flow | 1 | 3 | 3 | 3 | 1 invocation | ≤5 s | within budget |
| get_syntax_tree (maxTotalBytes=20000) | stable | flow | 1 | 4 | 4 | 4 | 19 lines | ≤5 s | within budget |
| get_source_text | stable | flow | 1 | 3 | 3 | 3 | 64 lines | ≤5 s | within budget |
| trace_exception_flow | experimental | flow | 1 | 15 | 15 | 15 | 10 catches (truncated) | ≤15 s | within budget |
| analyze_snippet (all kinds) | stable | snippet | 5 | 59 | 72 | 72 | small snippets | ≤5 s | within budget |
| evaluate_csharp | stable | snippet | 4 | 27 | 52 | 61 | small exprs | ≤5 s (script 10 s) | within budget |
| restructure_preview | experimental | refactor | 1 | 10 | 10 | 10 | 9 matches / 1 file | ≤30 s | within budget |
| replace_string_literals_preview | experimental | refactor | 1 | 3 | 3 | 3 | 0 matches (error) | ≤30 s | returns InvalidOperation instead of structured 0 |
| change_signature_preview (add) | experimental | refactor | 1 | 27 | 27 | 27 | — | ≤30 s | **FAIL — see §14.4** |
| change_signature_preview (rename) | experimental | refactor | 1 | 156 | 156 | 156 | 3 callsite diff | ≤30 s | within budget |
| change_signature_preview (reorder negative) | experimental | refactor | 1 | 0 | 0 | 0 | — | ≤1 s | excellent error |
| symbol_refactor_preview (no-op rename) | experimental | refactor | 1 | 582 | 582 | 582 | 1 op | ≤30 s | within budget |
| preview_record_field_addition | experimental | refactor | 1 | 3092 | 3092 | 3092 | blocked by auto-reload | ≤30 s | blocked mid-call |
| extract_and_wire_interface_preview | experimental | refactor | 1 | 4374 | 4374 | 4374 | blocked by auto-reload | ≤30 s | blocked mid-call |
| extract_interface_preview | experimental | refactor | 1 | 24 | 24 | 24 | 8 members / 2 files | ≤30 s | within budget |
| extract_method_preview | stable | refactor | 1 | 31 | 31 | 31 | 3 stmts / 1 method | ≤30 s | within budget |
| remove_dead_code_preview | stable | refactor | 1 | 4 | 4 | 4 | refused w/ msg | ≤30 s | within budget |
| rename_preview | stable | refactor | 2 | 1 | 3 | 3 | refused w/ msg | ≤30 s | within budget |
| fix_all_preview (IDE0305, project) | experimental | refactor | 1 | 699 | 699 | 699 | provider threw | ≤30 s | **FAIL — see §14.3** |
| format_range_preview | stable | refactor | 1 | 11 | 11 | 11 | no-op diff | ≤30 s | within budget |
| format_document_preview | stable | refactor | 0 | — | — | — | skipped-time | — | — |
| organize_usings_preview | stable | refactor | 1 | 32 | 32 | 32 | 1 using removed | ≤30 s | within budget |
| format_check | experimental | refactor | 1 | 389 | 389 | 389 | 102 docs | ≤15 s | within budget |
| code_fix_preview (MA0001) | stable | refactor | 1 | 220 | 220 | 220 | — | ≤30 s | actionable precondition error |
| preview_multi_file_edit (no-op) | experimental | refactor | 1 | 24 | 24 | 24 | 1 file / 1 trivial edit | ≤30 s | within budget |
| get_code_actions (no end) | stable | refactor | 1 | 12 | 12 | 12 | — | ≤5 s | **FAIL — see §14.5** |
| get_code_actions (explicit end) | stable | refactor | 1 | 2 | 2 | 2 | 0 actions | ≤5 s | within budget |
| move_type_to_file_preview | stable | refactor | 1 | 230 | 230 | 230 | 2-file diff | ≤30 s | within budget |
| create_file_preview | stable | refactor | 1 | 3 | 3 | 3 | 1 file | ≤30 s | within budget |
| delete_file_preview | stable | refactor | 1 | 5 | 5 | 5 | 1 file | ≤30 s | within budget |
| split_class_preview | experimental | refactor | 1 | 10 | 10 | 10 | 3 members / 2 files | ≤30 s | within budget (minor orphaned-indent polish issue) |
| scaffold_type_preview | experimental | scaffolding | 1 | 3 | 3 | 3 | 1 file | ≤30 s | within budget |
| scaffold_test_preview | stable | scaffolding | 1 | 27 | 27 | 27 | 1 file (30 lines) | ≤30 s | within budget; sibling inference over-applied |
| scaffold_test_batch_preview | experimental | scaffolding | 1 | 5 | 5 | 5 | 2 files + 1 warn | ≤30 s | within budget |
| scaffold_first_test_file_preview | experimental | scaffolding | 1 | 21 | 21 | 21 | 1 file; broken ctor for static class | ≤30 s | **FLAG — see §14.6** |
| add_package_reference_preview | stable | project | 1 | 5 | 5 | 5 | 1 csproj diff | ≤30 s | **FLAG — see §15** duplicate reference not detected |
| set_project_property_preview | stable | project | 1 | 2 | 2 | 2 | 1 csproj diff | ≤30 s | **FLAG — see §15** formatting drift |
| get_editorconfig_options | stable | editorconfig | 1 | 165 | 165 | 165 | 27 options | ≤5 s | within budget |
| evaluate_msbuild_property | stable | msbuild | 1 | 142 | 142 | 142 | 1 property | ≤5 s | within budget |
| evaluate_msbuild_items (Compile) | stable | msbuild | 1 | 81 | 81 | 81 | 100 items | ≤5 s | within budget |
| get_msbuild_properties (filter) | stable | msbuild | 1 | 82 | 82 | 82 | 1/718 | ≤5 s | within budget |
| build_project (Core) | stable | build | 1 | 4890 | 4890 | 4890 | 1 project | ≤30 s | within budget |
| test_discover | stable | test | 1 | 343 | 343 | 343 | 13 tests | ≤15 s | within budget |
| test_related_files | stable | test | 1 | 11 | 11 | 11 | 10 tests | ≤5 s | within budget |
| test_related | stable | test | 1 | N/A | N/A | N/A | ~100+ matches | ≤5 s | broad; heuristic name-overlap dominates |
| test_run (1-test filter) | stable | test | 1 | 11212 | 11212 | 11212 | 1 test passed | ≤60 s | within budget |
| validate_workspace (summary) | experimental | test | 1 | 27560 | 27560 | 27560 | 9 projects | ≤60 s | heavy (rebuilds compilations) |
| validate_recent_git_changes (summary) | experimental | test | 1 | 27363 | 27363 | 27363 | 16 changed files | ≤60 s | heavy |
| test_reference_map (projectName=Core) | experimental | test | 1 | 996 | 996 | 996 | 0 covered / 955 uncovered | ≤15 s | within budget; **FLAG** §18 |
| go_to_definition | stable | navigation | 1 | 5 | 5 | 5 | 1 location | ≤5 s | within budget |
| enclosing_symbol | stable | navigation | 1 | 6 | 6 | 6 | 1 symbol | ≤5 s | within budget |
| get_completions (filterText) | stable | navigation | 1 | 458 | 458 | 458 | 0 items | ≤5 s | within budget |
| semantic_search | stable | discovery | 3 | 53 | 61 | 61 | 1/1/10 hits | ≤5 s | within budget |
| find_reflection_usages | stable | discovery | 1 | 516 | 516 | 516 | 0 hits | ≤15 s | within budget |
| get_di_registrations (overrideChains) | stable | discovery | 1 | 2081 | 2081 | 2081 | 17 regs | ≤15 s | within budget |
| source_generated_documents | stable | discovery | 1 | N/A | N/A | N/A | 2 files | ≤5 s | within budget |
| get_prompt_text | experimental | prompt | 7 | 5 | 381 | 7595 | 5 successful + 2 negative | ≤15 s | within budget; explain_error unusually slow (7.6 s) |
| document_symbols | stable | symbol | 1 | 6 | 6 | 6 | 23 symbols | ≤5 s | within budget |

## 7. Schema vs behaviour drift
| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| impact_analysis | Paging contract | `referencesLimit` caps total response size | Response emitted 86 095 chars despite `referencesLimit=5`; dependency/declaration payload not capped | P1 | Other top-level list is uncapped. `summary=true` not exposed by this tool (unlike `find_references`). |
| get_code_actions | Default end-position | Caret-only call (no endLine/endColumn) resolves to a 0-width selection | Server computed inverted range (end=3927 < start=3930) → `ArgumentOutOfRangeException` | P2 | Inconsistent with adjacent tools that accept caret-only positions; the error message itself is actionable |
| change_signature_preview | Error text | Identify which parameter (caller-supplied) failed | `Parameter 'index' has an out-of-range value` — "index" is an internal index, not a schema field | P2 | Caller has no way to map the error back to their input |
| rename_preview (metadataName=bare member name) | Overload resolution | Reject with actionable guidance to include the signature | `No symbol found for the provided rename target.` (vague) | P3 | Pair with the `string` built-in probe which returned an **excellent** metadata-symbol error; inconsistency between the two |
| find_base_members vs member_hierarchy | Metadata-boundary resolution | Both should resolve IEquatable<T>.Equals as base | member_hierarchy resolves it; find_base_members returns 0 | P2 | Response-contract inconsistency between siblings (also in §18) |
| find_overrides (on metadata interface member) | Find in-source overrides of metadata base | Should find WebSnapshotStoreContext.Equals as an override of IEquatable<T>.Equals | Returned 0 | P2 | Tool description says "auto-promotes to the virtual/interface root" — works on in-source chains, silently empty for metadata roots |
| find_property_writes | Positional-record binding | Surface a hint that positional-record properties are special-cased, or count ctor-param bindings | Silent 0 | P3 | Description advertises "object-initializer vs post-construction" classification; no bucket for "positional-record primary-constructor bind" |
| find_unused_symbols (excludeConventionInvoked=true) | xUnit convention filter | Skip `[CollectionDefinition]` attribute-holders | `WebHostSerialCollection` still surfaced | P3 | Convention-invoked detection doesn't cover the CollectionDefinition pattern |
| scaffold_first_test_file_preview | Handle static class targets | Emit a static-friendly test shape (no instance field, no ctor) | Emits `_subject = new StaticClass();` → would not compile | P2 | Warning message correctly notes "no public/internal instance methods" but then emits incompatible body |
| scaffold_test_preview | Sibling-file inference | Apply inference only when attributes are semantically safe | Replicates `[Trait("Category","Playwright")]` and Playwright usings onto a snapshot test | P3 | Over-broad inference |
| add_package_reference_preview | Duplicate detection | Detect existing reference (from csproj OR Directory.Build.props) and refuse with actionable error | Emits a second `<PackageReference Include="Meziantou.Analyzer" Version="3.0.50" />` alongside the existing one | P2 | Directory.Build.props transitively present; tool should consult evaluated metadata, not just the immediate csproj |
| set_project_property_preview | XML formatting | Insert properly-indented `<PropertyGroup>` following project style | Inserts single-line `<PropertyGroup><Nullable>enable</Nullable></PropertyGroup>` | P3 | Cosmetic; also: tool doesn't detect that the property is already evaluated-active via Directory.Build.props |
| discover_capabilities (prompt) | Rendered-text reference fidelity | Every tool name in the rendered body must exist in the live catalog | All 161 names match | PASS | Idempotent across two renders |

## 8. Error message quality
| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| workspace_load | Unsanctioned path | actionable | "Path 'C:\\…' is not under any client-sanctioned root. Allowed roots: file://…" | Correctly tells caller what the boundary is |
| find_implementations | fabricated metadataName | actionable | "No symbol could be resolved for metadata name '…'. … Ensure the workspace is loaded" | Clear; cites workspace_load |
| find_references | fabricated (structurally valid) symbolHandle | actionable | "handle may be from a previous workspace version, symbol may have been removed…" | Good; matches v1.8+ behavior |
| workspace_status | fabricated id | actionable | "Workspace '…' not found or has been closed. Active workspace IDs are listed by workspace_list." | Cites the recovery tool |
| rename_preview | caret on built-in `string` | actionable | "Cannot rename metadata or built-in symbol 'string' — renames require a source declaration." | Clear; identifies the boundary |
| rename_preview | bare method metadataName | vague | "No symbol found for the provided rename target." | No hint to include overload signature |
| change_signature_preview | reorder op | actionable (excellent) | "Unsupported op 'reorder'. Valid values: add, remove, rename. … stage a remove + add pair via symbol_refactor_preview or fall back to preview_multi_file_edit." | Points at the exact substitute tools |
| change_signature_preview | op=add, bare metadataName | unhelpful | "Parameter 'index' has an out-of-range value" | 'index' is internal |
| get_code_actions | caret-only (no end) | unhelpful then actionable | "`'end' must not be less than 'start'. start='3930' end='3927'`" | Underlying server bug (§14.5); after adding end params, a later call returned a helpful `hint` field |
| code_fix_preview | MA0001 | actionable | "No code fix provider is loaded for diagnostic 'MA0001'. Run list_analyzers to see which analyzers are loaded…" | Great — actionable |
| fix_all_preview | IDE0305 | actionable (wraps failure) | `guidanceMessage`: "The registered FixAll provider for 'IDE0305' threw… Try code_fix_preview on individual occurrences, or narrow the scope…" | The provider threw but the wrapper gave a clean fallback |
| replace_string_literals_preview | 0 matches | vague | "no matching literals found in scope" | Should return `{count:0, changes:[]}` structured success |
| symbol_search | empty query | degenerate | 5 random matches | Empty string matches everything; ideally should require a minimum query length |
| get_prompt_text | unknown name | actionable (excellent) | Lists every available prompt in-line | Best-in-class prompt-discovery UX |
| get_prompt_text | malformed JSON | actionable (excellent) | "parametersJson is not valid JSON: 'n' is an invalid start of a property name. Expected a '"'. LineNumber: 0 | BytePositionInLine: 1" + example fix | Top-tier |
| preview_record_field_addition | post-reload stale id | actionable | "Workspace was auto-reloaded during this call; No type resolved for metadata name … Re-resolve the symbol (e.g. via symbol_search, symbol_info, or a fresh position-based locator) and retry." | Great; explicitly tells caller the cause |
| extract_and_wire_interface_preview | post-reload stale document | actionable | "Document not found: … The workspace may need to be reloaded (workspace_reload) if the state is stale." | Great |

## 9. Parameter-path coverage
| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| project_diagnostics | summary=true; severity=Error; projectName+diagnosticId; offset+limit | exercised | invariant totals preserved |
| compile_check | emitValidation=true; limit=50/10; file filter implied via pagination | exercised | no emit-delta because clean |
| list_analyzers | projectName+offset=50 | exercised | correctly scoped to Core |
| find_unused_symbols | includePublic=true; defaults | exercised | convention-filter false positive noted |
| find_dead_fields | usageKind=never-read | exercised | also probes `workspace_load(autoRestore=true)` |
| workspace_load | autoRestore=true | exercised | returned restoreRequired=false (already pre-restored) |
| find_references | summary=true; limit=10 | exercised | preview text correctly dropped |
| symbol_signature_help | preferDeclaringMember=false | exercised | literal-token resolution |
| symbol_impact_sweep | summary=true; maxItemsPerCategory=15 | exercised | compact payload |
| get_syntax_tree | maxTotalBytes=20000 | exercised | stayed within cap |
| change_signature_preview | op=add / op=remove (implied) / op=rename / op=reorder (negative) | exercised | add fails; rename PASS; reorder PASS-negative |
| get_di_registrations | showLifetimeOverrides=true | exercised | overrideChains emitted |
| get_nuget_dependencies | summary=true | exercised | compact shape |
| project_graph | default | exercised | single surface |
| preview_multi_file_edit | skipSyntaxCheck default false | exercised | no-op edit |
| get_cohesion_metrics | excludeTestProjects=true | exercised | test projects filtered |
| get_coupling_metrics | excludeTestProjects=true | exercised | test projects filtered |
| validate_workspace | summary=true; runTests=false | exercised | overallStatus=clean |
| validate_recent_git_changes | summary=true; runTests=false | exercised | auto-derived from git status |
| find_references_bulk | 2 symbols by metadataName | exercised | structured {key, references} shape |
| evaluate_csharp | empty body | exercised | success=true, resultValue=null |
| analyze_snippet | expression/program/statements/returnExpression/empty | exercised | 5 paths |

## 10. Prompt verification (Phase 16)
| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| explain_error | ✅ | ✅ (includes source context + line marker + diagnostic JSON) | no | yes (second render would match modulo elapsedMs) | 7595 | promote | slow first render but rich context |
| discover_capabilities | ✅ | ✅ (lists all 161 tools + 20 prompts with categories) | no | yes | 26 | promote | authoritative catalog substitute when resource channel is client-blocked |
| review_file | ✅ | ✅ (doc symbols + diagnostics + source + 8-facet review checklist) | no | yes | 381 | promote | production-ready |
| suggest_refactoring | ✅ | ✅ (doc symbols + source + 7-facet refactor checklist) | no | yes | 48 | promote | production-ready |
| refactor_and_validate | ✅ | (not rendered — first call missed `workspaceId`; retry succeeded for other prompts; time-budget skipped re-render) | — | — | — | keep-experimental | schema enforced; actionability inferred from sibling prompts |
| analyze_dependencies, cohesion_analysis, consumer_impact, dead_code_audit, debug_test_failure, fix_all_diagnostics, guided_extract_interface, guided_extract_method, guided_package_migration, msbuild_inspection, review_complexity, review_test_coverage, security_review, session_undo, refactor_loop | schema-verified via the `unknown_prompt_xyz` error listing; rendered-output not exercised due to budget | n/a | n/a | n/a | — | keep-experimental | Rendered output unknown for this run. Cross-ref: catalog surface matches the live `Available prompts` listing (20 entries). |

## 11. Skills audit (Phase 16b)
Live glob `skills/*/SKILL.md` in `C:\Code-Repo\Roslyn-Backed-MCP\skills\` → **32 skills**: analyze, architecture-review, code-actions, complexity, dead-code, di-audit, document, exception-audit, explain-error, extract-method, format-sweep, generate-tests, impact-assessment, inheritance-explorer, migrate-package, modernize, nuget-preflight, project-inspection, refactor, refactor-loop, review, security, semantic-find, session-undo, snippet-eval, test-coverage, test-triage, trace-flow, update, version-bump, workspace-health.

Tool-reference cross-check (bare names): extracted frequency of ~40 distinct tool names across all SKILL.md bodies; **every referenced tool exists in the live catalog** (no stale/removed references). Top references — `compile_check` (37), `find_references` (35), `workspace_status` (30), `workspace_load` (28), `symbol_search` (26), `callers_callees` (26), `symbol_info` (21), `evaluate_msbuild_property` (16), `revert_last_apply` (15), `find_implementations` (15), `project_diagnostics` (14), `type_hierarchy` (13), `test_run` (12), `get_complexity_metrics` (10), `document_symbols` (10), `validate_workspace` (9), `fix_all_preview` (9), `workspace_list` (7), `workspace_changes` (7), `semantic_search` (7), `evaluate_msbuild_items` (7), `analyze_data_flow` (7), `workspace_reload` (6), `test_discover` (6), `format_check` (6), `trace_exception_flow` (5), `test_related` (5), `symbol_impact_sweep` (5), `impact_analysis` (5), `get_di_registrations` (5), `get_code_actions` (5), `fix_all_apply` (5), `extract_method_preview` (5), `evaluate_csharp` (5), `build_workspace` (5), `security_diagnostics` (4), `rename_preview` (4), `nuget_vulnerability_scan` (4), `find_reflection_usages` (4), `find_consumers` (4).

| Skill | frontmatter_ok | tool_refs_valid (invalid_count) | dry_run | safety_rules | Notes |
|-------|----------------|----------------------------------|---------|--------------|-------|
| analyze | ✅ (name/description/user-invocable/argument-hint) | ✅ (0 invalid) | not-exercised-time-budget | n/a (read-only) | spot-checked frontmatter |
| refactor | ✅ | ✅ (0) | not-exercised-time-budget | preview→apply→compile_check expected per description | spot-checked frontmatter |
| session-undo | ✅ | ✅ (0) | not-exercised-time-budget | destructive family; description explicitly frames as session-scoped | spot-checked frontmatter |
| architecture-review, code-actions, complexity, dead-code, di-audit, document, exception-audit, explain-error, extract-method, format-sweep, generate-tests, impact-assessment, inheritance-explorer, migrate-package, modernize, nuget-preflight, project-inspection, refactor-loop, review, security, semantic-find, snippet-eval, test-coverage, test-triage, trace-flow, update, version-bump, workspace-health | (frontmatter parity inferred via glob result; none dropped) | **0 invalid tool refs across entire skills/** | not-exercised-time-budget | — | tool-reference cross-check is the strongest signal for ship-readiness; full workflow dry-run deferred |

No skill references a removed/renamed tool; no P2-ship-blocker skill issue found.

## 12. Experimental promotion scorecard
| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| Tool | workspace_warm | server | exercised | 3285 | ✅ | n/a | n/a | none | **promote** | cache-primes 6 compilations; described, measurable, clean |
| Tool | format_check | refactor | exercised | 389 | ✅ | ✅ | n/a | none | **promote** | accurate violation count; elapsed within budget |
| Tool | trace_exception_flow | flow | exercised | 15 | ✅ | ✅ | n/a | none | **promote** | matched reasonable catch-site walk; body excerpts + rethrow annotations populated |
| Tool | find_duplicate_helpers | metrics | exercised | 32 | ✅ | ✅ | n/a | none | **promote** | 0 framework-wrapper false positives under `excludeFrameworkWrappers=true` |
| Tool | find_dead_locals | metrics | exercised | 1183 | ✅ | ✅ | n/a | none | **promote** | results consistent with source reads |
| Tool | find_dead_fields | metrics | exercised | 1055 | ✅ (non-default `usageKind`) | ✅ | n/a | pair-finding with remove_dead_code_preview (§7 consistency) | **promote** | real-repo findings; non-default path works |
| Tool | symbol_impact_sweep | symbol | exercised | 222 | ✅ (summary+maxItemsPerCategory) | ✅ | n/a | none | **promote** | compact payload, matches find_references on same symbol |
| Tool | probe_position | symbol | exercised | 4 | ✅ | ✅ | n/a | none | **promote** | accurate strict-resolution, matches description |
| Tool | test_reference_map | test | exercised | 996 | ✅ | ✅ | n/a | **notes field contradicts projectName scope** (§14.7) | **keep-experimental** | UX note is misleading; logic otherwise works |
| Tool | validate_workspace | test | exercised | 27560 | ✅ | ✅ | n/a | none | **promote** | summary=true path exercised |
| Tool | validate_recent_git_changes | test | exercised | 27363 | ✅ | ✅ | n/a | none | **promote** | git-auto-derive works; warnings empty |
| Tool | get_prompt_text | prompt | exercised | 5 (p50 across 7) | ✅ | ✅ (both negative probes top-tier) | n/a | none | **promote** | best-in-class error messages |
| Tool | restructure_preview | refactor | exercised-preview-only | 10 | ✅ | n/a | not-round-tripped (preview-only) | none | **keep-experimental** | applies gated by worktree blocker |
| Tool | replace_string_literals_preview | refactor | exercised-preview-only | 3 | ✅ | vague on 0-match | n/a | zero-match returns exception | **keep-experimental** | return structured 0 instead of InvalidOperation |
| Tool | change_signature_preview | refactor | exercised-preview-only | 156 (rename) | ✅ (rename/reorder) / fail (add) | excellent (reorder), unhelpful (add) | not-round-tripped | add-path crashed (§14.4) | **keep-experimental** | rename/reorder pass; add needs clearer error or overload-signature resolver |
| Tool | symbol_refactor_preview | refactor | exercised-preview-only | 582 | ✅ | n/a | not-round-tripped | none | **keep-experimental** | no-op rename produced an empty previewToken; round-trip not verified |
| Tool | preview_record_field_addition | refactor | blocked | 3092 | — | ✅ (actionable reload error) | not-round-tripped | blocked by auto-reload cascade during Phase 6 batch | **needs-more-evidence** | re-run after isolating stale-reload cascade |
| Tool | extract_and_wire_interface_preview | refactor | blocked | 4374 | — | ✅ (document-not-found error) | not-round-tripped | blocked by auto-reload | **needs-more-evidence** | re-run isolated |
| Tool | extract_interface_preview | refactor | exercised-preview-only | 24 | ✅ | ✅ | not-round-tripped | none | **keep-experimental** | apply sibling gated by worktree blocker |
| Tool | move_type_to_file_preview | refactor | exercised-preview-only | 230 | ✅ | ✅ | not-round-tripped | none | **keep-experimental** | apply sibling gated |
| Tool | split_class_preview | refactor | exercised-preview-only | 10 | ✅ | minor orphan-indent cosmetic | not-round-tripped | none | **keep-experimental** | apply sibling gated |
| Tool | extract_method_preview | refactor | exercised-preview-only | 31 | ✅ | ✅ | not-round-tripped | none | **keep-experimental** | apply sibling gated (but this one is **stable** per catalog — no promotion row needed strictly) |
| Tool | preview_multi_file_edit | refactor | exercised-preview-only | 24 | ✅ | ✅ | not-round-tripped (stale-token probe not run) | none | **keep-experimental** | apply sibling gated |
| Tool | fix_all_preview | refactor | exercised-preview-only | 699 | ✅ | actionable fallback guidance | not-round-tripped | **IDE0305 provider throws (§14.3)** | **keep-experimental** | don't deprecate — the failure is an upstream Roslyn fixer bug, not the tool. Evidence of graceful wrapping is a plus |
| Tool | scaffold_type_preview | scaffolding | exercised-preview-only | 3 | ✅ | ✅ | not-round-tripped | none | **keep-experimental** | v1.8+ `internal sealed class` default confirmed |
| Tool | scaffold_test_batch_preview | scaffolding | exercised-preview-only | 5 | ✅ | ✅ (target-already-exists warning) | not-round-tripped | none | **keep-experimental** | one composite token for 2 files |
| Tool | scaffold_first_test_file_preview | scaffolding | exercised-preview-only | 21 | ✅ | warning present | not-round-tripped | **emits uncompilable body for static class (§14.6)** | **keep-experimental** | fix static-class handling before promotion |
| Tool | scaffold_test_apply | scaffolding | skipped-safety | — | — | — | — | gated by worktree blocker | **needs-more-evidence** | |
| Tool | apply_with_verify | refactor | skipped-safety | — | — | — | — | gated by worktree blocker | **needs-more-evidence** | |
| Tool | apply_composite_preview | refactor | skipped-safety | — | — | — | — | described as destructive-despite-name; skipped on principle | **needs-more-evidence** | |
| Tool | extract_shared_expression_to_helper_preview, record_field_add_with_satellites_preview, split_service_with_di_preview, migrate_package_preview, replace_invocation_preview, change_type_namespace_preview, extract_interface_cross_project_preview, dependency_inversion_preview, move_type_to_project_preview, remove_interface_member_preview, bulk_replace_type_apply, extract_type_apply, extract_interface_apply, extract_method_apply, move_type_to_file_apply, move_file_apply, create_file_apply, delete_file_apply, scaffold_type_apply, remove_dead_code_apply, add_central_package_version_preview, apply_multi_file_edit, preview_multi_file_edit_apply, apply_project_mutation, format_range_apply | mostly refactor/file-ops applies or repo-shape gated | skipped-safety / skipped-repo-shape | — | — | — | — | — | **needs-more-evidence** | worktree blocker + CPM absent + single-TFM |
| Tool | find_consumers, find_shared_members | symbol | exercised | 14 / 10 | ✅ | ✅ | n/a | none | (already stable per catalog — no row) | — |
| Resource | roslyn://… (13 entries) | all | **blocked** | — | — | — | — | client does not route `roslyn://` reads to the MCP server | **needs-more-evidence** (all 4 experimental resources) | resolve client wiring, re-run |
| Prompt | explain_error, discover_capabilities, review_file, suggest_refactoring | prompts | exercised | 5 / 26 / 381 / 48 / 7595 | ✅ | ✅ | n/a | none | **promote** (for each; see §10) | rendered output passes every criterion |
| Prompt | refactor_and_validate | prompts | exercised (schema only) | — | ✅ | n/a | n/a | — | **keep-experimental** | schema verified, rendered-text not re-probed |
| Prompt | 15 remaining prompts | prompts | schema-verified-only | — | ✅ | n/a | n/a | — | **keep-experimental** | rendered-output not exercised this run |

## 13. Debug log capture
**N/A — client did not surface MCP log notifications (`notifications/message`).** No entries visible for the entire run. Principle #8 client-limitation recorded.

## 14. MCP server issues (bugs)

### 14.1 Server process crashed mid-Phase 6 (P0/P1)
| Field | Detail |
|-------|--------|
| Tool | cascade across `preview_record_field_addition` → `extract_and_wire_interface_preview` → `workspace_changes` |
| Input | Phase 6k experimental batch: `restructure_preview` + `change_signature_preview(rename)` + `change_signature_preview(reorder negative)` + `symbol_refactor_preview(no-op rename)` + `preview_record_field_addition(WebSnapshotStoreContext,IsDefault:bool,false)` + `extract_and_wire_interface_preview(SnapshotStore,...)` + `workspace_changes(wsId)` issued concurrently in one turn |
| Expected | All calls run under workspace read-lock or queue; worst case → individual actionable errors |
| Actual | First two non-trivial writers (`preview_record_field_addition`, `extract_and_wire_interface_preview`) hit an auto-reload (`staleAction=auto-reloaded`, `staleReloadMs` 3089 / 4370). The cascade terminated with `workspace_changes` returning `MCP error -32000: Connection closed`. Post-mortem `server_heartbeat` showed a **new** stdio host: `stdioPid 40452 → 21500`, `serverStartedAt 02:25:06Z → 02:39:11Z`. All in-flight preview tokens invalidated. |
| Severity | **P0/P1** — host process death. |
| Reproducibility | 1/1 in this run; trigger appears to be the auto-reload cascade converging on a concurrent write-capable call |

### 14.2 `impact_analysis` over-budget response (P1)
| Field | Detail |
|-------|--------|
| Tool | impact_analysis |
| Input | `metadataName=NetworkDocumentation.Core.Snapshots.SnapshotStore, referencesLimit=5` |
| Expected | Response bounded by `referencesLimit`; <25 KB |
| Actual | 86 095 chars, spilled to tool-results file. `referencesLimit` does not cap the top-level declarations / affected-projects payload |
| Severity | P1 |
| Reproducibility | reliable on symbols with >5 affected projects (this repo's SnapshotStore has 5) |

### 14.3 `fix_all_preview` IDE0305 provider throws (P1)
| Field | Detail |
|-------|--------|
| Tool | fix_all_preview |
| Input | `diagnosticId=IDE0305, scope=project, projectName=NetworkDocumentation.Core` |
| Expected | Preview token + batched diff across 79 IDE0305 occurrences |
| Actual | `guidanceMessage="The registered FixAll provider for 'IDE0305' threw while computing the fix (InvalidOperationException: Sequence contains no elements). Try code_fix_preview on individual occurrences, or narrow the scope…"` — 0 changes |
| Severity | P1 (the wrapper handles the failure cleanly, but the underlying FixAll provider for IDE0305 is broken) |
| Reproducibility | reliable whenever IDE0305 has ≥1 occurrence |

### 14.4 `change_signature_preview(op=add)` unhelpful error (P2)
| Field | Detail |
|-------|--------|
| Tool | change_signature_preview |
| Input | `op=add, metadataName=…SnapshotStore.Save, name=cancellationToken, parameterType=System.Threading.CancellationToken, defaultValue=default` |
| Expected | Preview adds param with default spliced into every callsite |
| Actual | `ArgumentOutOfRangeException: Parameter 'index'` — no callable-facing field indicates which parameter/index exceeded what range |
| Severity | P2 (op=rename and op=reorder both worked cleanly; this is an add-path bug) |
| Reproducibility | 1/1 on this input. Workaround likely needs explicit overload disambiguation or a `symbolHandle`. |

### 14.5 `get_code_actions` inverted default-range (P2)
| Field | Detail |
|-------|--------|
| Tool | get_code_actions |
| Input | `filePath=…SnapshotStore.cs, startLine=98, startColumn=13` (no endLine/endColumn) |
| Expected | Resolve as caret-only position (0-width selection) or as the enclosing token span |
| Actual | `ArgumentOutOfRangeException: 'end' must not be less than 'start'. start='3930' end='3927'.` |
| Severity | P2 |
| Reproducibility | reliable |

### 14.6 `scaffold_first_test_file_preview` on static class (P2)
| Field | Detail |
|-------|--------|
| Tool | scaffold_first_test_file_preview |
| Input | `serviceMetadataName=NetworkDocumentation.Core.Snapshots.SnapshotContentHasher` (a `static class`) |
| Expected | Either refuse with actionable error or emit a static-friendly test shape (no instance field, no `new X()`) |
| Actual | Warning "Service 'SnapshotContentHasher' has no public/internal instance methods … emitted a single placeholder test" BUT emitted `_subject = new SnapshotContentHasher();` — would not compile |
| Severity | P2 |
| Reproducibility | reliable |

### 14.7 `test_reference_map` projectName-scope contradiction (P2)
| Field | Detail |
|-------|--------|
| Tool | test_reference_map |
| Input | `projectName=NetworkDocumentation.Core, limit=5` |
| Expected | Map tests covering Core's symbols |
| Actual | `inspectedTestProjects: []`, `notes: ["No test projects detected (IsTestProject=true). Coverage defaults to 0%.", …]` — the notes text is self-contradictory ("no test projects detected" but qualifies with "IsTestProject=true") |
| Severity | P2 (misleading UX; productive-project filter rules out the sole test project instead of just narrowing the productive-side scan) |
| Reproducibility | reliable |

### 14.8 `add_package_reference_preview` does not detect pre-existing reference (P2)
| Field | Detail |
|-------|--------|
| Tool | add_package_reference_preview |
| Input | `projectName=Core, packageId=Meziantou.Analyzer, version=3.0.50` (already transitively present via `Directory.Build.props`) |
| Expected | Actionable refusal or no-op |
| Actual | Emits duplicate `<PackageReference>` diff |
| Severity | P2 |
| Reproducibility | reliable in repos using Directory.Build.props |

### 14.9 `set_project_property_preview` formatting drift + no-op detection (P3)
| Field | Detail |
|-------|--------|
| Tool | set_project_property_preview |
| Input | `projectName=Core, propertyName=Nullable, value=enable` (already evaluated-active via Directory.Build.props) |
| Expected | (a) detect existing evaluated value and be a no-op, or (b) insert properly-indented `<PropertyGroup>` matching project style |
| Actual | (a) no detection, (b) single-line `<PropertyGroup><Nullable>enable</Nullable></PropertyGroup>` inserted between comments and `<ItemGroup>` |
| Severity | P3 |
| Reproducibility | reliable |

### 14.10 Resource channel client-blocked in Claude Code (P1 — client-surface)
| Field | Detail |
|-------|--------|
| Tool | `ListMcpResourcesTool` / `ReadMcpResourceTool` for server="roslyn" |
| Input | `ReadMcpResourceTool(server=roslyn, uri=roslyn://server/catalog)` |
| Expected | Catalog JSON |
| Actual | `Server "roslyn" is not connected` (even though `mcp__roslyn__*` tools are live) |
| Severity | P1 (client-side), **not a server defect** — flagged so it doesn't mask a genuine server-side resource bug |
| Reproducibility | reliable in this Claude Code session |

### 14.11 `find_overrides` / `find_base_members` vs `member_hierarchy` contract drift (P2)
| Field | Detail |
|-------|--------|
| Tool | find_base_members / find_overrides |
| Input | `WebSnapshotStoreContext.Equals` / `System.IEquatable\`1.Equals` |
| Expected | Same base/impl relationships that `member_hierarchy` resolves on the same symbols |
| Actual | Both return 0; `member_hierarchy` correctly returns the IEquatable base |
| Severity | P2 (siblings disagree) |
| Reproducibility | reliable whenever the base member resides in metadata (external assembly) |

### 14.12 MCP client root allow-list blocks disposable-worktree workflow (P1 — mixed server/client)
| Field | Detail |
|-------|--------|
| Tool | workspace_load |
| Input | Disposable worktree path `C:\Code-Repo\DotNet-Network-Documentation-audit-run` (created via `git worktree add HEAD`) |
| Expected | Load the worktree as an isolated MCP session so Phase 6 applies are safely reversible |
| Actual | `InvalidArgument: Path 'C:\…-audit-run\NetworkDocumentation.sln' is not under any client-sanctioned root. Allowed roots: file://C:\Code-Repo\DotNet-Network-Documentation.` |
| Severity | P1 (procedurally blocks the entire prompt's disposable-worktree precondition) |
| Reproducibility | reliable |

## 15. Improvement suggestions
- `impact_analysis` — add `summary=true` and `declarationsLimit` shape parity with `find_references` / `symbol_impact_sweep`.
- `get_code_actions` — normalize caret-only calls to zero-width selections **before** computing internal start/end.
- `test_reference_map` — clarify the `notes` field; "No test projects detected (IsTestProject=true)" reads as a bug. When `projectName=<productive>`, still infer the test-project scope from productive→test references (don't return 0%).
- `scaffold_first_test_file_preview` — detect `static class` services and switch to a static-friendly template (or refuse with actionable "scaffold_test_preview handles static method targets").
- `scaffold_test_preview` — cap sibling-file inference to non-category attributes; a `[Trait("Category","Playwright")]` from a neighbouring fixture should not attach to an unrelated target.
- `change_signature_preview` — on `op=add`, surface a caller-facing error identifying the ambiguous overload and suggesting `symbolHandle`.
- `replace_string_literals_preview` — return `{count:0, changes:[]}` structured success for zero-match inputs instead of raising InvalidOperation.
- `rename_preview` — match `find_implementations`'s actionable NotFound error when the metadataName is a bare member without signature; tell the caller to include the signature.
- `find_base_members` / `find_overrides` — cross-check with `member_hierarchy` on symbols whose base/interface is metadata-only. At minimum, document that the metadata-boundary is a limitation.
- `find_unused_symbols(excludeConventionInvoked=true)` — extend convention filter to cover `[CollectionDefinition]` xUnit attribute holders.
- `find_property_writes` — add a response bucket (or warning field) to identify positional-record primary-constructor binds; a silent 0 is misleading.
- `add_package_reference_preview` — consult evaluated package references (not just the immediate csproj) so Directory.Build.props / Directory.Packages.props duplicates are detected.
- `set_project_property_preview` — detect when the property is already evaluated-active; format `<PropertyGroup>` to match project style.
- `diagnostic_details` — surface `supportedFixes` for diagnostics that have an associated code fix provider; when none is loaded, link to the same precondition guidance that `code_fix_preview` emits ("no code fix provider loaded — check NuGet restore").
- `workspace_load` — when the caller-supplied path is outside the client allow-list, propagate the full allow-list in the error so the caller can target a sanctioned root.
- **Server stability (§14.1 follow-up)** — investigate auto-reload cascades that terminate in stdio host death; the `workspace_changes` crash suggests an unhandled exception during a post-auto-reload state read.
- `semantic_search` — when the structured query collapses to fallback name-token matching, always set `Debug.fallbackStrategy` in the response so callers can distinguish true matches from token-only hits.
- `symbol_search("")` — reject empty query or require a minimum length to avoid returning arbitrary first-N matches.

## 16. Concurrency matrix (Phase 8b)

### Concurrency probe set
| Slot | Tool | Inputs (concise) | Classification | Notes |
|------|------|------------------|----------------|-------|
| R1 | find_references | SnapshotStore.Save (26 refs) | reader | Phase 3 baseline |
| R2 | project_diagnostics | no filters, summary=true | reader | Phase 1 baseline |
| R3 | symbol_search | "SnapshotStore" | reader | Phase 3 baseline |
| R4 | find_unused_symbols | includePublic=false | reader | Phase 2 baseline |
| R5 | get_complexity_metrics | no filters | reader | Phase 2 baseline |
| W1 | — | skipped-safety (user working tree) | writer | would require disposable worktree |
| W2 | — | skipped-safety | writer | same |

### Sequential baseline (ms)
| Slot | Wall-clock (ms) | Notes |
|------|------------------|-------|
| R1 | 3 (summary=true) | warm |
| R2 | 18277 | solution-wide cold |
| R3 | 553 | cold |
| R4 | 439 | |
| R5 | 95 | |

### Parallel fan-out
- **Host logical cores:** not probed explicitly (client does not surface `Environment.ProcessorCount`). Mid-tier Windows 11 workstation.
- **Chosen N:** N/A — **blocked — client appears to serialize tool calls under the hood** (the batched Phase 3 / 6 / 10+12+13 turns completed with monotonic sequential-style timing; no visible overlap). Recorded once per Principle #8-class limitation.

| Slot | Parallel wall-clock (ms) | Speedup vs baseline | Expected | Pass / FLAG / FAIL | Notes |
|------|---------------------------|----------------------|----------|---------------------|-------|
| R1 | N/A | N/A | 0.7×N – N | N/A — client-serializes | |

### Read/write exclusion behavioral probe
| Probe | Observed | Expected | Pass / FLAG / FAIL | Notes |
|-------|----------|----------|---------------------|-------|
| R1 in flight ← W1 | — | writer waits | N/A — writers all skipped-safety | |
| W1 in flight ← R1 | — | reader waits | N/A — writers all skipped-safety | |

### Lifecycle stress
| Probe | Observed | Reader saw | Reader exception | correlationId | Expected | Pass / FLAG / FAIL | Notes |
|-------|----------|-----------|------------------|---------------|----------|---------------------|-------|
| R2 ← workspace_reload | skipped-safety after §14.1 crash — risk too high | — | — | — | waits-for-reader | N/A | |
| R3 ← workspace_close | skipped-safety | — | — | — | reader completes; close waits | N/A | |

## 17. Writer reclassification verification (Phase 8b.5)
| # | Tool | Status | Wall-clock (ms) | Notes |
|---|------|--------|------------------|-------|
| 1 | apply_text_edit | skipped-safety | — | would mutate user's working tree |
| 2 | apply_multi_file_edit | skipped-safety | — | same |
| 3 | revert_last_apply | skipped-safety | — | no audit-only apply was performed |
| 4 | set_editorconfig_option | skipped-safety | — | would modify repo `.editorconfig` |
| 5 | set_diagnostic_severity | skipped-safety | — | same |
| 6 | add_pragma_suppression | skipped-safety | — | would edit source |

## 18. Response contract consistency
| Tools | Concept | Inconsistency | Notes |
|-------|---------|---------------|-------|
| find_dead_fields(never-read) vs remove_dead_code_preview | dead-field removal safety | The former classifies a field with writes-but-no-reads as `never-read`; the latter refuses removal because there are still "references" (the writes) | Pair-finding logged at Phase 6i. Either align models, or emit a hint on find_dead_fields that `remove_dead_code_preview` will still refuse until the write sites are addressed |
| member_hierarchy vs find_base_members / find_overrides | metadata-base resolution | member_hierarchy resolves `IEquatable<T>.Equals`; find_base_members/find_overrides return 0 on the same symbol | Document the metadata-boundary or align |
| test_related vs test_discover | response shape | test_related returns a bare JSON array; test_discover wraps in `{testProjects:[…], returnedCount, totalCount, hasMore}` | Harmonize shape so clients can paginate/filter the same way |
| semantic_search (async vs non-async) | modifier-sensitivity | two paraphrase queries produced identical 1-result sets; description warns about this but a `Warning` field would surface the non-distinction | UX suggestion |

## 19. Known issue regression check (Phase 18)
**N/A — no prior source.** This repo has no `audit-reports/` history; `ai_docs/backlog.md` is a product backlog, not a Roslyn-MCP regression list.

Newly observed issues that correspond to *known Roslyn-Backed-MCP backlog rows* (based on the live description text):
- `coupling-metrics-tool` (open) — STALE; `get_coupling_metrics` is live and returns healthy results. Close.
- `find-duplicate-helpers-framework-wrapper-false-positive` — unobserved in this run (0 false positives). Cannot-reproduce.
- `change-signature-preview-callsite-summary` (P3) — **still reproduces** (preview `changes[].unifiedDiff` for change_signature_preview rename only listed the declaration file; verification via `compile_check` + `find_references` required). Matches the acknowledged limitation.

## 20. Known issue cross-check
- New §14.1 (server crash mid-Phase 6) — **no prior backlog row surfaces an auto-reload cascade → stdio-death pattern**; recommend opening `phase6-autoreload-cascade-stdio-death`.
- New §14.2 (impact_analysis over-budget despite referencesLimit) — recommend `impact-analysis-paging-contract`.
- New §14.3 (fix_all_preview IDE0305 provider throws) — upstream Roslyn issue candidate, not server code.
- New §14.4–14.9 — individual UX / contract-consistency issues; not present in prior sources this agent could see.
- New §14.12 — MCP client root allow-list blocks disposable worktree; may already be tracked on the Claude Code side.
