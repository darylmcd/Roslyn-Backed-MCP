# Deep Code Review & Refactor Agent Prompt

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
2. Call `workspace_status` to confirm the workspace loaded cleanly. Note any load errors.
3. Call `server_info` to record the server version, Roslyn version, and tool counts.
4. Call `project_graph` to understand the project dependency structure.

**MCP audit checkpoint:** Did `workspace_load` succeed? Did `workspace_status` report any stale documents or missing projects? Does `server_info` report the expected tool counts?

---

### Phase 1: Broad Diagnostics Scan

1. Call `project_diagnostics` (no filters) to get all compiler errors and warnings across the solution.
2. Call `compile_check` (no filters) to get in-memory compilation diagnostics. Compare the results with `project_diagnostics` — they should be consistent but may differ in count or detail.
3. Call `security_diagnostics` to identify security-relevant findings with OWASP categorization.
4. Call `security_analyzer_status` to check which analyzer packages are installed and what's missing.
5. Call `list_analyzers` to enumerate all loaded analyzers and their rules. Note total rule count.

**MCP audit checkpoint:** Do `project_diagnostics` and `compile_check` agree on error counts? Does `compile_check` with `emitValidation=true` find additional issues? Does `list_analyzers` return data for all projects? Are there any analyzers that fail to load (look for LOAD_ERROR entries)?

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

1. Call `symbol_info` to understand the type.
2. Call `document_symbols` on its file to see its member structure.
3. Call `type_hierarchy` to understand inheritance.
4. Call `find_references` to see how widely it's used.
5. Call `find_consumers` to classify dependency kinds (constructor, field, parameter, etc.).
6. Call `find_shared_members` to identify private members shared across public methods.
7. Call `find_type_mutations` to find all mutating members and their external callers.
8. Call `find_type_usages` to see how the type is used (return types, parameters, fields, casts, etc.).
9. Call `callers_callees` on 2-3 of its methods to map call chains.
10. Call `find_property_writes` on any settable properties to classify init vs post-construction writes.
11. Call `member_hierarchy` on overridden/implemented members.
12. Call `symbol_relationships` for a combined view.

**MCP audit checkpoint:** Are `find_references` and `find_consumers` consistent? Does `find_type_usages` categorize correctly? Do `callers_callees` results match what you'd expect from reading the code? Does `symbol_relationships` combine data correctly from its sub-tools?

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
3. Call `organize_usings_preview` on files that had code fix changes.
4. Call `organize_usings_apply`.

#### 6f. Code Actions
1. Call `get_code_actions` at a position with available refactorings.
2. Call `preview_code_action` on an interesting action.
3. Call `apply_code_action`.

#### 6g. Dead Code Removal
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

1. Call `build_workspace` to do a full MSBuild build after all refactoring.
2. Call `build_project` on individual projects that were modified.
3. Call `test_discover` to find all tests.
4. Call `test_related_files` with the list of files you modified during refactoring.
5. Call `test_run` with the filter from `test_related_files` to run affected tests.
6. Call `test_run` with no filter to run the full suite.
7. Call `test_coverage` to measure coverage.

**MCP audit checkpoint:** Does `build_workspace` match `compile_check` results? Does `test_related_files` correctly identify related tests? Does `test_run` produce structured results with pass/fail counts? Does `test_coverage` produce coverage data?

---

### Phase 9: Undo Verification

1. Call `revert_last_apply` to undo the most recent refactoring.
2. Call `compile_check` to verify the revert was clean.
3. Note: only Roslyn solution-level changes can be reverted.

**MCP audit checkpoint:** Does `revert_last_apply` actually restore the previous state? Does it handle the case where there's nothing to revert? Does it report what was undone?

---

### Phase 10: Cross-Project Operations (if multi-project solution)

1. Call `move_type_to_file_preview` to move a type into its own file.
2. Call `move_file_preview` to move a file to a different directory with namespace update.
3. Call `extract_interface_cross_project_preview` to extract an interface into a different project.
4. Call `dependency_inversion_preview` to extract interface + update constructor dependencies.

**MCP audit checkpoint:** Do cross-project operations correctly add project references? Do namespace updates apply? Are all references updated?

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

## Tools Reference (all available)

**Workspace:** workspace_load, workspace_close, workspace_list, workspace_reload, workspace_status, project_graph, source_generated_documents, get_source_text
**Symbols:** symbol_search, symbol_info, go_to_definition, goto_type_definition, find_references, find_references_bulk, find_implementations, find_overrides, find_base_members, member_hierarchy, document_symbols, enclosing_symbol, symbol_signature_help, symbol_relationships, get_completions, find_property_writes
**Analysis:** project_diagnostics, diagnostic_details, type_hierarchy, callers_callees, impact_analysis, find_type_mutations, find_type_usages, find_consumers, get_cohesion_metrics, find_shared_members, compile_check, analyze_data_flow, analyze_control_flow, get_operations, list_analyzers
**Advanced Analysis:** find_unused_symbols, get_di_registrations, get_complexity_metrics, find_reflection_usages, get_namespace_dependencies, get_nuget_dependencies, semantic_search
**Security:** security_diagnostics, security_analyzer_status
**Refactoring:** rename_preview/apply, organize_usings_preview/apply, format_document_preview/apply, format_range_preview/apply, code_fix_preview/apply, fix_all_preview/apply, get_code_actions, preview_code_action, apply_code_action
**Type Operations:** extract_interface_preview/apply, extract_interface_cross_project_preview, extract_and_wire_interface_preview, dependency_inversion_preview, extract_type_preview/apply, split_class_preview, move_type_to_file_preview/apply, move_type_to_project_preview, bulk_replace_type_preview/apply
**File & Edit:** apply_text_edit, apply_multi_file_edit, create_file_preview/apply, delete_file_preview/apply, move_file_preview/apply
**Project Mutation:** add/remove_package_reference_preview, add/remove_project_reference_preview, add/remove_target_framework_preview, set_project_property_preview, set_conditional_property_preview, add/remove_central_package_version_preview, migrate_package_preview, apply_project_mutation
**Validation:** build_workspace, build_project, test_discover, test_run, test_related, test_related_files, test_coverage
**Scaffolding:** scaffold_type_preview/apply, scaffold_test_preview/apply
**Dead Code:** remove_dead_code_preview/apply
**Configuration:** get_editorconfig_options
**Scripting:** evaluate_csharp, analyze_snippet
**Syntax:** get_syntax_tree
**Orchestration:** apply_composite_preview
**Undo:** revert_last_apply
**Server:** server_info
