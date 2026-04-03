# Deep Code Review & Refactor Agent Prompt

<!-- DO NOT DELETE THIS FILE.
     This is a living document that must be maintained and kept in sync
     with the project's actual tool, resource, and prompt surface at all times.
     When tools, resources, or prompts are added, removed, or renamed,
     update the Tools Reference appendix accordingly.
     Last surface audit: 2026-04-03 (116 tools, 6 resources, 16 prompts). -->

> Use this prompt with an AI coding agent that has access to the Roslyn MCP server.
> It exercises every major tool category and surfaces both codebase issues AND MCP server bugs.

---

## Prompt

You are a senior .NET architect performing a comprehensive code review and refactoring pass on a C# solution. You have access to a Roslyn MCP server that provides semantic analysis, refactoring, and validation tools. Your job is twofold:

1. **Review and refactor the codebase** using every available tool
2. **Audit the MCP server itself** — report any tool failures, incorrect results, crashes, timeouts, or unexpected behavior as bugs

Work through each phase below **in order**. After each tool call, evaluate whether the result is correct and complete. If a tool returns an error, empty results when data was expected, or results that seem wrong, log it in a `## MCP Server Issues` section at the end of your report.

---

### Phase 0: Setup

1. Call `workspace_load` with the solution path.
2. Call `workspace_list` to confirm the session appears.
3. Call `workspace_status` to confirm the workspace loaded cleanly. Note any load errors.
4. Call `server_info` to record the server version, Roslyn version, and tool counts.
5. Call `project_graph` to understand the project dependency structure.

**MCP audit checkpoint:** Did `workspace_load` succeed? Does `workspace_list` show the new session? Did `workspace_status` report any stale documents or missing projects? Does `server_info` report the expected tool counts?

---

### Phase 1: Broad Diagnostics Scan

1. Call `project_diagnostics` (no filters) to get all compiler errors and warnings across the solution.
2. Call `compile_check` (no filters) to get in-memory compilation diagnostics. Compare the results with `project_diagnostics` — they should be consistent but may differ in count or detail.
3. Call `security_diagnostics` to identify security-relevant findings with OWASP categorization.
4. Call `security_analyzer_status` to check which analyzer packages are installed and what's missing.
5. Call `list_analyzers` to enumerate all loaded analyzers and their rules. Note total rule count.
6. Call `diagnostic_details` on a representative error and a representative warning to inspect full detail.

**MCP audit checkpoint:** Do `project_diagnostics` and `compile_check` agree on error counts? Does `compile_check` with `emitValidation=true` find additional issues? Does `list_analyzers` return data for all projects? Are there any analyzers that fail to load (look for LOAD_ERROR entries)? Does `diagnostic_details` return accurate location and message information?

---

### Phase 2: Code Quality Metrics

1. Call `get_complexity_metrics` with no filters to find the most complex methods across the solution. Note any with cyclomatic complexity > 10 or nesting depth > 4.
2. Call `get_cohesion_metrics` with `minMethods=3` to find types with LCOM4 > 1 (SRP violations).
3. Call `find_unused_symbols` with `includePublic=false` to detect dead code.
4. Call `find_unused_symbols` with `includePublic=true` and compare — are there public APIs with zero internal references?
5. Call `get_namespace_dependencies` to check for circular namespace dependencies.
6. Call `get_nuget_dependencies` to audit package references across projects.

**MCP audit checkpoint:** Do complexity metrics make sense for the methods shown? Are LCOM4 scores reasonable (do types with score=1 actually look cohesive when you read them)? Does `find_unused_symbols` miss any obviously dead code or falsely flag used symbols?

---

### Phase 3: Deep Symbol Analysis (pick 3-5 key types)

For each key type in the solution:

1. Call `symbol_search` to locate the type by name.
2. Call `symbol_info` to understand the type.
3. Call `document_symbols` on its file to see its member structure.
4. Call `type_hierarchy` to understand inheritance.
5. Call `find_references` to see how widely it's used.
6. Call `find_consumers` to classify dependency kinds (constructor, field, parameter, etc.).
7. Call `find_shared_members` to identify private members shared across public methods.
8. Call `find_type_mutations` to find all mutating members and their external callers.
9. Call `find_type_usages` to see how the type is used (return types, parameters, fields, casts, etc.).
10. Call `callers_callees` on 2-3 of its methods to map call chains.
11. Call `find_property_writes` on any settable properties to classify init vs post-construction writes.
12. Call `member_hierarchy` on overridden/implemented members.
13. Call `symbol_relationships` for a combined view.
14. Call `symbol_signature_help` on a key method to verify signature documentation.
15. Call `impact_analysis` on a symbol you might refactor to estimate change blast radius.

**MCP audit checkpoint:** Are `find_references` and `find_consumers` consistent? Does `find_type_usages` categorize correctly? Do `callers_callees` results match what you'd expect from reading the code? Does `symbol_relationships` combine data correctly from its sub-tools? Does `impact_analysis` produce a reasonable blast radius estimate?

---

### Phase 4: Flow Analysis (pick 3-5 complex methods)

For each method with high complexity:

1. Call `get_source_text` to read the file.
2. Call `analyze_data_flow` on the method body (startLine to endLine). Examine:
   - Variables that flow in but are never read inside (unused parameters?)
   - Variables written inside but never read outside (dead assignments?)
   - Captured variables (closure risks)
3. Call `analyze_control_flow` on the same range. Examine:
   - Is `EndPointIsReachable` false where it should be true (or vice versa)?
   - Are there unreachable exit points?
   - Do return statements cover all paths?
4. Call `get_operations` on key expressions within the method (assignments, invocations, conditionals) to get the IOperation tree. Look for:
   - Implicit conversions that may lose data
   - Null-conditional chains that could be simplified
   - Patterns that indicate code smell (long invocation chains, nested conditionals)
5. Call `get_syntax_tree` on the method's line range to compare syntax structure with the IOperation view.

**MCP audit checkpoint:** Does `analyze_data_flow` correctly identify all variables? Are the `Captured` and `CapturedInside` lists accurate for lambdas? Does `analyze_control_flow` correctly detect unreachable code? Does `get_operations` return a sensible tree or does it miss operations? Do line numbers in results map correctly to the source?

---

### Phase 5: Snippet & Script Validation

1. Call `analyze_snippet` with `kind="expression"` using a simple expression like `1 + 2`. Verify it reports no errors.
2. Call `analyze_snippet` with `kind="program"` using a small class definition. Verify declared symbols are listed.
3. Call `analyze_snippet` with intentionally broken code. Verify it reports the expected compilation error.
4. Call `evaluate_csharp` with a simple expression like `Enumerable.Range(1, 10).Sum()`. Verify the result is `55`.
5. Call `evaluate_csharp` with a multi-line script. Verify execution.
6. Call `evaluate_csharp` with code that should produce a runtime error (e.g., `int.Parse("abc")`). Verify it reports the error gracefully.
7. Call `evaluate_csharp` with an infinite loop. Verify the timeout fires and doesn't hang the server.

**MCP audit checkpoint:** Does `analyze_snippet` correctly wrap different `kind` values? Are compilation errors accurate and well-formatted? Does `evaluate_csharp` handle runtime errors without crashing? Does the timeout work? Are result types correctly identified?

---

### Phase 6: Refactoring Pass

#### 6a. Fix All Diagnostics
1. Pick a diagnostic ID that appears many times (e.g., IDE0005 unused usings, CS8600 nullable).
2. Call `fix_all_preview` with `scope="solution"` and that diagnostic ID.
3. Examine the preview — are all instances found? Is the diff correct?
4. Call `fix_all_apply` with the preview token.
5. Call `compile_check` to verify the fix didn't break anything.

#### 6b. Rename
1. Pick a poorly-named symbol.
2. Call `rename_preview` with a better name.
3. Verify the preview shows all reference updates.
4. Call `rename_apply`.
5. Call `compile_check`.

#### 6c. Extract Interface (if a type with consumers was found in Phase 3)
1. Call `extract_interface_preview` with selected public members.
2. Examine the generated interface file in the preview.
3. Call `extract_interface_apply`.
4. Call `bulk_replace_type_preview` to update consumers from concrete to interface.
5. Call `bulk_replace_type_apply`.
6. Call `compile_check`.

#### 6d. Extract Type (if LCOM4 > 1 types were found in Phase 2)
1. Call `find_shared_members` on the type.
2. Call `extract_type_preview` with the independent cluster's members.
3. Call `extract_type_apply`.
4. Call `compile_check`.

#### 6e. Format & Organize
1. Call `format_range_preview` on a recently-edited region.
2. Call `format_range_apply`.
3. Call `format_document_preview` on an entire file that was heavily modified.
4. Call `format_document_apply`.
5. Call `organize_usings_preview` on files that had code fix changes.
6. Call `organize_usings_apply`.

#### 6f. Curated Code Fixes
1. Call `code_fix_preview` on a specific diagnostic occurrence (e.g., a nullable warning).
2. Examine the preview diff.
3. Call `code_fix_apply`.
4. Call `compile_check`.

#### 6g. Code Actions
1. Call `get_code_actions` at a position with available refactorings.
2. Call `preview_code_action` on an interesting action.
3. Call `apply_code_action`.

#### 6h. Direct Text Edits
1. Call `apply_text_edit` to make a small targeted edit to a single file.
2. Call `apply_multi_file_edit` to make coordinated edits across multiple files.
3. Call `compile_check`.

#### 6i. Dead Code Removal
1. From Phase 2's `find_unused_symbols` results, pick symbols confirmed as dead.
2. Call `remove_dead_code_preview` with their symbol handles.
3. Call `remove_dead_code_apply`.
4. Call `compile_check`.

**MCP audit checkpoint:** Does `fix_all_preview` find ALL instances? Does it crash or timeout on large scopes? Does `rename_preview` catch references in comments or strings? Does `extract_interface_preview` generate valid C# syntax? Does `bulk_replace_type_preview` miss any usages? Does `extract_type_preview` handle shared private members correctly? Does `format_range_preview` format only the specified range or does it bleed? Does `remove_dead_code_preview` correctly remove only the targeted symbols? After each apply, does `compile_check` pass?

---

### Phase 7: EditorConfig & Configuration

1. Pick a source file and call `get_editorconfig_options` on it.
2. Verify the returned options match what you'd expect from the .editorconfig files in the repo.
3. Check if key options like `csharp_style_var_for_built_in_types`, `indent_style`, and `dotnet_sort_system_directives_first` are present and correct.

**MCP audit checkpoint:** Does `get_editorconfig_options` return options? Are the values accurate? Does it find the correct .editorconfig file path? Are there options that should appear but don't?

---

### Phase 8: Build & Test Validation

1. Call `workspace_reload` to refresh the workspace after all refactoring file changes.
2. Call `build_workspace` to do a full MSBuild build after all refactoring.
3. Call `build_project` on individual projects that were modified.
3. Call `test_discover` to find all tests.
4. Call `test_related_files` with the list of files you modified during refactoring.
5. Call `test_related` on a symbol you refactored to find tests targeting it specifically.
6. Call `test_run` with the filter from `test_related_files` to run affected tests.
7. Call `test_run` with no filter to run the full suite.
8. Call `test_coverage` to measure coverage.

**MCP audit checkpoint:** Does `build_workspace` match `compile_check` results? Does `test_related_files` correctly identify related tests? Does `test_related` find the right tests for the symbol? Does `test_run` produce structured results with pass/fail counts? Does `test_coverage` produce coverage data?

---

### Phase 9: Undo Verification

1. Call `revert_last_apply` to undo the most recent refactoring.
2. Call `compile_check` to verify the revert was clean.
3. Note: only Roslyn solution-level changes can be reverted.

**MCP audit checkpoint:** Does `revert_last_apply` actually restore the previous state? Does it handle the case where there's nothing to revert? Does it report what was undone?

When finished with all phases, call `workspace_close` to release the session.

---

### Phase 10: Cross-Project Operations (if multi-project solution)

1. Call `move_type_to_file_preview` to move a type into its own file.
2. Call `move_type_to_file_apply` to apply the move.
3. Call `move_file_preview` to move a file to a different directory with namespace update.
4. Call `move_file_apply` to apply the file move.
5. Call `create_file_preview` to preview creating a new source file in a project.
6. Call `create_file_apply` to apply the file creation.
7. Call `delete_file_preview` to preview deleting an unused source file.
8. Call `delete_file_apply` to apply the file deletion.
9. Call `extract_interface_cross_project_preview` to extract an interface into a different project.
10. Call `dependency_inversion_preview` to extract interface + update constructor dependencies.
11. Call `move_type_to_project_preview` to move a type declaration into another project.
12. Call `extract_and_wire_interface_preview` to extract an interface and rewrite DI registrations.
13. Call `split_class_preview` to split a type into partial class files.
14. Call `migrate_package_preview` to replace one NuGet package with another across projects.
15. For orchestration previews (steps 9-14), call `apply_composite_preview` to apply.
16. Call `compile_check` after each apply to verify correctness.

**MCP audit checkpoint:** Do cross-project operations correctly add project references? Do namespace updates apply? Are all references updated? Does `extract_and_wire_interface_preview` correctly identify DI registrations? Does `split_class_preview` produce valid partial classes? Do file creation and deletion previews produce correct diffs?

---

### Phase 11: Semantic Search & Discovery

1. Call `semantic_search` with a natural-language query like `"async methods returning Task<bool>"`. Verify results match the query semantics.
2. Call `semantic_search` with `"classes implementing IDisposable"`. Cross-check against `find_implementations`.
3. Call `find_reflection_usages` to locate reflection-heavy code that may resist refactoring.
4. Call `get_di_registrations` to audit the DI container wiring.
5. Call `source_generated_documents` to list any source-generator output files.

**MCP audit checkpoint:** Does `semantic_search` return relevant results? Does `find_reflection_usages` find all reflection patterns (typeof, GetMethod, Activator, etc.)? Does `get_di_registrations` parse all registration styles?

---

### Phase 12: Scaffolding

1. Call `scaffold_type_preview` to scaffold a new class in a project. Verify the generated file content and namespace.
2. Call `scaffold_type_apply` to apply the scaffolded type.
3. Call `scaffold_test_preview` to scaffold a test file for an existing type. Verify it targets the correct type and uses the right test framework.
4. Call `scaffold_test_apply` to apply the scaffolded test.
5. Call `compile_check`.

**MCP audit checkpoint:** Does `scaffold_type_preview` infer the correct namespace from the project structure? Does `scaffold_test_preview` generate valid test stubs? Do the apply operations write files correctly?

---

### Phase 13: Project Mutation

1. Call `add_package_reference_preview` to add a NuGet package to a project.
2. Call `remove_package_reference_preview` to remove a NuGet package.
3. Call `add_project_reference_preview` to add a project-to-project reference.
4. Call `remove_project_reference_preview` to remove a project reference.
5. Call `set_project_property_preview` to set a project property (e.g., Nullable, LangVersion).
6. Call `set_conditional_property_preview` to set a conditional project property scoped to a configuration.
7. Call `add_target_framework_preview` to add a target framework to a multi-targeting project.
8. Call `remove_target_framework_preview` to remove a target framework.
9. If the solution uses Central Package Management, call `add_central_package_version_preview` and `remove_central_package_version_preview`.
10. Call `apply_project_mutation` to apply each previewed mutation.
11. Call `compile_check` after each apply to verify the mutations.

**MCP audit checkpoint:** Do project mutation previews produce correct XML diffs? Does `apply_project_mutation` write valid project files? Do `set_conditional_property_preview` conditions evaluate correctly? Do framework additions/removals update the TargetFrameworks property correctly? Do subsequent `compile_check` calls reflect the mutations?

---

### Phase 14: Navigation & Completions

1. Call `go_to_definition` on a symbol usage. Verify it navigates to the correct declaration.
2. Call `goto_type_definition` on a variable. Verify it goes to the type, not the variable declaration.
3. Call `enclosing_symbol` at a position inside a method. Verify it returns the method.
4. Call `get_completions` at a position after a dot. Verify IntelliSense-style results.
5. Call `find_references_bulk` with multiple symbol handles. Verify batch results match individual `find_references` calls.
6. Call `find_overrides` on a virtual method. Verify all overriding members are found.
7. Call `find_base_members` on an override. Verify it navigates back to the base declaration.

**MCP audit checkpoint:** Are navigation results accurate? Does `get_completions` return relevant members? Does `find_references_bulk` produce consistent results vs individual calls?

---

### Phase 15: Resource Verification

1. Read `roslyn://server/catalog` to get the machine-readable surface inventory.
2. Read `roslyn://workspaces` to list active workspace sessions.
3. Read `roslyn://workspace/{workspaceId}/status` and compare with `workspace_status` tool output.
4. Read `roslyn://workspace/{workspaceId}/projects` and compare with `project_graph` tool output.
5. Read `roslyn://workspace/{workspaceId}/diagnostics` and compare with `project_diagnostics` tool output.
6. Read `roslyn://workspace/{workspaceId}/file/{filePath}` for a source file and compare with `get_source_text`.

**MCP audit checkpoint:** Do resources return data consistent with their tool counterparts? Are URI templates resolved correctly? Is the server catalog accurate and up to date?

---

## Output Format

Produce two documents:

### Document 1: Code Review & Refactoring Report
- Executive summary of codebase health
- Issues found by category (security, complexity, cohesion, dead code, etc.)
- Refactorings performed and their impact
- Remaining items that need manual attention
- Metrics before and after (diagnostic counts, complexity, LCOM4, test coverage)

### Document 2: MCP Server Audit Report
For each issue found, include:
- **Tool name** that exhibited the issue
- **Input parameters** that triggered it
- **Expected behavior** vs **actual behavior**
- **Severity**: crash / incorrect result / missing data / degraded performance / cosmetic
- **Reproducibility**: always / sometimes / once

Categories:
- Tool crashes or unhandled exceptions
- Incorrect or incomplete results
- Inconsistencies between tools that should agree
- Missing functionality or edge cases not handled
- Performance issues (tools that take unreasonably long)
- Serialization issues (malformed JSON, truncated output)
- Line number or position mapping errors

---

## Tools Reference — Complete Surface Inventory

> **Maintenance note:** This appendix must be kept in sync with the actual server surface.
> When tools, resources, or prompts change, update this section.
> Last verified: 2026-04-03 — **116 tools | 6 resources | 16 prompts**

### Tools by Category (116 total)

#### Server (1 tool)
`server_info`

#### Workspace (8 tools)
`workspace_load` `workspace_reload` `workspace_close` `workspace_list` `workspace_status` `project_graph` `source_generated_documents` `get_source_text`

#### Symbols (16 tools)
`symbol_search` `symbol_info` `go_to_definition` `goto_type_definition` `find_references` `find_references_bulk` `find_implementations` `find_overrides` `find_base_members` `member_hierarchy` `document_symbols` `enclosing_symbol` `symbol_signature_help` `symbol_relationships` `find_property_writes` `get_completions`

#### Analysis (7 tools)
`project_diagnostics` `diagnostic_details` `type_hierarchy` `callers_callees` `impact_analysis` `find_type_mutations` `find_type_usages`

#### Advanced Analysis (7 tools)
`find_unused_symbols` `get_di_registrations` `get_complexity_metrics` `find_reflection_usages` `get_namespace_dependencies` `get_nuget_dependencies` `semantic_search`

#### Consumer & Cohesion Analysis (3 tools)
`find_consumers` `get_cohesion_metrics` `find_shared_members`

#### Security (2 tools)
`security_diagnostics` `security_analyzer_status`

#### Flow Analysis (2 tools)
`analyze_data_flow` `analyze_control_flow`

#### Syntax & Operations (2 tools)
`get_syntax_tree` `get_operations`

#### Compilation (1 tool)
`compile_check`

#### Analyzer Info (1 tool)
`list_analyzers`

#### Snippet & Scripting (2 tools)
`analyze_snippet` `evaluate_csharp`

#### Refactoring — Rename, Format, Organize (8 tools)
`rename_preview` `rename_apply` `organize_usings_preview` `organize_usings_apply` `format_document_preview` `format_document_apply` `format_range_preview` `format_range_apply`

#### Code Actions & Fixes (5 tools)
`code_fix_preview` `code_fix_apply` `get_code_actions` `preview_code_action` `apply_code_action`

#### Fix All (2 tools)
`fix_all_preview` `fix_all_apply`

#### Interface Extraction (2 tools)
`extract_interface_preview` `extract_interface_apply`

#### Type Extraction (2 tools)
`extract_type_preview` `extract_type_apply`

#### Type Movement (2 tools)
`move_type_to_file_preview` `move_type_to_file_apply`

#### Bulk Refactoring (2 tools)
`bulk_replace_type_preview` `bulk_replace_type_apply`

#### Cross-Project Refactoring (3 tools)
`move_type_to_project_preview` `extract_interface_cross_project_preview` `dependency_inversion_preview`

#### Orchestration (4 tools)
`migrate_package_preview` `split_class_preview` `extract_and_wire_interface_preview` `apply_composite_preview`

#### Dead Code Removal (2 tools)
`remove_dead_code_preview` `remove_dead_code_apply`

#### Text Editing (2 tools)
`apply_text_edit` `apply_multi_file_edit`

#### File Operations (6 tools)
`create_file_preview` `create_file_apply` `delete_file_preview` `delete_file_apply` `move_file_preview` `move_file_apply`

#### Project Mutation (11 tools)
`add_package_reference_preview` `remove_package_reference_preview` `add_project_reference_preview` `remove_project_reference_preview` `set_project_property_preview` `set_conditional_property_preview` `add_target_framework_preview` `remove_target_framework_preview` `add_central_package_version_preview` `remove_central_package_version_preview` `apply_project_mutation`

#### Scaffolding (4 tools)
`scaffold_type_preview` `scaffold_type_apply` `scaffold_test_preview` `scaffold_test_apply`

#### Validation — Build & Test (7 tools)
`build_workspace` `build_project` `test_discover` `test_run` `test_related` `test_related_files` `test_coverage`

#### EditorConfig (1 tool)
`get_editorconfig_options`

#### Undo (1 tool)
`revert_last_apply`

### Resources (6 total)

| URI Template | Description | MIME Type |
|---|---|---|
| `roslyn://server/catalog` | Machine-readable surface inventory and support policy | `application/json` |
| `roslyn://workspaces` | List active workspace sessions | `application/json` |
| `roslyn://workspace/{workspaceId}/status` | Workspace status and diagnostics | `application/json` |
| `roslyn://workspace/{workspaceId}/projects` | Project graph metadata | `application/json` |
| `roslyn://workspace/{workspaceId}/diagnostics` | Compiler diagnostics | `application/json` |
| `roslyn://workspace/{workspaceId}/file/{filePath}` | Source file content | `text/x-csharp` |

### Prompts (16 total)

| Prompt | Description |
|---|---|
| `explain_error` | Explain a compiler diagnostic |
| `suggest_refactoring` | Suggest refactorings for a symbol or region |
| `review_file` | Review a source file |
| `analyze_dependencies` | Architecture and dependency analysis |
| `debug_test_failure` | Debug a failing test |
| `refactor_and_validate` | Preview-first refactoring with validation |
| `fix_all_diagnostics` | Batched diagnostic cleanup |
| `guided_package_migration` | Package migration across projects |
| `guided_extract_interface` | Interface extraction and consumer updates |
| `security_review` | Comprehensive security review |
| `discover_capabilities` | Discover relevant tools for a task category |
| `dead_code_audit` | Dead code detection and removal |
| `review_test_coverage` | Test coverage review and gap identification |
| `review_complexity` | Complexity review and refactoring opportunities |
| `cohesion_analysis` | SRP analysis via LCOM4 with guided type extraction |
| `consumer_impact` | Consumer/dependency graph analysis for refactoring impact |

### Tool Source Files

| File | Count | Categories |
|---|---|---|
| `ServerTools.cs` | 1 | Server |
| `WorkspaceTools.cs` | 8 | Workspace |
| `SymbolTools.cs` | 16 | Symbols |
| `AnalysisTools.cs` | 7 | Analysis |
| `AdvancedAnalysisTools.cs` | 7 | Advanced Analysis |
| `ConsumerAnalysisTools.cs` | 1 | Consumer Analysis |
| `CohesionAnalysisTools.cs` | 2 | Cohesion Analysis |
| `SecurityTools.cs` | 2 | Security |
| `FlowAnalysisTools.cs` | 2 | Flow Analysis |
| `SyntaxTools.cs` | 1 | Syntax |
| `OperationTools.cs` | 1 | Operations |
| `CompileCheckTools.cs` | 1 | Compilation |
| `AnalyzerInfoTools.cs` | 1 | Analyzer Info |
| `SnippetAnalysisTools.cs` | 1 | Snippet Analysis |
| `ScriptingTools.cs` | 1 | Scripting |
| `RefactoringTools.cs` | 8 | Rename, Format, Organize |
| `CodeActionTools.cs` | 5 | Code Actions & Fixes |
| `FixAllTools.cs` | 2 | Fix All |
| `InterfaceExtractionTools.cs` | 2 | Interface Extraction |
| `TypeExtractionTools.cs` | 2 | Type Extraction |
| `TypeMoveTools.cs` | 2 | Type Movement |
| `BulkRefactoringTools.cs` | 2 | Bulk Refactoring |
| `CrossProjectRefactoringTools.cs` | 3 | Cross-Project Refactoring |
| `OrchestrationTools.cs` | 4 | Orchestration |
| `DeadCodeTools.cs` | 2 | Dead Code Removal |
| `EditTools.cs` | 1 | Text Editing |
| `MultiFileEditTools.cs` | 1 | Text Editing |
| `FileOperationTools.cs` | 6 | File Operations |
| `ProjectMutationTools.cs` | 11 | Project Mutation |
| `ScaffoldingTools.cs` | 4 | Scaffolding |
| `ValidationTools.cs` | 7 | Build, Test, Coverage |
| `EditorConfigTools.cs` | 1 | EditorConfig |
| `UndoTools.cs` | 1 | Undo |
