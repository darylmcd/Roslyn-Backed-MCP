# Phase group G3 — Phases 5, 11, 14 (run 20260924T130517Z)

Server: Darylmcd.RoslynMcp@4.2.1. W_RO=`bbea96a6…` (RoslynMcp.slnx, 905 docs). W_RW=`4b02621c…` (SampleSolution, 37 docs; read-only calls only — no mutations by G3).
Paths sanitized: `<repo>` = <repo>, `<ro>` = `<repo>/.worktrees/surface-test-20260924T130517Z-ro`, `<rw>` = `<repo>/.worktrees/surface-test-20260924T130517Z/samples/SampleSolution`.
Operator stderr access: no. Total calls: 66.

## Heartbeat
| # | tool | inputs | elapsedMs | verdict | observation |
|---|---|---|---|---|---|
| 1 | workspace_list | — | n/a (no _meta on list? present-less) | PASS | both workspaces isReady, W_RO v2, W_RW v1 |

## Phase 5 — Snippet & script validation
| # | tool | inputs | elapsedMs | verdict | observation |
|---|---|---|---|---|---|
| 2 | analyze_snippet | expression `1 + 2` | 771 | PASS | isValid, 0 diags; wrapper `Snippet.Evaluate()` |
| 3 | analyze_snippet | program: class Foo{prop,method}+interface IQux | 251 | PASS | 5 declared symbols listed correctly |
| 4 | analyze_snippet | statements `int x = "hello";` | 279 | PASS | CS0029 at startColumn 9 (user-relative, FLAG-C fix holds) |
| 5 | analyze_snippet | returnExpression `return 42;` | 229 | PASS | valid; wrapper `object? Run()` |
| 6 | analyze_snippet | statements `return 42;` | 260 | PASS | CS0127 — documented FLAG-007 behaviour |
| 7 | analyze_snippet | members: method + field `_name`, usings=[System.Text] | 301 | FLAG | CS0414 correctly reports field, but declaredSymbols lists only `Add` — field `_name` omitted (see finding snippet-declared-symbols-drop-fields) |
| 8 | analyze_snippet | code="" (default kind) | 177 | FLAG (Low, C5) | isValid=true, `declaredSymbols: null` (vs [] elsewhere) — null-vs-empty contract drift, SnippetAnalysisService.cs:117 |
| 9 | analyze_snippet | kind="bogusKind" | 0 | FLAG | error vague/unhelpful: "Parameter '<unknown>' is invalid…"; source message listing valid kinds (SnippetAnalysisService.cs:154) is redacted; schemaHint omits descriptions |
| 10 | evaluate_csharp | `Enumerable.Range(1,10).Sum()` | 794 | PASS | 55 / System.Int32; appliedScriptTimeoutSeconds=10 |
| 11 | evaluate_csharp | multi-line List sort + join | 850 | PASS | "1,2,3!" |
| 12 | evaluate_csharp | `int.Parse("abc")` | 753 | PASS | graceful "Runtime error: FormatException…" (actionable) |
| 13 | evaluate_csharp | code="" | 446 | FLAG (Low, C5) | success=true, resultType=null, resultValue = string `"null"` (not JSON null) |
| 14 | evaluate_csharp | `int x = "nope";` | 603 | PASS | compilationErrors[] CS0029 col 9 |
| 15 | evaluate_csharp | `while (true) { }` default timeout | **20055** (worker elapsedMs 20046) | PASS (w/ note) | hard deadline = budget 10 s + ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS 10 s; worker killed, "0/8 worker process(es) could not be reclaimed"; progressHeartbeatCount=9. Returns at 2x configured timeout — documented design (ScriptingTools.cs:14-18, README), not a hang. Wall-clock bracket via bash was invalid (ran in parallel); _meta is authoritative. |
| 16 | evaluate_csharp | `while(true){}` timeoutSeconds=2 (non-default) | 12079 | PASS | 2 s + 10 s grace; heartbeats=5 |
| 17 | evaluate_csharp | timeoutSeconds=-1 | 0 | FLAG | error vague: "Parameter 'timeoutSeconds' is invalid…"; source says "must be greater than 0" (ScriptingTools.cs:33) but redacted; schemaHint truncated at "(UX-002)" so ">0" constraint also dropped |
| 18 | evaluate_csharp | `await Task.Delay(Timeout.Infinite)` t=2 | 1204 | PASS (precondition) | CS0103 `Timeout` — System.Threading not in default imports; not a server defect |

## Phase 11 — Semantic search, discovery, reflection/DI
| # | tool | inputs | elapsedMs | verdict | observation |
|---|---|---|---|---|---|
| 19 | semantic_search | W_RO "async methods returning Task<bool>" | 190 | PASS | 15 results; predicates keyword:async,keyword:method,returning-type; text cross-check `async … Task<bool> X(` = 15 lines |
| 20 | semantic_search | "async methods returning Task&lt;bool&gt;" | 160 | PASS | identical 15 results — HTML-decode parity holds |
| 21 | semantic_search | "methods returning Task<bool>" | 350 | PASS | 21 results; delta = 6 non-async (interface/abstract/test fakes e.g. IUndoService.RevertAsync, IWorkspaceCacheStore.PutAsync) — explainable, async predicate dropped |
| 22 | semantic_search | "classes implementing IDisposable" projectName=RoslynMcp.Roslyn limit=200 | 47 | PASS | 15 types; text grep of `class…: …IDisposable` in project = 11 direct lines (rest are multi-line/nested decls) |
| 23 | find_implementations (cross-check) | metadataName=System.IDisposable | 9 | FLAG (documented) | count 0 + hint "source-anchor the query instead" |
| 24 | find_implementations (cross-check) | source-anchored ChangeTracker.cs:12:54 (probe confirms token `IDisposable`) | 398 | **FAIL** | still count 0 + SAME hint — the hint's recommended workaround is also short-circuited. Root cause SymbolTools.cs:350 guard ignores locator kind |
| 25 | probe_position | ChangeTracker.cs:12:54 | 88 | PASS | Identifier `IDisposable`, containing ChangeTracker |
| 26 | semantic_grep | `^ConfigureAwait$` limit=5 | 170 | PASS (note) | 5 hits, hasMore; totalCount=500 = collection ceiling, not true total (documented in schema) |
| 27 | semantic_grep | bogus `([unclosed` | 1 | PASS | clean InvalidArgument, actionable .NET-regex guidance |
| 28 | semantic_grep | `zzqNoSuchIdentifierqzz` | 303 | PASS | clean empty result |
| 29 | semantic_grep | `TODO\|FIXME` scope=comments projectName=RoslynMcp.Core (non-default) | 9 | FLAG | 2 hits but line/column = START of multi-line doc-comment trivia (RecordFieldAdditionImpactDto.cs reported line 64; actual `TODO` at line 70) and snippet = first 200 chars of trivia which does NOT contain the match. Root cause SemanticGrepService.cs:233-243 |
| 30 | semantic_grep | `Task\.Run` identifiers | 187 | PASS | 0 — documented (identifier scope splits member access) |
| 31 | find_reflection_usages | W_RO summary=true | 4645 | PASS | 405 total, 13 kinds, isComplete |
| 32 | find_reflection_usages | projectName=Host.Stdio limit=15 | 577 | PASS | 15 of 67, paged across kinds (11 typeof + 4 GetMethods) |
| 33 | get_di_registrations | Host.Stdio summary=true | 515 | FLAG | count 14 all Singleton; source has **17** registration calls (rg `.(Add\|TryAdd)(Singleton…)`) — 3 `TryAddSingleton` (ServiceCollectionExtensions.cs:80-82) excluded from default view while totalCountMeaning="complete" |
| 34 | get_di_registrations | Host.Stdio detailed | 2 | FLAG | spot-check 14 rows: lines/methods correct; **2 wrong implementationType**: ServiceCollectionExtensions.cs:87 `AddSingleton(sp => new NuGetVersionChecker(sp.GetRequiredService<IHttpClientFactory>(),…))` → impl `System.Net.Http.IHttpClientFactory`; :97 `AddSingleton<IWorkspaceCacheStore>(sp => new WorkspaceCacheStore(sp.GetService<ILogger<…>>()))` → impl `ILogger<WorkspaceCacheStore>`. Program.cs:66 factory correctly "factory". |
| 35 | get_di_registrations | Host.Stdio summary + showLifetimeOverrides (non-default) | 3 | PASS | overrideChain IServerObservabilitySink: 2 regs (TryAdd + Program AddSingleton), 1 dead — raw view does include TryAdd |
| 36 | get_di_registrations | W_RO full solution summary | 4184 | PASS | 152 regs / 110 distinct, all Singleton, within 15 s |
| 37 | source_generated_documents | W_RW (all) | n/a | FLAG | 3 docs, all `*.GlobalUsings.g.cs` from obj/ — MSBuild-generated, NOT source-generator output (disk fallback WorkspaceManager.cs:840-858). No origin discriminator. Response is a bare JSON array → **no `_meta`** (ToolErrorHandler.cs:278 skips non-object roots). SampleLib.Generators not in slnx (precondition). |
| 38 | source_generated_documents | W_RO projectName=RoslynMcp.Host.Stdio | n/a | PASS (w/ C5 note) | RegexGenerator.g.cs — genuine generator output; again no `_meta` |

## Phase 14 — Navigation & completions (W_RW fixture unless noted)
| # | tool | inputs | elapsedMs | verdict | observation |
|---|---|---|---|---|---|
| 39 | go_to_definition | AnimalService.cs:20:33 (`Speak` usage) | 2220 (queued 2181 stale auto-reload) | PASS | IAnimal.cs:6:12 `string Speak();`. staleAction=auto-reloaded (W_RW touched by another group) |
| 40 | goto_type_definition | AnimalService.cs:20:26 (`animal` local) | 5 | PASS (nit) | IAnimal.cs:3 — type not variable. containingMember "SampleLib.IAnimal?" leaks var-nullable annotation |
| 41 | goto_type_definition | AnimalService.cs:20:18 (`sound` → System.String, BCL) | 3 | PASS | NotFound "metadata-only and has no source definition" — actionable enough |
| 42 | enclosing_symbol | AnimalService.cs:21:14 | 6 | PASS | MakeThemSpeak method |
| 43 | probe_position | AnimalService.cs:20:32 | 5 | PASS | IdentifierToken `Speak` |
| 44 | probe_position | AnimalService.cs:16:34 (whitespace) | 5 | PASS | WhitespaceTrivia reported, no adjacent-identifier fallback (matches schema) |
| 45 | get_completions | 20:32 trigger='.' filterText=To maxItems=10 | 1119 | PASS | [ToString] only (member-access set) |
| 46 | get_completions | 21:13 filterText=To (no trigger) | 67 | PASS | ToString first, then `~`-sorted ToBase64Transform, Token*… — v1.8 ranking holds |
| 47 | get_completions | maxItems=0 | 1 | PASS | InvalidArgument; generic msg but schemaHint carries "must be > 0" (actionable via hint) |
| 48 | get_symbol_outline | W_RO SemanticGrepService.cs | 63 | PASS | 1 class, 16 members; deprecation.canonicalName=document_symbols |
| 49 | document_symbols | same file | 64 | PASS | identical tree, kinds, ordering (0 drift) |
| 50 | document_symbols | metadataName=SampleLib.Hierarchy.Rectangle (non-default) | 5 | PASS (nit) | 5 members; modifiers order ["public","override"] vs find_overrides ["override","public"] |
| 51 | find_references_bulk | 4 locators (IAnimal md, Speak pos, Shape md, bogus md) summary=true | 21 | PASS (C5 nit) | 15/3/5/0 — matches single calls exactly; bogus → per-item error "Symbol could not be resolved." (actionable). Bulk order unsorted vs single path-sorted |
| 52 | find_references | IAnimal summary | 5 | PASS | 15 |
| 53 | find_references | Speak pos summary | 5 | PASS | 3 |
| 54 | find_references | Shape summary | 1 | PASS | 5 |
| 55 | find_overrides | Shape.Describe (virtual) 7:27 | 2750 (stale reload 2691) | PASS | Rectangle.Describe |
| 56 | find_overrides | Shape.Area (abstract) 5:28 | 34 | PASS | Circle.Area, Rectangle.Area |
| 57 | find_overrides | metadataName=type Rectangle (negative) | 0 | FLAG (Low) | silent count 0 for a non-member symbol, no hint that a member is required |
| 58 | find_base_members | Rectangle.Describe 16:28 | 6 | PASS | Shape.Describe |
| 59 | find_base_members | EquatablePoint.Equals(object) (struct, corlib boundary) | 165 | PASS | ValueType.Equals + object.Equals with filePath=null (matches schema) |
| 60 | enclosing_symbol | line=999 (negative) | 8 | FLAG | vague "Parameter 'line' is invalid" — no line-count context |
| 61 | recommend_workflow | "rename this symbol safely…" | 1 | PASS | rename_preview → rename_apply, compile_check, test_related_files |
| 62 | recommend_workflow | "find all DI registrations and check which services are never injected" | 0 | FLAG (Low) | falls to generic discover_capabilities fallback; no DI rule (get_di_registrations not suggested) — WorkflowRecommendationTools.cs has no DI intent |
| 63 | recommend_workflow | "run related tests after I edit a file" + filePath | 0 | PASS | test_related_files → test_run, test_related, validate_recent_git_changes |
| 64 | recommend_workflow | "xyzzy plugh" | 0 | PASS | generic fallback |
| 65 | recommend_workflow | task="" | 0 | FLAG | vague error; schemaHint truncated to "Natural-language task, e.g" (TrimToFirstSentence splits on ". " inside "e.g. ") |
| 66 | find_reflection_usages | W_RO default (no summary, limit 200) | 3509 | PASS (size note) | **94,761 chars** — just under 100 KB threshold, but exceeds Claude Code inline tool-result cap (spilled to file) |

Catalog check: every tool named by recommend_workflow (rename_preview, rename_apply, compile_check, test_related_files, get_prompt_text, server_info, test_run, test_related, validate_recent_git_changes) exists in ledger-seed.tsv (1 row each). `roslyn://server/catalog` is a resource URI.

## >100 KB default responses
None exceeded 100 KB. Closest: find_reflection_usages default on W_RO = 94.8 KB. semantic_search default (21 results) ≈ 25 KB — dominated by base64 symbolHandle per row.

## Findings (confirmed against source at HEAD)
| id | sev | check | tool | anchor | summary | backlog | fixedAtHead |
|---|---|---|---|---|---|---|---|
| find-implementations-corlib-guard-blocks-source-anchor | Medium | C1 | find_implementations | src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:350 | Corlib-root guard fires for every locator kind, so the source-anchored query the hint recommends also returns count 0 + the same hint; no way to enumerate IDisposable implementers | none | false (SymbolTools.cs unchanged) |
| di-factory-lambda-impl-misattributed | Medium | C1 | get_di_registrations | src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs:535 | TryResolveLambdaReturnType returns the first nested GetRequiredService/GetService<T> anywhere in the lambda, so `sp => new Foo(sp.GetRequiredService<Bar>())` reports Bar (a ctor dependency) as the implementation; doc comment :525 asserts this as intended. Should use ObjectCreation type / direct-return forwarding only | none | false |
| di-default-view-omits-tryadd-claims-complete | Low | C2 | get_di_registrations | src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs:181 | Default/summary view filters TryAdd* (3/17 in Host.Stdio) yet reports totalCountMeaning=complete; schema doesn't disclose exclusion | none (host-tools-dependency-analysis-extraction deps name di-registration-scan-completeness, row not present) | false |
| semantic-grep-comment-hit-location-is-trivia-start | Low | C1 | semantic_grep | src/RoslynMcp.Roslyn/Services/SemanticGrepService.cs:233 | comments scope reports trivia start line/col and first 200 chars of trivia, not the regex match position/text; multi-line doc comments point several lines off and snippet may not contain the match | none | false |
| argument-exception-detail-redacted | Low | C4 | analyze_snippet, evaluate_csharp, enclosing_symbol, recommend_workflow | src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs:154 ; src/RoslynMcp.Host.Stdio/Tools/ScriptingTools.cs:33 ; ToolErrorHandler.cs:646 | Throw sites use plain ArgumentException (analyze_snippet without ParamName → "<unknown>"); BuildSafeArgumentMessage redacts to generic text, losing "valid kinds…" / "must be > 0". Fix: PublicArgumentException at these sites | none | false (ToolErrorHandler diff touches only root-boundary refusal) |
| schema-hint-first-sentence-split-on-abbreviation | Low | C4 | (all InvalidArgument) | src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:788 | TrimToFirstSentence splits on first ". " → "e.g" truncation and drops constraint sentences ("Must be > 0") after "(UX-002)." | none | false |
| snippet-declared-symbols-drop-fields | Low | C1 | analyze_snippet | src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs:105 | FieldDeclarationSyntax is in the filter but GetDeclaredSymbol(FieldDeclarationSyntax) returns null — must walk Declaration.Variables; enums/delegates/events/ctors also never listed | none | false |
| source-generated-documents-msbuild-conflation-and-no-meta | Low | C5 | source_generated_documents | src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:840 ; ToolErrorHandler.cs:278 | Disk fallback globs obj/**/*.g.cs (GlobalUsings/AssemblyInfo) and labels them generator output without an origin field; bare-array response gets no _meta | none | false (WorkspaceManager diff does not touch this method) |
