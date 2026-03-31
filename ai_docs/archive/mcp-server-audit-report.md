# MCP Server Audit Report

**Date:** 2026-03-30
**Server:** roslyn-mcp v1.2.0+1f52c42, Roslyn 5.3.0.0, .NET 10.0.5
**Catalog Version:** 2026.03
**Surface:** 49 stable + 67 experimental tools | 6 resources | 16 prompts
**Audit method:** All 16 phases of `ai_docs/prompts/deep-review-and-refactor.md` executed
**Total tool calls:** ~300 across 4 solutions
**Solutions audited:** Roslyn-Backed-MCP.sln (6 projects, 273 docs), SampleSolution (embedded), NetworkDocumentation.sln (8 projects, 335 docs), ITChatBot.sln (23 projects, 366 docs)

---

## Summary

| Severity | Count |
|----------|------:|
| High | 3 |
| Medium | 9 |
| Low | 17 |
| Informational | 6 |
| **Total issues** | **35** |

The server is **stable under load** — zero crashes, hangs, or unhandled exceptions across ~300 tool calls on 4 solutions ranging from 6 to 23 projects. All critical paths (workspace loading, diagnostics, symbol resolution, refactoring previews, build, test) function correctly. Issues are concentrated in edge cases (type resolution ambiguity, namespace handling, NuGet content file pollution), output sizing (no pagination on large outputs), and discoverability.

---

## Issues

### HIGH Severity

#### AUDIT-01: `scaffold_test_preview` resolves wrong type and generates hardcoded `using SampleLib`
- **Tool:** `scaffold_test_preview`
- **Repro 1 (Roslyn-Backed-MCP.sln):** `targetTypeName: "WorkspaceManager"` — resolved to wrong project (SampleLib instead of RoslynMcp.Roslyn.Services), generated `using SampleLib;` and parameterless `new WorkspaceManager()`.
- **Repro 2 (NetworkDocumentation.sln):** `targetTypeName: "SnapshotStore"` — generated `using SampleLib;` (nonexistent namespace) and `new SnapshotStore()` (missing required constructor parameters).
- **Repro 3 (ITChatBot.sln):** `targetTypeName: "ChatOrchestrator"` — generated `using SampleLib;` (nonexistent namespace) and `new ChatOrchestrator()` (class has 19-parameter constructor).
- **Root cause:** `using SampleLib;` is a hardcoded template placeholder that is never replaced. Type resolution does not scope to the correct project or resolve constructor parameters.
- **Impact:** Generated test files will not compile in any non-trivial scenario.
- **Reproducibility:** Always (confirmed across 3 solutions)
- **Recommendation:** Resolve target type by fully-qualified name scoped to the test project's references. Generate constructor parameters from the type's actual signature. Remove hardcoded `SampleLib` import.

#### AUDIT-28: `extract_interface_cross_project_preview` doesn't detect existing interface conflicts
- **Tool:** `extract_interface_cross_project_preview`
- **Input (ITChatBot.sln):** Extract `IChatOrchestrator` from `ChatOrchestrator` into `ITChatBot.Adapters.Abstractions`
- **Expected:** Warning that `IChatOrchestrator` already exists in `src/chat/IChatOrchestrator.cs`
- **Actual:** Generates a second `IChatOrchestrator` in a different project without conflict detection. Would create compile errors from conflicting type names.
- **Reproducibility:** Always
- **Impact:** Silently creates duplicate type that breaks compilation.
- **Recommendation:** Check for existing types with the same name across the solution and warn or refuse.

#### AUDIT-29: `find_reflection_usages` polluted by NuGet content files
- **Tool:** `find_reflection_usages`
- **Input (ITChatBot.sln):** No filters (full solution)
- **Expected:** Reflection usages in project source code
- **Actual:** 101 results, vast majority from `xunit.v3.core` NuGet content file `DefaultRunnerReporters.cs` duplicated ~12 times (once per test project). Only 3 project-authored files actually contain reflection (11 real hits).
- **Reproducibility:** Always (for solutions with xUnit v3 test projects)
- **Impact:** Results are unusable without manual filtering. Same root cause as AUDIT-20 (NuGet content files in workspace).
- **Recommendation:** Filter results to project source files only, excluding NuGet `_content/` paths.

---

### MEDIUM Severity

#### AUDIT-02: `test_run` parameterless call returns opaque error
- **Tool:** `test_run`
- **Input:** No `projectName` or `filter` specified
- **Expected:** Run tests across all test projects, or return structured error
- **Actual:** Opaque error: "An error occurred invoking 'test_run'" — no category, no exception type, no guidance
- **Reproducibility:** Always (when workspace has no test projects or multi-solution workspace)
- **Recommendation:** Return structured error with category and message.

#### AUDIT-03: `move_type_to_file_preview` false negative for nested types
- **Tool:** `move_type_to_file_preview`
- **Input:** File with `WorkspaceManager` (line 17) and `WorkspaceSession` (line 461, nested class)
- **Expected:** Offer to move one of the types to its own file
- **Actual:** Error: "Source file only contains one type. Move is unnecessary."
- **Reproducibility:** Always (for files containing nested/inner classes)
- **Recommendation:** Clarify error message to say "no additional top-level types" or support nested class extraction.

#### AUDIT-04: `set_project_property_preview` silent no-op and missing PropertyGroup failure
- **Tool:** `set_project_property_preview`
- **Repro 1 (SampleLib):** Set `Nullable=enable` on project that already has it — returns success with preview token but empty diff. No indication this is a no-op.
- **Repro 2 (NetworkDocumentation.Core):** Set `LangVersion=preview` — error: "Project file is missing a PropertyGroup element." Fails for projects using centralized Directory.Build.props.
- **Reproducibility:** Always
- **Recommendation:** (1) Return explicit "no changes needed" for no-op. (2) Create PropertyGroup when missing.

#### AUDIT-05: `find_overrides` position-sensitive within same token
- **Tool:** `find_overrides`
- **Input:** `Shape.Area()` abstract method — column 27 (on `A`) vs column 30 (on `(`)
- **Expected:** Same result for any column within the method name token
- **Actual:** Column 27 returns 0 results; column 30 returns 2 correct results
- **Reproducibility:** Sometimes (depends on exact column within the identifier token)
- **Recommendation:** Resolve the symbol from the entire token span, not just the exact cursor position.

#### AUDIT-19: `find_unused_symbols` returns duplicate entries
- **Tool:** `find_unused_symbols`
- **Repro 1 (NetworkDocumentation.sln):** `LoggingHelpersLog` appears 2 times in both result sets (same symbolHandle).
- **Repro 2 (ITChatBot.sln):** `XunitAutoGeneratedEntryPoint` appears 11 times (once per test project, same NuGet content file).
- **Reproducibility:** Always
- **Impact:** Inflates dead code counts. Consumers must deduplicate by symbolHandle.
- **Recommendation:** Deduplicate results by symbolHandle before returning.

#### AUDIT-21: `fix_all_preview` no code fix provider for IDE/compiler/CA diagnostics
- **Tool:** `fix_all_preview`
- **Repro 1 (Roslyn-Backed-MCP.sln):** `diagnosticId="IDE0005"`, `"CS8019"`, `"CS0414"` — "No code fix provider found"
- **Repro 2 (ITChatBot.sln):** `diagnosticId="CA5351"`, `"CA1707"` — "No code fix provider found". The workspace does not load .NET analyzers (`Microsoft.CodeAnalysis.NetAnalyzers`) since these are SDK-implicit and only active during `dotnet build`. `project_diagnostics` reports 1,107 CA-prefixed diagnostics but none are fixable.
- **Reproducibility:** Always (for IDE, CS warning, and CA-prefixed diagnostics)
- **Workaround:** Use `organize_usings_preview` for unused-using removal
- **Recommendation:** Either load analyzer NuGet packages explicitly, or return a clear message pointing to the workaround.

#### AUDIT-22: `extract_interface_cross_project_preview` uses source namespace instead of target
- **Tool:** `extract_interface_cross_project_preview`
- **Input (NetworkDocumentation.sln):** Extract from DiagramService in Diagrams project, targeting Core project
- **Expected:** Interface file in Core project with a Core-appropriate namespace
- **Actual:** Interface file placed in Core project but uses `namespace NetworkDocumentation.Diagrams;` — the source namespace, not the target project namespace
- **Reproducibility:** Always
- **Impact:** Cross-project extraction creates namespace mismatch. Users must manually fix.
- **Also:** Copies all usings from the source file (16 directives) instead of only the ones needed by the interface signature (see also ITChatBot repro in AUDIT-28 context).
- **Recommendation:** Derive namespace from the target project's root namespace or folder structure. Filter usings to those required by the interface signature.

#### AUDIT-23: `semantic_search` unreliable for generic return type queries
- **Tool:** `semantic_search`
- **Input:** `query="async methods returning Task<bool>"`
- **Repro 1 (NetworkDocumentation.sln):** 0 results. Non-generic version returns 10 correct results.
- **Repro 2 (ITChatBot.sln):** First call returned 0 results; second call returned 2 correct results (`IsGitRepositoryAsync`, `IsGitAvailableAsync`).
- **Reproducibility:** Always for generic syntax; intermittent warmup behavior on ITChatBot
- **Recommendation:** Parse generic type parameters in semantic queries. Investigate first-call warmup gap.

#### AUDIT-30: `find_base_members` empty for implicit interface implementations
- **Tool:** `find_base_members`
- **Input (ITChatBot.sln):** `SysLogServerAdapter.Describe` method (implicit interface implementation)
- **Expected:** Returns `ISourceAdapter.Describe()` as the base member
- **Actual:** Empty array
- **Reproducibility:** Always (for implicit interface implementations; works correctly for `override` methods on classes)
- **Impact:** Cannot trace implicit interface implementations back to their interface declaration.
- **Recommendation:** Resolve interface members for implicit implementations, not just explicit overrides.

---

### LOW Severity

#### AUDIT-06: `server_info` surface counts don't match actual surface
- **Tool:** `server_info`
- **Actual:** Reports 116 tools (49+67), 6 resources, 16 prompts. Each category is off by exactly 1 from the deep-review prompt's reference (117/7/17).
- **Reproducibility:** Always (confirmed across 4 solutions)
- **Note:** The server's self-reported counts should be treated as authoritative. Prompt reference may reflect a different build.

#### AUDIT-07: `project_graph` TargetFrameworks = "unknown" for many projects
- **Tool:** `workspace_load`, `project_graph`, `workspace_status`
- **Repro 1 (Roslyn-Backed-MCP.sln):** "unknown" for 4/6 projects.
- **Repro 2 (NetworkDocumentation.sln):** "unknown" for all 8 projects.
- **Root cause:** MSBuildWorkspace may not fully evaluate TFMs from imported Directory.Build.props or multi-TFM configurations.
- **Reproducibility:** Always (confirmed across 2 solutions; ITChatBot not explicitly tested)

#### AUDIT-08: `list_analyzers` and `project_diagnostics` output too large with no effective pagination
- **Tool:** `list_analyzers`, `project_diagnostics`
- **list_analyzers:** 175K chars (Roslyn-Backed-MCP), 219K chars (NetworkDocumentation), 252K chars (ITChatBot). `project` filter barely helps since analyzers are workspace-level.
- **project_diagnostics:** 626K chars on ITChatBot (1,107 analyzer warnings). Accepts `severity` and `project` filters but no `offset`/`limit`.
- **Reproducibility:** Always (for projects with many analyzers/diagnostics)
- **Recommendation:** Add `limit`/`offset` pagination to both tools.

#### AUDIT-09: `build_workspace` diagnostics count inconsistencies
- **Tool:** `build_workspace`
- **Repro 1 (Roslyn-Backed-MCP.sln):** CS0414 appears twice in structured `Diagnostics` array (parsed from two separate stdout lines).
- **Repro 2 (ITChatBot.sln):** MSBuild stdout reports 91 warnings; structured array contains 7 unique warnings. Discrepancy due to MSBuild retry warnings being deduplicated differently.
- **Reproducibility:** Always
- **Recommendation:** Deduplicate diagnostics by ID+file+line+column. Consider adding a `totalMSBuildWarnings` count alongside the deduplicated list.

#### AUDIT-10: `split_class_preview` reformats unrelated code
- **Tool:** `split_class_preview`
- **Repro 1 (Roslyn-Backed-MCP.sln):** Whitespace reformatting in original file during partial split.
- **Repro 2 (NetworkDocumentation.sln):** Collapses alignment padding, removes blank lines, reformats LINQ chains, alters XML doc `cref` spacing. Also duplicates full XML doc block into new partial file.
- **Repro 3 (ITChatBot.sln):** Collapses multi-line method calls to single lines.
- **Reproducibility:** Always (confirmed across 3 solutions)
- **Recommendation:** Apply minimal changes — add `partial`, remove members, preserve existing formatting.

#### AUDIT-11: `source_generated_documents` shows duplicates
- **Tool:** `source_generated_documents`
- **Actual:** Duplicate entries for Debug/Release/platform obj folders (e.g., `GlobalUsings.g.cs` appears multiple times)
- **Reproducibility:** Always
- **Recommendation:** Deduplicate by HintName per project.

#### AUDIT-12: `add_package_reference_preview` XML formatting
- **Tool:** `add_package_reference_preview`
- **Actual:** `<PackageReference>` element inserted inline without line breaks or indentation. On ITChatBot, element placed on same line as `</ItemGroup>` closing tag.
- **Reproducibility:** Always (confirmed across 2 solutions)

#### AUDIT-13: `goto_type_definition` unhelpful error for primitives
- **Tool:** `goto_type_definition`
- **Input:** `int _unusedField` (a primitive type)
- **Expected:** Descriptive message like "Cannot navigate to type definition for built-in types"
- **Actual:** Generic "No type definition found"
- **Reproducibility:** Always (for primitive/built-in types)

#### AUDIT-14: Resource listing omits dynamic workspace resources
- **Tool:** `ListMcpResourcesTool`
- **Actual:** Only 2 of 6+ resources listed (`roslyn://workspaces`, `roslyn://server/catalog`). Workspace-scoped resources like `roslyn://workspace/{id}/status` are not discoverable despite functioning correctly when read directly.
- **Reproducibility:** Always

#### AUDIT-15: Resource DTO property name inconsistency
- **Tool:** Resources `roslyn://workspace/{id}/status` vs `roslyn://workspace/{id}/projects`
- **Actual:** Status resource uses `Name` for project name; projects resource uses `ProjectName`
- **Reproducibility:** Always

#### AUDIT-20: `find_unused_symbols` reports source-generated and NuGet content code as unused
- **Tool:** `find_unused_symbols`
- **Repro 1 (NetworkDocumentation.sln):** Reports `UptimeRegexGenerated` (Regex source gen), `AutoGeneratedProgram` (NuGet test SDK), `InterceptsLocationAttribute`, OpenAPI source gen symbols.
- **Repro 2 (Roslyn-Backed-MCP.sln):** Reports `AutoGeneratedProgram` from NuGet test SDK.
- **Repro 3 (ITChatBot.sln):** 11 identical `XunitAutoGeneratedEntryPoint` entries from `xunit.v3.core` NuGet content file (one per test project).
- **Root cause:** NuGet content files (`_content/` paths) and source-generated files (`obj/` paths) are included in the workspace and not filtered.
- **Reproducibility:** Always (confirmed across 3 solutions)
- **Recommendation:** Exclude `obj/` paths and NuGet `_content/` paths from results by default.

#### AUDIT-24: `test_discover` output too large with no pagination
- **Tool:** `test_discover`
- **Repro 1 (Roslyn-Backed-MCP.sln):** 55,400 characters (143 tests)
- **Repro 2 (NetworkDocumentation.sln):** 347,458 characters
- **Repro 3 (ITChatBot.sln):** 171,768 characters (425 tests)
- **Reproducibility:** Always
- **Recommendation:** Add `limit`, `project`, or `pattern` filter parameters.

#### AUDIT-25: `get_namespace_dependencies` no circular-only filter
- **Tool:** `get_namespace_dependencies`
- **Actual:** Returns full dependency graph (65.5K chars on NetworkDocumentation.sln) when often only circular dependencies are needed
- **Reproducibility:** Always
- **Recommendation:** Add `circularOnly=true` filter option, or add summary counts (`circularCount`, `nodeCount`, `edgeCount`) at top level.

#### AUDIT-31: `project_diagnostics` vs `compile_check` report different diagnostic categories
- **Tool:** `project_diagnostics`, `compile_check`
- **Input (ITChatBot.sln):** Both with no filters
- **Expected:** Consistent diagnostic counts
- **Actual:** `compile_check` reports 2 compiler warnings (CS*). `project_diagnostics` reports 2 compiler warnings + 1,107 analyzer warnings (CA*) in separate sections.
- **Reproducibility:** Always
- **Impact:** Not a bug — `compile_check` only runs Roslyn compilation, while `project_diagnostics` includes analyzer diagnostics. But the difference is not obvious from tool descriptions.
- **Recommendation:** `compile_check` description should mention it excludes analyzer diagnostics, or add an `includeAnalyzers` parameter.

#### AUDIT-32: `get_syntax_tree` exceeds output for large methods
- **Tool:** `get_syntax_tree`
- **Input (ITChatBot.sln):** `ChatOrchestrator.cs`, lines 93-324, `maxDepth=3`
- **Actual:** 142K+ characters, saved to temp file
- **Reproducibility:** Always (for methods >100 LOC at depth 3)
- **Workaround:** Use smaller line ranges or `maxDepth=2`.

#### AUDIT-33: `analyze_data_flow` variable names not scope-disambiguated
- **Tool:** `analyze_data_flow`
- **Input (ITChatBot.sln):** `RetrievalPlanner.CreatePlan`, lines 38-217
- **Expected:** Unique variable identifiers or scope qualifiers
- **Actual:** Repeated names like `desc`, `adapter` appear multiple times in `VariablesDeclared` without scope disambiguation
- **Reproducibility:** Always (for methods with multiple loop scopes declaring same-named variables)
- **Recommendation:** Add scope/line info to each entry in the flat list format.

#### AUDIT-34: `find_consumers` misses DI generic argument registration pattern
- **Tool:** `find_consumers`
- **Input (ITChatBot.sln):** `ClaimsUserIdentityResolver` at its declaration
- **Expected:** Program.cs listed as consumer (DI registration via `AddSingleton<IUserIdentityResolver, ClaimsUserIdentityResolver>`)
- **Actual:** Only `ClaimsUserIdentityResolverTests` found as consumer. `find_references` does capture the DI registration.
- **Reproducibility:** Always (for DI generic argument patterns)
- **Impact:** Minor gap — `find_references` provides coverage. `find_consumers` specifically misses the generic type argument usage pattern in DI registrations.

#### AUDIT-35: `find_unused_symbols` no confidence scoring for public symbols
- **Tool:** `find_unused_symbols`
- **Input (ITChatBot.sln):** `includePublic=true`
- **Expected:** Symbols flagged with confidence levels
- **Actual:** 50 results including public enum fields (e.g., `Sensitivity.Public`, `EvidenceKind.Ticket`) and DTO properties that are part of the public contract surface. No distinction between "no internal refs" and "truly dead".
- **Reproducibility:** Always
- **Recommendation:** Add confidence scoring (high/medium/low). Consider filtering out enum members and DTO properties by default, or providing an `excludeEnums` / `excludeRecordProperties` option.

---

### INFORMATIONAL

#### AUDIT-16: `analyze_snippet` error line numbers offset by wrapper
- **Tool:** `analyze_snippet`
- **Observation:** Error positions are reported relative to the internal wrapper code (e.g., line 6 for the first line of user code) because the snippet engine prepends implicit `using` directives. Not a bug — design trade-off.

#### AUDIT-17: `type_hierarchy` returns null for implicit base types
- **Tool:** `type_hierarchy`
- **Observation:** `BaseTypes: null` for classes that implicitly inherit from `object`. Technically correct but could show `System.Object` for completeness.

#### AUDIT-18: `get_operations` results are position-sensitive
- **Tool:** `get_operations`
- **Observation:** Landing on a simple variable reference yields `LocalReference`; landing on a method call yields a rich `Invocation` tree. Expected behavior — the tool returns the IOperation for the exact syntax node at the cursor position. Column position must target the specific expression to get the full operation tree. Documentation should clarify this positional behavior.

#### AUDIT-26: `compile_check` with `emitValidation` is significantly slower
- **Tool:** `compile_check`
- **Measurements:** 129ms → 1518ms (11.7x) on NetworkDocumentation.sln; 56ms → 998ms (17.8x) on ITChatBot.sln. Expected behavior — emit validation performs full assembly generation.
- **Recommendation:** Documentation should note this performance difference and recommend non-emit path for iterative loops.

#### AUDIT-27: `analyze_snippet` `usings` parameter adds directives but not assembly references
- **Tool:** `analyze_snippet`
- **Observation:** Adding `System.Text.Json` via the `usings` parameter adds the `using` directive but does not add the assembly reference, so types from that namespace fail to resolve. Design limitation of the ephemeral workspace approach.

#### AUDIT-36: `get_cohesion_metrics` dominated by test classes
- **Tool:** `get_cohesion_metrics`
- **Input (ITChatBot.sln):** `minMethods=3`
- **Observation:** Top results are all test classes (e.g., `TextSimilarityTests` LCOM4=15) which naturally have high LCOM4 because test methods don't share state. Production types are buried. Not a bug — test classes inherently have independent methods.
- **Recommendation:** Add `excludeTests` filter parameter or sort production and test results separately.

---

## Positive Findings

1. **Zero crashes** across ~300 tool calls on 4 solutions (6-23 projects each). Server is stable under load.
2. **Large output handling** works well — outputs exceeding token limits are automatically persisted to disk with preview snippets.
3. **Preview/apply pattern** works correctly across all refactoring tools tested.
4. **`revert_last_apply`** handles the no-op case gracefully with a clear message.
5. **Cross-project reference validation** correctly prevents circular references (`extract_interface_cross_project_preview` on Roslyn-Backed-MCP.sln).
6. **`compile_check`** is fast (29-129ms without emit) and consistent with `project_diagnostics` for compiler diagnostics across all 4 solutions.
7. **`evaluate_csharp`** handles all scenarios cleanly: success, runtime errors, and multi-line scripts.
8. **`security_diagnostics`** and **`security_analyzer_status`** correctly identify installed analyzers and recommend missing packages consistently. Found real security issue (MD5 usage with OWASP categorization) on ITChatBot.
9. **`semantic_search`** produces accurate results for non-generic natural-language queries.
10. **`find_consumers`** and **`find_references`** return complementary, consistent data.
11. **`project_diagnostics`**, **`compile_check`**, and **`build_workspace`** are consistent across all 4 solutions.
12. **Phase 3-4 deep analysis** completed without errors on all solutions tested. `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `callers_callees` all produced correct results. Line number accuracy confirmed correct across all tools — no off-by-one issues.
13. **Navigation tools** (`go_to_definition`, `enclosing_symbol`, `get_completions`, `find_references_bulk`, `find_implementations`) all work correctly. `find_implementations` found all 4 `ISourceAdapter.QueryAsync` implementations on ITChatBot (2 production, 2 test).
14. **`scaffold_type_preview`** generates correct minimal skeletons with proper namespaces.
15. **`add_package_reference_preview`** and **`remove_package_reference_preview`** correctly handle CPM (central package management).
16. **`rename_preview`** excellent cross-project coverage — found 13 references across 10 files on ITChatBot.
17. **`remove_dead_code_preview`** clean targeted removal verified on ITChatBot.
18. **Cross-tool consistency** confirmed: 59+ symbol analysis calls on ITChatBot with zero contradictions between `find_references`, `find_type_usages`, `symbol_relationships`, `type_hierarchy`, and `symbol_info`.
19. **`get_di_registrations`** correctly found all registrations across all solutions (52 on Roslyn-Backed-MCP, 5 on NetworkDocumentation, 49 on ITChatBot).

---

## Performance Observations

| Tool | Roslyn-Backed-MCP | NetworkDocumentation | ITChatBot |
|------|---:|---:|---:|
| `workspace_load` | ~2s | ~3s | ~3s |
| `compile_check` (no emit) | 29ms | 129ms | 56ms |
| `compile_check` (emit) | — | 1518ms | 998ms |
| `evaluate_csharp` | 196ms (first), 19-38ms (cached) | — | 19ms |
| `get_editorconfig_options` | — | — | ~100ms |
| `get_di_registrations` | ~1s | ~1s | ~1s |
| `semantic_search` | — | — | ~500ms |

---

## Tool Coverage Summary

### Tools Exercised (45+ distinct tools)

- **Workspace:** workspace_load, workspace_status, workspace_list, workspace_reload, project_graph, server_info, get_source_text
- **Diagnostics:** project_diagnostics, compile_check (both modes), security_diagnostics, security_analyzer_status, list_analyzers
- **Metrics:** get_complexity_metrics, get_cohesion_metrics, get_namespace_dependencies, get_nuget_dependencies
- **Dead Code:** find_unused_symbols (private + public), remove_dead_code_preview
- **Symbols:** symbol_info, document_symbols, type_hierarchy, find_references, find_references_bulk, find_consumers, callers_callees, find_overrides, find_base_members, find_shared_members, find_type_usages, symbol_relationships, semantic_search, find_reflection_usages, get_di_registrations, source_generated_documents
- **Navigation:** go_to_definition, goto_type_definition, enclosing_symbol, get_completions, find_implementations
- **Flow Analysis:** analyze_data_flow, analyze_control_flow, get_operations, get_syntax_tree
- **Snippets:** analyze_snippet (3 kinds), evaluate_csharp (3 scenarios)
- **Refactoring:** rename_preview, fix_all_preview, get_code_actions, organize_usings_preview, format_document_preview, extract_interface_preview
- **Cross-Project:** move_type_to_file_preview, split_class_preview, extract_interface_cross_project_preview
- **Scaffolding:** scaffold_type_preview, scaffold_test_preview
- **Project Mutation:** add_package_reference_preview, remove_package_reference_preview, set_project_property_preview
- **EditorConfig:** get_editorconfig_options
- **Build/Test:** build_workspace, test_discover, test_run, test_coverage
- **Undo:** revert_last_apply
- **Resources:** roslyn://server/catalog, roslyn://workspaces, roslyn://workspace/{id}/status, roslyn://workspace/{id}/projects

---

## Comparison with Previous Audit (2026-03-28)

The previous audit reported 29 issues across 3 solutions. This consolidated audit found 35 issues across 4 solutions.

- **Resolved since last audit:** Data quality issues in metrics, diagnostics, and syntax tree fixed in v1.2.0 (PRs #39-#42). `revert_last_apply` crash fixed. `callers_callees` async resolution fixed. `test_run` skipped count fixed.
- **New findings this cycle:** AUDIT-28 (interface conflict detection), AUDIT-29 (reflection NuGet pollution), AUDIT-30 (find_base_members implicit impls), AUDIT-31-36 (category gaps, output limits, scope disambiguation, consumer detection, confidence scoring, test class noise).
- **Persistent across audits:** TargetFrameworks "unknown" (AUDIT-07), large output pagination gaps (AUDIT-08), scaffold_test_preview wrong type (AUDIT-01), NuGet content file pollution (AUDIT-20, AUDIT-29).
- **Worsening with solution size:** Output size issues (AUDIT-08, AUDIT-24) scale with project count. ITChatBot (23 projects) produces significantly larger outputs than smaller solutions.

---

## Action Items

| Priority | Issue | Action |
|----------|-------|--------|
| P1 | AUDIT-01 | Fix scaffold_test_preview type resolution and remove hardcoded SampleLib import |
| P1 | AUDIT-22 | Fix extract_interface_cross_project_preview namespace to use target project |
| P1 | AUDIT-28 | Add existing-interface conflict detection to extract_interface_cross_project_preview |
| P1 | AUDIT-29 | Filter NuGet content files from find_reflection_usages results |
| P2 | AUDIT-02 | Return structured error from test_run when no test projects found |
| P2 | AUDIT-05 | Fix find_overrides token span resolution |
| P2 | AUDIT-19 | Deduplicate find_unused_symbols results by symbolHandle |
| P2 | AUDIT-21 | Load IDE/CA analyzers for fix_all_preview or return workaround message |
| P2 | AUDIT-23 | Support generic type parameters in semantic_search queries; investigate warmup gap |
| P2 | AUDIT-30 | Support implicit interface implementations in find_base_members |
| P3 | AUDIT-03 | Clarify move_type_to_file_preview error for nested types |
| P3 | AUDIT-04 | Return explicit no-op message from set_project_property_preview; handle missing PropertyGroup |
| P3 | AUDIT-07 | Fix TargetFrameworks resolution for Directory.Build.props |
| P3 | AUDIT-08 | Add pagination to list_analyzers and project_diagnostics |
| P3 | AUDIT-09 | Deduplicate/clarify build_workspace diagnostics |
| P3 | AUDIT-10 | Limit split_class_preview reformatting to moved members only |
| P3 | AUDIT-20 | Filter source-generated and NuGet content files from find_unused_symbols |
| P3 | AUDIT-24 | Add pagination/filters to test_discover |
| P3 | AUDIT-35 | Add confidence scoring to find_unused_symbols for public symbols |
