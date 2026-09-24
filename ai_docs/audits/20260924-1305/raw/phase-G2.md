# Phase G2 evidence — Phases 3, 4, 18 on W_RO (bbea96a6…)

Paths: `<ro>` = `<repo>/.worktrees/surface-test-20260924T130517Z-ro`. All calls read-only.

## Ground-truth text counts (rg, `<ro>` src+tests)
| symbol | rg | find_references |
|---|---|---|
| CompilationCache (type, -w) | 76 word hits incl comments/test names | 65 |
| ICompilationCache | 103 lines / 35 files | 97 |
| ServerSurfaceCatalog.Tool( (Host.Stdio) | 176 incl decl | 175 |
| ICompilationCache.GetCompilationAsync | ~40 cache invocations + impls/crefs | 47 |

## Phase 3 calls
| # | tool | inputs | ms | verdict | observation |
|---|---|---|---|---|---|
| 1 | workspace_list | — | n/a | PASS | W_RO v2 ready; W_RW stale (not ours) |
| 2 | symbol_search | CompilationCache summary | 605 | PASS | 188 total, paged 50; includes metadata-only Microsoft.CodeAnalysis.CompilationCache (filePath null) |
| 3 | symbol_search | ParameterObjectService kind=Class | 135 | PASS | 3 hits (type + 2 nested); kind=Class also returned a `Record` (CallSiteBinding) — kind filter treats Record as Class (acceptable) |
| 4 | symbol_search | WorkspaceManager summary limit20 | 81 | FLAG | totalCount=1000 exactly — looks like a clamp, not a true total (substring "WorkspaceManager" matches fields/tests) |
| 5 | find_references | CompilationCache metadataName summary | 6 | PASS | 65 total |
| 6 | find_references | ICompilationCache metadataName | 10 | PASS | 97 total |
| 7 | find_references | ServerSurfaceCatalog.Tool pos 434:33 | 80 | FLAG | 175 total = rg; but every ref in the static array initializer has containingMember:null |
| 8 | find_consumers | ICompilationCache limit5 | 8 | PASS | 37 consumer types / 3 projects; kinds Constructor/FieldType/MethodParameter/BaseType |
| 9 | find_type_consumers | ICompilationCache limit3 | 14 | FLAG | returns count=3 only — no totalCount/hasMore/offset; caller can't know it was truncated (35 files by rg) |
| 10 | find_type_usages | ICompilationCache limit3 | 19 | PASS | totalCount 97 = find_references; classifications MethodParameter/GenericArgument/Documentation |
| 11 | impact_analysis | ICompilationCache summary | 12 | PASS | 97 refs, 68 decls, 3 projects |
| 12 | symbol_impact_sweep | ICompilationCache summary max3 | 384 | FLAG | refs 97 OK; mapperCallsites flags `SymbolHandleSerializer.FindAllByMetadataNameAsync` parameter decl as a mapper callsite (suffix heuristic `*Serializer` hit on a param-type reference, not a To*/From* call) |
| 13 | find_references | ICompilationCache.GetCompilationAsync pos 67:24 | 121 | PASS | 47 (includes `<see cref>` doc refs) |
| 14 | callers_callees | same | 74 | FLAG | totalCallers 47 — includes `<see cref=GetCompilationAsync>` doc comments (ICompilationCache.cs:32,101) as "callers"; classification null (find_references says "Read") |
| 15 | symbol_relationships | same | 421 | PASS | refs 47, implementations 6 (CompilationCache + 5 test fakes) |
| 16 | member_hierarchy | same (interface member) | 323 | FLAG | baseMembers/overrides/siblingInterfaceImplementations all [] — the 6 implementations symbol_relationships finds are not surfaced for an interface member |
| 17 | callers_callees | ServerSurfaceCatalog.Tool 434:33 | 48 | FAIL | callers:[] totalCallers 0 vs find_references 175. callees list GetSchema/TryGet (OK) but omits SurfaceEntry ctor + `with` |
| 18 | impact_analysis | Tool summary | 47 | FLAG | 175 refs but totalAffectedDeclarations 0 (same root cause) |
| 19 | symbol_impact_sweep | Tool summary max2 | 377 | PASS | 175 refs (containingMember null) |
| 20 | test_related | CompilationCache metadataName max10 | 124 | PASS | 176 total related tests, pagination block present, filter string emitted |
| 21 | type_hierarchy | WorkspaceManager metadataName | 6 | PASS | IWorkspaceManager + IDisposable, no base |
| 22 | find_implementations | IWorkspaceManager metadataName | 2 | PASS | 37 = rg (38 regex hits, 1 is a comment); no totalCount field |
| 23 | symbol_info | SideEffectClassifier metadataName | 2 | PASS | static class, docs |
| 24 | document_symbols | ConsumerAnalysisService.cs filePath | 61 | PASS | 4 members, lines match source |
| 25 | get_source_text | ParameterObjectService 490-580 | 84 | PASS | slice exact; totalLineCount 1873 |
| 26 | find_shared_members | ParameterObjectService | 11 | PASS | 0 — correct: only one public method |
| 27 | find_type_mutations | CompilationCache | 24 | FLAG | 3 members CollectionWrite OK; GetCompilationSnapshotAsync externalCallers [] — production callers go through ICompilationCache (interface dispatch not counted), only test callers appear |
| 28 | find_property_writes | WorkspaceSession.LoadedPath 1537:24 | 92 | PASS | 1 Assignment = rg |
| 29 | symbol_relationships | 498:20 return-type token, default | 489 | PASS | auto-promoted to method; refs 1 |
| 30 | symbol_relationships | 498:20 preferDeclaringMember=false | 183 | PASS | resolves `string`; builtin hint |
| 31 | symbol_signature_help | 498:20 default | 144 | PASS | method signature |
| 32 | symbol_signature_help | 498:20 preferDeclaringMember=false | 71 | PASS | `string` |
| 33 | probe_position | 498:20 | 84 | PASS | StringKeyword, containing method — agrees |
| 34 | impact_analysis | ClassifyValueTypeMutation 498:28 | 89 | PASS | 1 ref, 1 decl |
| 35 | test_related | ClassifyValueTypeMutation 498:28 | 85 | FLAG | 0 tests; type-level query (#39) finds 45 in ParameterObjectPreviewTests. Private-member sweep never escalates to containing type (TestDiscoveryService.cs:272/309) |
| 36 | test_related_files | SideEffectClassifier.cs + ConsumerAnalysisService.cs | 91 | PASS | 25 total, filter, per-file missReason |
| 37 | find_consumers | IWorkspaceManager projectFilter=Host.Stdio | 4 | PASS | 10 consumers / 1 project |
| 38 | probe_position | 498:2 whitespace | 65 | PASS | Whitespace, leadingTriviaBefore true |
| 39 | test_related | ParameterObjectService metadataName | 4 | PASS | 45 tests |
| 40 | find_type_consumers | ICompilationCache limit100 | 177 | PASS | 34 files = rg 35 minus declaring file |
| 41 | symbol_info | 498:2 whitespace strict | 106 | FLAG | NotFound generic "Ensure the workspace is loaded…" — vague: no whitespace / allowAdjacent hint |
| 42 | document_symbols | SideEffectClassifier metadataName | 120 | PASS | locator path |
| 43 | get_source_text | SideEffectClassifier 36-155 | 60 | PASS | |
| 44 | get_source_text | ConsumerAnalysisService 20-133 maxChars 3000 | 100 | PASS | truncated:true + marker |

## Generator-blind cross-check (Phase 2 Critical: CompilationCache.cs:61 -> SourceGeneratorCompilation.CreateAsync)
Host.Stdio has a materialized generated doc (RegexGenerator.g.cs per source_generated_documents); RoslynMcp.Roslyn/Core show no mismatch in any tool.
| tool | Roslyn symbol (ICompilationCache 97 / GetCompilationAsync 47) | Host.Stdio symbol (SurfaceEntry 61 / Tool 175) | verdict |
|---|---|---|---|
| find_references | 97 / 47 | 61 / 175 | baseline OK (= rg) |
| find_consumers | 37 types | 13 types / 2 projects | OK |
| find_type_consumers | 34 files OK | 0 (SurfaceEntry), 0 (McpToolException, 13 rg lines) | UNDER-REPORTS |
| find_type_usages | 97 | 61 | OK |
| impact_analysis | 97 | 175 refs (decls 0 = field-initializer bug, not generator) | OK on refs |
| symbol_impact_sweep | 97 | 61 / 175 | OK |
| symbol_relationships | 47 | 61 | OK |
| callers_callees | 47 (incl crefs) | Tool: 0 callers | under-reports — root cause field-initializer containing symbol, NOT generator |
| test_related | 176 | 52 | OK |
| member_hierarchy | not count-bearing | — | n/a |
| find_type_mutations (extra) | CompilationCache 3 members | HostProcessMetadataStore 0 members (WriteCurrent does File.WriteAllText/Move/Copy/Delete) | UNDER-REPORTS |
Mechanisms: TypeConsumersService.cs:111 resolves the type from the cache (generator-rerun) compilation, then SymbolFinder.FindReferencesAsync(symbol, solution) with a foreign-compilation symbol -> 0. MutationAnalysisService.cs:375 compares projectCompilation.Assembly (rerun compilation's new assembly symbol) to namedType.ContainingAssembly -> never equal -> compilation null -> SideEffectClassifier skipped. SymbolResolver-based tools are unaffected.

## Phase 4 calls (ClassifyValueTypeMutation 498, SideEffectClassifier.ClassifyMethod 87, ConsumerAnalysisService.FindConsumersAsync 20, MutationAnalysisService.ClassifyTypeUsageAfterWalk 665, TypeMoveService.PreviewMoveTypeToFileAsync 21; expr-bodied Rank 145)
| # | tool | inputs | ms | verdict | observation |
|---|---|---|---|---|---|
| 45 | analyze_data_flow | POS 505-570 | 11 | PASS | 9 declared, 5 params flow in |
| 46 | analyze_control_flow | POS 505-570 | 9 | PASS | 9 returns, lines match source |
| 47 | analyze_data_flow | Consumer 23-132 | 22 | PASS | captured = solution, filterSet (lambda :73) correct |
| 48 | analyze_control_flow | Consumer 23-132 | 2 | PASS | single return :129 |
| 49 | analyze_control_flow | Rank 145-154 expr-bodied | 2 | FLAG low | synthesized OK but exitPoints [] while block-bodied results mirror returns into exitPoints |
| 50 | analyze_data_flow | Rank 145-154 | 3 | PASS | scope flows in |
| 51 | analyze_data_flow | ClassifyMethod 89-114 | 4 | PASS | ns/typeName/methodName always assigned |
| 52 | analyze_control_flow | TypeMove 23-141 | 9 | PASS | 1 return; throws not exitPoints (Roslyn semantics) |
| 53 | analyze_control_flow | Mutation 667-725 (straddles 2 methods) | 33 | FAIL | silently analyzed only the 2nd method (returns 711-725); requested method's returns 672/675 dropped, no warning |
| 54 | analyze_control_flow | Mutation 667-694 | 5 | PASS | returns 672, 675 |
| 55 | analyze_data_flow | Mutation 667-725 | 4 | FAIL | same silent narrowing (FlowAnalysisService.cs:247-265 picks largest block group) |
| 56 | trace_exception_flow | PublicInvalidOperationException max5 | 1647 | FLAG | throw half present; top-5 catches are catch(System.Exception) though 9 catch(InvalidOperationException) exist — ExceptionFlowService.cs:172/242 ranks only exact-type vs rest; countOmitted mixes both lists; throwSiteCount = returned not total |
| 57 | get_operations | SideEffectClassifier 51:25 | 77 | PASS | Invocation->Argument->LocalReference |
| 58 | get_syntax_tree | SideEffectClassifier 138-143 depth4 | 68 | PASS | agrees with IOperation view |

## Phase 18 regressions
| # | id | call | ms | result |
|---|---|---|---|---|
| 59 | member-hierarchy-bare-null | member_hierarchy metadataName=No.Such.Type.Member | 1 | no longer reproduces — structured NotFound (message vague) |
| 56/63/64 | trace-exception-flow-no-throwsite-half | ArgumentException vs IOException max3 | 984/1046 | partially fixed — throwSites present, catch lists differ per type; still truncates at default 200 for common types (ArgumentException 512 total, IOException 270) since every catch(Exception) qualifies |
| 60 | find-references-identical-ambiguous-candidates | find_references metadataName=System.Xml.XmlException | 32 | no longer reproduces — 4 refs, no ambiguity |
| 61/62 | find-implementations-corlib-metadataname-zero | metadataName System.IDisposable -> 0+hint; source-anchored WorkspaceManager.cs:22:59 -> ALSO 0 + same hint | 0/79 | still reproduces (worse; confirms G3). SymbolTools.cs:350 short-circuits for any locator kind; hint's advice cannot succeed. rg: 26 `class … : IDisposable` |
| 66 | schemahint-double-question-mark | symbol_info(line,column only) | 0 | no longer reproduces — `line: int?` |
| 65/67 | test-discover-no-autopagination | test_discover limit2; offset2720 default limit | 5/0 | no longer reproduces — totalCount 2724, limit 200 |

## Extra calls
| # | tool | inputs | ms | verdict | observation |
|---|---|---|---|---|---|
| 68 | test_related | filePath+line only | 0 | FLAG | InvalidArgument returned as NON-error result (isError false); symbol_info same error = MCP error. ValidationTools.cs:449-456; same in 7 tools |
| 69-73 | find_references/find_type_usages/find_consumers/find_type_consumers/symbol_impact_sweep | SurfaceEntry | 2/5/2/0/197 | see generator table |
| 74-76 | find_type_consumers | McpToolException / CompilationCache / PublicInvalidOperationException | 0/2/1 | Host.Stdio 0; Roslyn/Core OK |
| 77 | symbol_relationships | SurfaceEntry | 2 | PASS 61 |
| 78 | test_related | SurfaceEntry | 4 | PASS 52 |
| 79 | source_generated_documents | Host.Stdio (unassigned, read-only evidence) | n/a | RegexGenerator.g.cs |
| 80 | find_type_mutations | HostProcessMetadataStore | 1 | FAIL | 0 mutating members |

## Bad-code sightings (Directive #3, orchestrator to file)
- src/RoslynMcp.Roslyn/Helpers/SideEffectClassifier.cs:78,82 — stale comments ("classifying a single SyntaxKind" but switch is on (namespace,typeName); "See inline note above" but note is below).
- src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs:51 — `if (ct.IsCancellationRequested) break;` returns a partial consumer set on cancellation instead of throwing.
- src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs (7 ClassifyAndFormat sites) — inline catch-all error formatting bypasses StructuredResultProjector IsError=true.
