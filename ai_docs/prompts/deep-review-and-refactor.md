# Deep Code Review & Refactor Agent Prompt

<!-- purpose: Exhaustive living prompt for MCP audits and refactor exercises against the real tool surface. -->
<!-- DO NOT DELETE THIS FILE.
     This is a living document that must be maintained and kept in sync
     with the project's actual tool, resource, and prompt surface at all times.
     When tools, resources, or prompts are added, removed, or renamed,
     update the Tools Reference appendix accordingly.
     Last surface audit: 2026-04-04 (see appendix; 123 tools, 7 resources, 16 prompts). -->

> Use this prompt with an AI coding agent that has access to the Roslyn MCP server.
> **Primary purpose:** produce an MCP server audit (bugs, incorrect results, gaps). **Mechanism:** real refactoring plus tool calls that exercise the full surface.

---

## Prompt

You are a senior .NET architect. Your **primary mission** is to **audit the Roslyn MCP server** — capture incorrect results, crashes, timeouts, missing data, inconsistencies between tools, and UX/documentation gaps. Your **secondary mechanism** is to **actually refactor the target solution** (Phases 0–6) so tool calls run against real code, previews have real diffs, and `compile_check` / `test_run` mean something.

1. **MCP server audit (primary)** — For every tool call, ask whether the result is correct, complete, and consistent with sibling tools. Record issues in the mandatory audit file (see Output Format). Also record **tool coverage** (what was exercised vs skipped) and **verified working** tools — not only failures.
2. **Refactor the codebase (supporting)** — In **Phase 6 only**, apply meaningful improvements (fixes, renames, extractions, dead code removal, format, organize). Verify with `compile_check` and `test_run`. Those changes belong in the target repo’s git history. Summarize what you applied in the audit report’s **Phase 6 refactor summary** subsection (see Output Format); that summary is part of the single MCP audit file, not a separate deliverable.

**Known issues:** Open MCP server work is tracked in [`ai_docs/backlog.md`](../backlog.md) in the **Roslyn-Backed-MCP** repository (the repo that ships this server). If you hit a known issue, cite the backlog item briefly (e.g. “matches AUDIT-23”) and put **detail only on new or divergent behavior**.

**Phase order note:** After Phase 8, run **Phase 10** before **Phase 9** (Undo). Phase 9 is placed after Phase 10 so `revert_last_apply` does **not** undo Phase 6 refactoring — see Phase 9.

Work through each phase below **in order** (including the Phase 10 → Phase 9 sequence). After each tool call, evaluate whether the result is correct and complete. If a tool returns an error, empty results when data was expected, or results that seem wrong, record it for the MCP Server Audit Report.

### Cross-cutting audit principles (apply to every phase)

These standing rules apply to **every** tool call across all phases. Do not wait for a specific phase checkpoint to observe these — capture them in real time.

1. **Inline severity signal:** After each tool call, mentally tag the result: **PASS** (correct and complete), **FLAG** (minor issue or unexpected behavior worth noting), or **FAIL** (incorrect, crash, or missing data). Accumulate FLAGs and FAILs for the report so observations are not lost between tool calls and report-writing time.
2. **Tool description accuracy:** Compare each tool's actual behavior with its MCP tool schema/description. Did the schema accurately describe required vs optional parameters? Did defaults match the description? Were there undocumented parameters that changed behavior? Was the return shape consistent with what the description promised? Schema-vs-behavior mismatches are high-value findings because the descriptions are the API documentation consumed by every AI client.
3. **Performance observation:** Note any tool call that is notably slow (>5 s for a simple single-symbol operation, >15 s for a solution-wide scan). Record the tool name, input scale, and approximate wall-clock time for the report's **Performance observations** subsection.
4. **Error message quality:** When a tool returns an error, evaluate the message itself: Is it **actionable** (tells the caller what went wrong and what to do differently)? **Vague** (generic message with no remediation hint)? Or **unhelpful** (raw exception / stack trace)? Record the rating in the report.
5. **Response contract consistency:** Across tool calls, note inconsistencies in response contracts: Are line numbers 0-based or 1-based consistently? Do related tools (`find_consumers`, `find_references`, `find_type_usages`) use the same field names for the same concepts? Are classification values always strings or sometimes numeric codes? Note inconsistencies in the report.

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

**Applies for real product changes happen here only.** Phases 10, 12, and 13 are **preview-only** (audit the tools without leaving scaffold, package, or project-mutation junk in the repo). Phase 6 is where you apply fixes, renames, extractions, formatting, and dead-code removal that genuinely improve the solution.

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

**Cross-tool chain validation (Phase 6):** After `rename_apply`, call `find_references` on the new name — does the count match the preview? After `extract_interface_apply`, call `type_hierarchy` on the new interface — does it show the implementor? After `fix_all_apply`, call `project_diagnostics` with the same diagnostic ID — did the count drop to zero? After `organize_usings_apply`, call `get_source_text` — are usings actually sorted? These chains verify that workspace state stays consistent across mutations.

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
4. Call `test_discover` to find all tests.
5. Call `test_related_files` with the list of files you modified during refactoring.
6. Call `test_related` on a symbol you refactored to find tests targeting it specifically.
7. Call `test_run` with the filter from `test_related_files` to run affected tests.
8. Call `test_run` with no filter to run the full suite.
9. Call `test_coverage` to measure coverage.

**MCP audit checkpoint:** Does `build_workspace` match `compile_check` results (same error count, same diagnostic IDs, same locations)? Does `test_related_files` correctly identify related tests? Does `test_related` find the right tests for the symbol? Does `test_run` produce structured results with pass/fail counts — and do the aggregated counts reflect the full solution or only the last assembly? Does `test_coverage` produce coverage data?

**Do not** call `revert_last_apply` yet — that would undo the most recent Phase 6 apply. Continue to **Phase 10** (preview-only), then **Phase 9** (Undo verification).

---

### Phase 10: Cross-Project Operations (preview only — if multi-project solution)

**PREVIEW ONLY — do not apply.** Exercise preview and composite tools; inspect diffs and JSON. Applying moves, deletes, scaffolds, or package migrations here leaves audit artifacts in the repo and is **out of scope** for this prompt.

1. Call `move_type_to_file_preview` to move a type into its own file.
2. Call `move_file_preview` to move a file to a different directory with namespace update.
3. Call `create_file_preview` to preview creating a new source file in a project.
4. Call `delete_file_preview` to preview deleting an unused source file (pick a path that would be safe if you were applying — do **not** apply).
5. Call `extract_interface_cross_project_preview` to extract an interface into a different project.
6. Call `dependency_inversion_preview` to extract interface + update constructor dependencies.
7. Call `move_type_to_project_preview` to move a type declaration into another project.
8. Call `extract_and_wire_interface_preview` to extract an interface and rewrite DI registrations.
9. Call `split_class_preview` to split a type into partial class files.
10. Call `migrate_package_preview` to replace one NuGet package with another across projects.
11. Optionally call `apply_composite_preview` **only as a preview path** if the server requires it to materialize a composite diff — if that would write to disk, skip and note in the audit.

**MCP audit checkpoint:** Do cross-project previews produce valid diffs? Do namespace and reference updates look correct in the preview? Does `extract_and_wire_interface_preview` correctly identify DI registrations? Does `split_class_preview` produce valid partial classes? Do file creation and deletion previews produce correct diffs?

---

### Phase 9: Undo Verification (run after Phase 10)

**Why after Phase 10:** Calling `revert_last_apply` immediately after Phase 8 would undo the **last Phase 6 apply** (e.g. dead-code removal), not “all of Phase 6.” To test undo **without** losing Phase 6 work, add one **audit-only** apply on top, then revert it.

1. Perform **exactly one** low-impact Roslyn apply whose reversal is safe — for example `format_document_preview` then `format_document_apply` on a single file (prefer one already touched in Phase 6), or another minimal apply that only affects one file. This apply must become the **new** top of the undo stack.
2. Call `revert_last_apply`. Confirm it undoes **only** that audit-only apply; Phase 6 changes must remain.
3. Call `compile_check` to verify the workspace is still consistent.
4. Note: only Roslyn solution-level changes can be reverted; document if `revert_last_apply` errors or no-ops.

**MCP audit checkpoint:** Does `revert_last_apply` restore the prior state for that one apply? Does it report what was undone? Does it behave correctly when there is nothing to revert (if tested)?

---

### Phase 11: Semantic Search & Discovery

1. Call `semantic_search` with a natural-language query like `"async methods returning Task<bool>"`. Verify results match the query semantics.
2. Call `semantic_search` with `"classes implementing IDisposable"`. Cross-check against `find_implementations`.
3. Call `find_reflection_usages` to locate reflection-heavy code that may resist refactoring.
4. Call `get_di_registrations` to audit the DI container wiring.
5. Call `source_generated_documents` to list any source-generator output files.

**MCP audit checkpoint:** Does `semantic_search` return relevant results? Does `find_reflection_usages` find all reflection patterns (typeof, GetMethod, Activator, etc.)? Does `get_di_registrations` parse all registration styles?

---

### Phase 12: Scaffolding (preview only)

**PREVIEW ONLY — do not apply.** Do not call `scaffold_type_apply` or `scaffold_test_apply`; audit the previews only.

1. Call `scaffold_type_preview` to scaffold a new class in a project. Verify the generated file content and namespace in the preview payload.
2. Call `scaffold_test_preview` to scaffold a test file for an existing type. Verify it targets the correct type and uses the right test framework in the preview.
3. Call `compile_check` if the server requires a fresh compilation for previews; otherwise skip (previews alone do not change disk).

**MCP audit checkpoint:** Does `scaffold_type_preview` infer the correct namespace from the project structure? Does `scaffold_test_preview` generate valid test stubs? Are preview payloads complete and apply-able in principle?

---

### Phase 13: Project Mutation (preview only)

**PREVIEW ONLY — do not apply.** Do not call `apply_project_mutation` (or any path that writes `.csproj` / `Directory.Packages.props`). Inspect preview XML/diffs only.

1. Call `add_package_reference_preview` to add a NuGet package to a project.
2. Call `remove_package_reference_preview` to remove a NuGet package.
3. Call `add_project_reference_preview` to add a project-to-project reference.
4. Call `remove_project_reference_preview` to remove a project reference.
5. Call `set_project_property_preview` to set a project property (e.g., Nullable, LangVersion).
6. Call `set_conditional_property_preview` to set a conditional project property scoped to a configuration.
7. Call `add_target_framework_preview` to add a target framework to a multi-targeting project.
8. Call `remove_target_framework_preview` to remove a target framework.
9. If the solution uses Central Package Management, call `add_central_package_version_preview` and `remove_central_package_version_preview` (preview only).

**MCP audit checkpoint:** Do project mutation previews produce correct XML diffs? Do `set_conditional_property_preview` conditions evaluate correctly in the preview? Do framework additions/removals look correct in the preview? (Do not verify via apply.)

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
2. Read `roslyn://server/resource-templates` to list all resource URI templates and compare with the catalog.
3. Read `roslyn://workspaces` to list active workspace sessions.
4. Read `roslyn://workspace/{workspaceId}/status` and compare with `workspace_status` tool output.
5. Read `roslyn://workspace/{workspaceId}/projects` and compare with `project_graph` tool output.
6. Read `roslyn://workspace/{workspaceId}/diagnostics` and compare with `project_diagnostics` tool output.
7. Read `roslyn://workspace/{workspaceId}/file/{filePath}` for a source file and compare with `get_source_text`.

**MCP audit checkpoint:** Do resources return data consistent with their tool counterparts? Are URI templates resolved correctly? Is the server catalog accurate and up to date? Do resource DTOs use the same field names as their tool counterparts (e.g., `Name` vs `ProjectName`)? Does the catalog tool/resource/prompt count match `server_info`?

---

### Phase 16: Prompt Verification (pick 4+ prompts)

The server exposes 16 prompts. Exercise at least 4 representative ones to verify their argument templates, output quality, and accuracy.

1. Call `explain_error` with a diagnostic from Phase 1. Verify the explanation is accurate and actionable.
2. Call `suggest_refactoring` on a symbol or region you analyzed in Phase 3. Verify the suggestions reference real tools and produce sensible guidance.
3. Call `review_file` on a file modified in Phase 6. Verify the review references actual code and produces useful observations.
4. Call `discover_capabilities` with a task category (e.g. "refactoring", "testing", "security"). Verify it returns relevant tools for the category.
5. Optionally call additional prompts (`dead_code_audit`, `review_complexity`, `cohesion_analysis`, `consumer_impact`, `security_review`, `debug_test_failure`, etc.) for broader coverage.

**MCP audit checkpoint:** Do prompt argument templates make sense? Does the prompt output reference correct tools and produce actionable guidance? Are prompt descriptions accurate? Do any prompts fail, return empty output, or produce hallucinated tool names?

---

### Phase 17: Boundary & Negative Testing

Deliberately probe edge cases and error paths. The goal is to verify that the server validates inputs, returns helpful error messages, and does not crash on bad data.

#### 17a. Invalid Identifiers
1. Call a workspace-scoped tool (e.g. `workspace_status`) with a **non-existent workspace id**. Verify the error is actionable.
2. Call `find_references` (or `symbol_info`) with a **fabricated symbol handle**. Verify it returns a clear error, not a crash or empty success.
3. Call `rename_preview` with the same fabricated handle. Verify error handling.

#### 17b. Out-of-Range Positions
1. Call `go_to_definition` with a **line number beyond the end of the file**. Verify the error.
2. Call `enclosing_symbol` at **line 0, column 0** (potential off-by-one). Verify behavior.
3. Call `analyze_data_flow` with **startLine > endLine**. Verify it rejects or handles gracefully.

#### 17c. Empty and Degenerate Inputs
1. Call `symbol_search` with an **empty string** query. Verify it returns an empty result or a clear error, not a crash.
2. Call `analyze_snippet` with an **empty string** code body. Verify behavior.
3. Call `evaluate_csharp` with an **empty string**. Verify behavior.

#### 17d. Stale and Double-Apply
1. Call any `*_apply` tool with a **preview token from a previous phase** that has already been applied. Verify it rejects the stale token.
2. Call `revert_last_apply` **twice** in succession. Verify the second call returns a clear "nothing to revert" response, not an error.

#### 17e. Post-Close Operations
1. Call `workspace_close` to close the session.
2. Call `workspace_status` with the **now-closed workspace id**. Verify the error is clear.
3. Call `workspace_load` to re-open the workspace for subsequent phases (or confirm session is still active for the remaining phases if you defer this sub-phase to the end).

**MCP audit checkpoint:** For every error path tested: Did the server return an actionable error message or did it crash / return a vague MCP error? Rate each error message as **actionable**, **vague**, or **unhelpful**. Were there any 500-level or unhandled exceptions? Did any bad input cause the server to enter a degraded state affecting subsequent calls?

---

### Phase 18: Regression Verification (re-test backlog items)

Before writing the report, deliberately re-test 3–5 items from [`ai_docs/backlog.md`](../backlog.md), prioritizing P1 and P2 items.

1. Read `ai_docs/backlog.md` and select 3–5 items that can be reproduced with the current workspace.
2. For each selected item, reproduce the exact scenario described (same tool, similar inputs). Record the result:
   - **Still reproduces** — confirm with current server version.
   - **Partially fixed** — describe what changed and what remains.
   - **No longer reproduces** — mark as **candidate for closure** in the report.
3. Include findings in the report's **Backlog regression check** section.

**MCP audit checkpoint:** Did any previously reported issues get fixed without being removed from the backlog? Did any issues change in character (e.g., different error message, partial fix)?

---

When finished with all phases, call `workspace_close` to release the session (if not already closed during Phase 17e).

---

## Output Format — MANDATORY

### What goes in git (no audit file)

Meaningful refactoring from **Phase 6** is committed in the **target solution’s** repository. A short **Phase 6 refactor summary** in the MCP audit report (see Report contents) ties git history to tools exercised; the code changes themselves are not a separate file deliverable.

### What goes in a file (one mandatory output)

You **MUST** write exactly one file: the **MCP Server Audit Report**. The task is **not finished** until this file exists on disk. Do not only paste it in chat.

### Where to save the report

Save the file in the **Roslyn-Backed-MCP** repository (the repo that contains this prompt and ships the MCP server), **not** necessarily the target solution’s repo:

**Canonical path:** `<Roslyn-Backed-MCP-root>/ai_docs/refactor/<repo-id>_mcp-server-audit.md`

- If your workspace **is** Roslyn-Backed-MCP, use `ai_docs/refactor/` relative to that repo root.
- If you are auditing another solution in a different workspace and **cannot** write to Roslyn-Backed-MCP, write the file to the **current workspace root** as `ai_docs/refactor/<repo-id>_mcp-server-audit.md` (create `ai_docs/refactor/` if needed) **and** state in the report header: *“Intended final path: `<path-to-Roslyn-Backed-MCP>/ai_docs/refactor/…` — copy this file there for backlog review.”*

### Naming scheme (`<repo-id>`)

Derive `<repo-id>` from the **audited** solution or repository name:
- Strip the `.sln` / `.slnx` / `.csproj` extension
- Lowercase; replace spaces and dots with hyphens
- Examples: `ITChatBot.sln` → `itchatbot`, `FirewallAnalyzer.slnx` → `firewallanalyzer`, `Roslyn-Backed-MCP.sln` → `roslyn-backed-mcp`, `NetworkDocumentation.sln` → `networkdocumentation`

### Report contents (required sections)

1. **Header (metadata)** — server version (from `server_info`), Roslyn / .NET versions if available, **audited** solution path and name, date, workspace id, approximate project count and document count from `workspace_load` / `project_graph`.
2. **Tool coverage** — For each **category** below, state **exercised** / **skipped** (with reason) / **N/A** (e.g. single-project solution and cross-project previews not applicable). Categories: Server; Workspace; Symbols; Analysis; Advanced Analysis; Consumer & Cohesion; Security; Flow; Syntax & Operations; Compilation; Analyzer Info; Snippet & Scripting; Refactoring (rename/format/organize); Code Actions & Fixes; Fix All; Interface/Type extraction; Type movement; Bulk refactor; Cross-project refactor; Orchestration; Dead code; Text edits; File ops; Project mutation (previews); Scaffolding (previews); Build & Test; EditorConfig; Undo; Resources (MCP resources); Prompts; Boundary & Negative Testing; Regression Verification.
3. **Verified tools (working)** — Bullet list: tool name + one-line evidence (e.g. “`compile_check` — 0 errors after Phase 6”). This distinguishes “tested and fine” from “never called.”
4. **Phase 6 refactor summary** — Concise record of **applied** Phase 6 work (not preview-only phases). Include: target repo identity (if different from Roslyn-Backed-MCP); **scope** (e.g. fix-all + rename + format — which sub-steps 6a–6i you actually applied); **notable symbols or files** touched; **MCP tools used** for each change (e.g. `rename_apply`, `fix_all_apply`); **verification** (`compile_check` / `test_run` / `build_workspace` outcomes). If Phase 6 had **no** applies (skipped or audit-only), state **N/A** with one line why. Optional: git commit hash or PR link if available.
5. **MCP server issues (bugs)** — For each issue: **Tool name**, **inputs**, **expected** vs **actual**, **severity** (crash / incorrect result / missing data / degraded performance / cosmetic / error-message-quality), **reproducibility** (always / sometimes / once). Watch for: crashes, wrong or incomplete results, tool-vs-tool inconsistency, missing functionality, slow tools, bad JSON/truncation, bad line/position mapping, unhelpful error messages.
6. **Improvement suggestions** — Things that work correctly but could work better. UX friction (confusing parameter names, unexpected defaults, verbose output). Missing convenience features (e.g., “I wished tool X also returned Y”). Workflow gaps (sequences of 3+ tool calls that could be a single composite tool). Output enrichment (e.g., numeric codes where string labels would be more useful). Schema/description mismatches (tool description says one thing, actual behavior does another). These are not bugs — they are actionable input for the next release.
7. **Performance observations** — List any tools that were notably slow, with tool name, input scale, and approximate wall-clock time. If all tools responded within expected bounds, state that.
8. **Backlog regression check** — For the 3–5 backlog items re-tested in Phase 18, state: **still reproduces**, **partially fixed** (describe), or **candidate for closure** (no longer reproduces). Include the AUDIT-ID and one-line summary for each.
9. **Known backlog cross-check** — List any *newly observed* issues that match [`ai_docs/backlog.md`](../backlog.md) (e.g. “AUDIT-23 — semantic_search Task<bool>”) with one line each; put **full write-ups only for new or different** behavior.

If there are **zero** new issues, still write sections 1–4 and 6–9; in section 5 state **“No new issues found”** and confirm the audit ran.

### Markdown template (copy, fill in, save)

```markdown
# MCP Server Audit Report

## Header
- **Date:**
- **Audited solution:**
- **Workspace id:**
- **Server:** (from `server_info`)
- **Roslyn / .NET:** (if reported)
- **Scale:** ~N projects, ~M documents
- **Report path note:** (if not saved under Roslyn-Backed-MCP, state intended copy destination)

## Tool coverage
| Category | Status | Notes |
|----------|--------|--------|
| Server | exercised / skipped / N/A | |
| Workspace | | |
| … | | |

## Verified tools (working)
- `tool_name` — one-line observation
- …

## Phase 6 refactor summary
- **Target repo:** (name / path; same as audited solution if in-repo)
- **Scope:** (which of 6a–6i applied; or **N/A** — no applies, with reason)
- **Changes:** bullets — symbol/file, tool used (`rename_apply`, etc.)
- **Verification:** `compile_check` / `test_run` / `build_workspace` — outcome
- **Optional:** commit / PR reference

## MCP server issues (bugs)
### 1. (title or tool name)
| Field | Detail |
|--------|--------|
| Tool | |
| Input | |
| Expected | |
| Actual | |
| Severity | |
| Reproducibility | |

## Improvement suggestions
- `tool_name` — suggestion (UX friction / missing feature / workflow gap / output enrichment / schema mismatch)
- …

## Performance observations
- `tool_name` — input scale, ~Ns wall-clock (or "All tools responded within expected bounds")

## Backlog regression check
| AUDIT-ID | Summary | Status |
|----------|---------|--------|
| AUDIT-XX | one-line summary | still reproduces / partially fixed / candidate for closure |

## Known backlog cross-check
- …
```

### Completion gate

The file must exist at the canonical path above (or the documented fallback). Create `ai_docs/refactor/` if missing. The task is **incomplete** without this file.

---

## Tools Reference — Complete Surface Inventory

> **Maintenance note:** This appendix must be kept in sync with the actual server surface.
> When tools, resources, or prompts change, update this section.
> Last verified: 2026-04-04 — **123 tools | 7 resources | 16 prompts**

### Tools by Category (123 total)

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

#### Security (3 tools)
`security_diagnostics` `security_analyzer_status` `nuget_vulnerability_scan`

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

#### EditorConfig (2 tools)
`get_editorconfig_options` `set_editorconfig_option`

#### MSBuild evaluation (3 tools)
`evaluate_msbuild_property` `evaluate_msbuild_items` `get_msbuild_properties`

#### Suppression and severity (2 tools)
`set_diagnostic_severity` `add_pragma_suppression`

#### Undo (1 tool)
`revert_last_apply`

### Resources (7 total)

| URI Template | Description | MIME Type |
|---|---|---|
| `roslyn://server/catalog` | Machine-readable surface inventory and support policy | `application/json` |
| `roslyn://server/resource-templates` | Lists all resource URI templates, including workspace-scoped templates | `application/json` |
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
| `SecurityTools.cs` | 3 | Security |
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
| `EditorConfigTools.cs` | 2 | EditorConfig |
| `MSBuildTools.cs` | 3 | MSBuild evaluation |
| `SuppressionTools.cs` | 2 | Suppression and severity |
| `UndoTools.cs` | 1 | Undo |
