# MCP Server Audit Report

**Date:** 2026-03-28 – 2026-03-29
**Server:** roslyn-mcp v1.1.1+5bbf20dc
**Roslyn Version:** 5.3.0.0
**Runtime:** .NET 10.0.5
**Tool Surface:** 49 stable + 67 experimental = 116 tools
**Solutions tested:**
- Roslyn-Backed-MCP.sln (6 projects, 273 docs)
- NetworkDocumentation.sln (8 projects, 339 docs)
- ITChatBot.sln (23 projects, 366 docs)

---

## Summary

| Category | Count |
|----------|-------|
| Crashes / unhandled exceptions | 9 |
| Incorrect / incomplete results | 11 |
| Missing functionality / data | 3 |
| Performance / output overflow | 4 |
| Cosmetic / inconsistency | 2 |
| **Total unique issues** | **29** |

**Broadly functional:** `compile_check`, `build_workspace`, `rename_preview/apply`, `organize_usings_preview/apply`, `remove_dead_code_preview/apply`, `format_range_preview`, `security_diagnostics`, `get_complexity_metrics` (with filter), `get_namespace_dependencies`, `get_nuget_dependencies`, `semantic_search`, `evaluate_csharp`, `analyze_snippet`.

**Significantly broken:** `fix_all_preview` (crashes on all solutions), `callers_callees` (wrong symbol resolution), `IsTestProject` detection (fails on 2 of 3 solutions), workspace session stability (silent drops).

---

## Crashes / Unhandled Exceptions

### Issue 1: `fix_all_preview` — Crashes at All Scopes on All Solutions

- **Tool:** `fix_all_preview`
- **Input:** Tested with CS8019, IDE0005 at solution/project/document scopes across all 3 solutions (10+ attempts)
- **Expected:** Preview of fix-all changes
- **Actual:** `"An error occurred invoking 'fix_all_preview'."` — no further details
- **Severity:** Crash
- **Reproducibility:** Always (10/10 attempts across 3 solutions)
- **Impact:** Cannot batch-fix diagnostics — a core refactoring workflow. Workaround: `organize_usings_preview` per-file.

### Issue 2: `get_source_text` — Crashes (Solution-Dependent)

- **Tool:** `get_source_text`
- **Input:** Various source files across solutions
- **Actual:**
  - **Roslyn-Backed-MCP.sln:** Crashes on all 3 files tested — completely non-functional
  - **ITChatBot.sln:** Works correctly
- **Severity:** Crash (solution-dependent)
- **Reproducibility:** Always on Roslyn-Backed-MCP.sln; works on ITChatBot.sln
- **Workaround:** Use file system Read tool directly.

### Issue 3: `analyze_data_flow` / `analyze_control_flow` — Crash on Large Method Bodies

- **Tool:** `analyze_data_flow` and `analyze_control_flow`
- **Input:** Full method bodies with try-catch statements or multiple catch clauses spanning to end of class
- **Actual:** Generic crash error. Works when narrowed to inner blocks.
- **Severity:** Crash
- **Reproducibility:** Always (confirmed across Roslyn-Backed-MCP.sln and ITChatBot.sln)
- **Workaround:** Use smaller line ranges that don't extend to the class closing brace.

### Issue 4: `extract_interface_preview` — Crashes (Solution-Dependent)

- **Tool:** `extract_interface_preview`
- **Input:** Various types across solutions
- **Actual:**
  - **NetworkDocumentation.sln:** Crashes with no details
  - **Roslyn-Backed-MCP.sln:** Works (with formatting issues — see Issue 26)
- **Severity:** Crash (solution-dependent)
- **Impact:** Cascades to `extract_and_wire_interface_preview`, `dependency_inversion_preview`, `bulk_replace_type_preview`.

### Issue 5: `extract_interface_cross_project_preview` — Crashes or Wrong Namespace

- **Tool:** `extract_interface_cross_project_preview`
- **Actual:**
  - **NetworkDocumentation.sln:** Crashes with no details
  - **Roslyn-Backed-MCP.sln:** Succeeds but uses source namespace instead of target project namespace. Also includes `Dispose()` and unnecessary usings.
- **Severity:** Crash / Incorrect result (solution-dependent)
- **Reproducibility:** Always

### Issue 6: `get_cohesion_metrics` — Crashes (Solution-Dependent)

- **Tool:** `get_cohesion_metrics`
- **Input:** `minMethods=3`
- **Actual:**
  - **NetworkDocumentation.sln:** Crashes with no details
  - **Roslyn-Backed-MCP.sln / ITChatBot.sln:** Works (with caveats — see Issue 16)
- **Severity:** Crash (solution-dependent)

### Issue 7: `find_reflection_usages` / `get_di_registrations` — Crash (Solution-Dependent)

- **Tool:** `find_reflection_usages`, `get_di_registrations`
- **Actual:**
  - **ITChatBot.sln:** Both crash with generic error
  - **Roslyn-Backed-MCP.sln / NetworkDocumentation.sln:** Work correctly
- **Severity:** Crash (solution-dependent)
- **Reproducibility:** Always on ITChatBot.sln (tested on 2 separate workspace loads)

### Issue 8: `test_discover` — Crashes or Returns Empty (Solution-Dependent)

- **Tool:** `test_discover`
- **Actual:**
  - **ITChatBot.sln:** Crashes with generic error
  - **NetworkDocumentation.sln:** Returns `TestProjects: []` (due to IsTestProject=false — Issue 13)
  - **Roslyn-Backed-MCP.sln:** Works correctly (found 143 tests)
- **Severity:** Crash / Missing data (solution-dependent)

### Issue 9: Workspace Session Dropped Silently

- **Tool:** All workspace-dependent tools
- **Input:** Using workspace ID after ~30 minutes of inactivity
- **Expected:** Workspace persists or clear "workspace expired" error
- **Actual:** `workspace_status` returns generic error. `workspace_list` returns `{ count: 0 }`. No indication of why.
- **Severity:** Crash / degraded reliability
- **Reproducibility:** Sometimes (appears related to session duration or MCP connection drops)
- **Impact:** All prior workspace state lost. Client must detect failure and reload.

---

## Incorrect / Incomplete Results

### Issue 10: `callers_callees` — Resolves to Return Type Instead of Method Name

- **Tool:** `callers_callees`
- **Input:** Positions on method declarations — async methods (5 tested) and non-async methods with column on return type (2 tested)
- **Expected:** Callers and callees of the specific method
- **Actual:** Resolves to the return type token (`Task<T>`, `JsonObject`, `Device`) instead of the method. Returns callers of those types.
- **Severity:** Incorrect result — **critical bug**
- **Reproducibility:** Always for async methods (5/5); always when column lands on return type token (2/2). Works when column is precisely on the method name identifier.
- **Impact:** Misleading data + 239K+ character output overflow per call.

### Issue 11: `find_unused_symbols` — Multiple False Positive Sources

- **Tool:** `find_unused_symbols`
- **False positive sources identified across 3 solutions:**
  - **Framework-invoked methods:** MCP `[McpServerTool]` methods (50 false positives, Roslyn-Backed-MCP.sln)
  - **Source-generated code:** Methods from `obj/Debug/.../RegexGenerator.g.cs` (NetworkDocumentation.sln)
  - **NuGet package content duplicates:** `XunitAutoGeneratedEntryPoint` appears **11 times** (once per test project, identical file path) on ITChatBot.sln
- **Severity:** Incorrect result
- **Reproducibility:** Always
- **Recommendations:** (a) Exclude `obj/` paths by default; (b) Deduplicate by file path; (c) Exclude NuGet `_content/` paths; (d) Consider attribute-aware analysis.

### Issue 12: `find_type_mutations` — Dispose() Over-Matching

- **Tool:** `find_type_mutations`
- **Input:** `WorkspaceManager` type
- **Expected:** Callers of `WorkspaceManager.Dispose()` specifically
- **Actual:** Returns 59 unrelated `IDisposable.Dispose()` calls across the entire solution
- **Severity:** Incorrect result — noise
- **Reproducibility:** Always (for types implementing IDisposable)

### Issue 13: `workspace_load` — `IsTestProject` Not Detected

- **Tool:** `workspace_load` / `workspace_status`
- **Input:** Test projects referencing xUnit, MSTest, or Microsoft.NET.Test.Sdk
- **Actual:**
  - **ITChatBot.sln:** All 11 xUnit test projects report `IsTestProject: false`
  - **NetworkDocumentation.sln:** Test project reports `IsTestProject: false`
  - **Roslyn-Backed-MCP.sln:** MSTest project correctly reports `IsTestProject: true`
- **Severity:** Incorrect result (cascading — breaks `test_discover`, `test_related_files`)
- **Reproducibility:** Always for xUnit projects; MSTest detection works
- **Notes:** Likely only checks for MSTest markers, not xUnit/NUnit package references.

### Issue 14: `workspace_load` — TargetFrameworks Shows "unknown"

- **Tool:** `workspace_load` / `workspace_status` / `project_graph`
- **Input:** All 3 solutions (all projects target `net10.0`)
- **Expected:** `TargetFrameworks: ["net10.0"]`
- **Actual:** `"unknown"` for most projects across all solutions. Only SampleApp/SampleLib (with explicit `<TargetFramework>net10.0</TargetFramework>`) show correctly.
- **Severity:** Missing data
- **Reproducibility:** Always

### Issue 15: `move_type_to_file_preview` — Multiple Issues

- **Tool:** `move_type_to_file_preview`
- **Issues identified:**
  - **Invalid access modifier:** Moving a nested `private sealed class` keeps `private` as top-level — invalid C# (Roslyn-Backed-MCP.sln)
  - **Unnecessary usings copied:** Generated file includes usings not needed by the moved type (ITChatBot.sln)
- **Severity:** Incorrect result — first sub-issue would produce compilation error
- **Reproducibility:** Always (for nested private types; always for unnecessary usings)

### Issue 16: `find_shared_members` — Returns Empty for Readonly Fields and Static Classes

- **Tool:** `find_shared_members`
- **Input:** 4 types: 3 service types with readonly fields, 1 static class with shared field
- **Expected:** Lists private fields shared across public methods
- **Actual:** `count: 0` for all 4 types
- **Severity:** Missing data — likely false negative
- **Reproducibility:** Always (across 2 solutions)
- **Notes:** May only consider non-readonly field writes. Useless for typical immutable DI service pattern.

### Issue 17: `test_run` — Skipped Count Not Reported

- **Tool:** `test_run`
- **Input:** Full test suite on NetworkDocumentation.sln
- **Expected:** `Skipped: 24`
- **Actual:** `Skipped: 0` (but stdout shows 24 `[SKIP]` entries; `Total: 8052` is correct as 8028+24)
- **Severity:** Incorrect result
- **Reproducibility:** Always

### Issue 18: `test_related_files` — Returns Empty (Cascading from Issue 13)

- **Tool:** `test_related_files`
- **Input:** Modified source file paths from NetworkDocumentation.sln
- **Expected:** Related tests
- **Actual:** `Tests: [], DotnetTestFilter: ""`
- **Severity:** Missing data (cascading from IsTestProject=false)
- **Reproducibility:** Always on NetworkDocumentation.sln

### Issue 19: `analyze_snippet` — `usings` Parameter Doesn't Add Assembly References

- **Tool:** `analyze_snippet`
- **Input:** `code: "using System.Text.Json; var json = JsonSerializer.Serialize(...);"`, `usings: ["System.Text.Json"]`
- **Expected:** Snippet compiles with System.Text.Json available
- **Actual:** `CS0234: The type or namespace name 'Json' does not exist in the namespace 'System.Text'`
- **Severity:** Incorrect result
- **Reproducibility:** Always
- **Notes:** The `usings` parameter adds using directives but doesn't add the required assembly reference.

### Issue 20: `revert_last_apply` — Generic Error When Nothing to Revert

- **Tool:** `revert_last_apply`
- **Input:** `workspaceId` with no prior apply operations
- **Expected:** Clear "nothing to revert" response
- **Actual:** `"An error occurred invoking 'revert_last_apply'."` — generic error
- **Severity:** Incorrect result / poor error handling
- **Reproducibility:** Always
- **Notes:** On Roslyn-Backed-MCP.sln, `revert_last_apply` works correctly after an actual apply.

---

## Missing Functionality / Data

### Issue 21: `get_code_actions` — Returns Empty at Most Positions

- **Tool:** `get_code_actions`
- **Input:** Tested at 4+ positions across all 3 solutions, including positions with active diagnostics (CS0414, CA5351)
- **Expected:** Available code actions / refactorings
- **Actual:** `count: 0, actions: []` in most cases
- **Severity:** Missing data
- **Reproducibility:** Frequent (4/5 attempts returned empty)
- **Notes:** CS0414 has no built-in fix (correct). CA5351 has a security diagnostic but no code action offered. May be position-sensitive or require code fix providers not loaded in MSBuildWorkspace.

### Issue 22: `get_syntax_tree` — Line Range Filter Does Not Scope Root

- **Tool:** `get_syntax_tree`
- **Input:** `startLine=438, endLine=488` targeting a single method
- **Expected:** Only the method at the specified lines
- **Actual:** Returns entire `ClassDeclaration` with all sibling methods
- **Severity:** Missing functionality
- **Reproducibility:** Always

### Issue 23: `get_complexity_metrics` — Unsorted Without `minComplexity`

- **Tool:** `get_complexity_metrics`
- **Input:** `{ workspaceId, limit: 30 }` (no `minComplexity` filter)
- **Expected:** Sorted by cyclomatic complexity descending (most complex first)
- **Actual:** Returns first 30 methods in alphabetical file-path order, starting with trivial CC=1 getters
- **Severity:** Missing functionality / degraded usability
- **Reproducibility:** Always
- **Workaround:** Always pass `minComplexity` (e.g., `minComplexity: 5`) which triggers proper sort.

---

## Performance / Output Overflow

### Issue 24: `project_diagnostics` — Unbounded Output for Hidden Diagnostics

- **Tool:** `project_diagnostics`
- **Input:** No filters
- **Actual:** 194K chars (385 items, Roslyn-Backed-MCP.sln) → 1.6M chars (ITChatBot.sln) → 2.8M chars (5,774 items, NetworkDocumentation.sln)
- **Severity:** Performance / degraded experience
- **Recommendation:** Default to Warning+ severity or add pagination.

### Issue 25: Multiple Symbol Tools Lack Limit/Pagination

- **Tools:** `find_references`, `find_type_mutations`, `find_type_usages`, `symbol_relationships`, `callers_callees`
- **Actual:** Results overflow 88K–565K characters for popular types
- **Severity:** Performance
- **Recommendation:** Add `limit`/`offset` parameters.

### Issue 26: `list_analyzers` / `get_syntax_tree` — Large Outputs

- **Tools:** `list_analyzers` (175K chars), `get_syntax_tree` at depth=4 (111K–150K chars)
- **Severity:** Performance (minor)
- **Workaround:** Use `maxDepth=2` for `get_syntax_tree`.

---

## Cosmetic / Inconsistency

### Issue 27: `extract_interface_preview` — Missing Space in Generated Code

- **Tool:** `extract_interface_preview`
- **Expected:** `class Foo : IExisting, INew {`
- **Actual:** Generates `,INew{` with no space after comma or before brace. Also includes unnecessary usings.
- **Severity:** Cosmetic

### Issue 28: `type_hierarchy` / `symbol_info` — Returns Null Instead of Empty Arrays

- **Tools:** `type_hierarchy`, `symbol_info`
- **Expected:** `BaseTypes: []`, `Interfaces: []`
- **Actual:** `null` for empty collections
- **Severity:** Cosmetic — consumers must null-check instead of iterating.

### Issue 29: `get_cohesion_metrics` — Includes Interfaces in LCOM4 Results

- **Tool:** `get_cohesion_metrics`
- **Input:** `minMethods=3`
- **Expected:** Interfaces excluded (they inherently have no shared fields)
- **Actual:** Interfaces return LCOM4 = method count (trivially always true). E.g., `ISourceAdapter` returns LCOM4=6.
- **Severity:** Cosmetic / noise
- **Impact:** Pollutes results with non-actionable findings.

---

## Tools Verified Working Correctly

| Tool | Solutions Verified | Notes |
|------|-------------------|-------|
| `workspace_load/status/close` | All 3 | Clean load (IsTestProject/TFM issues noted separately) |
| `server_info` | All 3 | Accurate version, tool counts |
| `project_graph` | All 3 | Correct dependency structure |
| `compile_check` | All 3 | Accurate, fast (34ms normal, 2133ms emitValidation) |
| `project_diagnostics` | All 3 | Correct (large output — see Issue 24) |
| `security_diagnostics` | All 3 | Correct |
| `security_analyzer_status` | All 3 | Correct |
| `list_analyzers` | All 3 | Comprehensive |
| `get_complexity_metrics` (with filter) | All 3 | Accurate CC, LOC, nesting |
| `get_cohesion_metrics` | 2 of 3 | Correct on Roslyn-Backed-MCP, ITChatBot; crashes on NetworkDoc |
| `find_unused_symbols` (private) | All 3 | Correct for private symbols (false positives noted separately) |
| `get_namespace_dependencies` | All 3 | Correct |
| `get_nuget_dependencies` | All 3 | Correct |
| `source_generated_documents` | 2 of 3 | Correct |
| `semantic_search` | 2 of 3 | Accurate |
| `find_reflection_usages` | 2 of 3 | Correct (crashes on ITChatBot — Issue 7) |
| `get_di_registrations` | 2 of 3 | Correct (crashes on ITChatBot — Issue 7) |
| `get_editorconfig_options` | All 3 | Correct |
| `organize_usings_preview/apply` | All 3 | Correct |
| `rename_preview/apply` | 2 of 3 | Correct |
| `format_range_preview` | 2 of 3 | Correct |
| `remove_dead_code_preview/apply` | 2 of 3 | Correct |
| `move_type_to_file_preview` | All 3 | Works (issues noted separately) |
| `move_file_preview` | 2 of 3 | Correct with namespace update |
| `revert_last_apply` | 2 of 3 | Correct after actual apply (error when nothing to revert — Issue 20) |
| `build_workspace` | All 3 | Correct, matches compile_check |
| `test_run` | All 3 | Runs correctly (Skipped count wrong — Issue 17) |
| `evaluate_csharp` | 1 of 3 | Correct execution, proper runtime error handling |
| `analyze_snippet` | 1 of 3 | Correct for all `kind` values (usings issue — Issue 19) |
| `analyze_data_flow/control_flow` | All 3 | Correct for non-try-catch bodies |
| `get_syntax_tree` | All 3 | Correct AST (scoping/overflow issues noted) |
| `get_operations` | 2 of 3 | Correct IOperation kinds |
| `get_source_text` | 1 of 3 | Works on ITChatBot (crashes on Roslyn-Backed-MCP — Issue 2) |

---

## Consistency Checks

### `project_diagnostics` vs `compile_check`
- `project_diagnostics` includes all severity levels from compiler + analyzers
- `compile_check` returns only Error/Warning compiler diagnostics
- **Assessment:** Different scopes by design. Correct but could be documented.

### `build_workspace` vs `compile_check`
- Agree on error/warning counts
- `build_workspace` sometimes duplicates diagnostics in output array
- **Minor issue:** Diagnostic deduplication could be improved.

### Preview token invalidation
- Tokens correctly invalidated after workspace mutations
- Each preview must be immediately followed by its apply.

---

## Action Items

All recommendations and fix items have been consolidated into [`ai_docs/backlog.md`](backlog.md) (sections P0–P3). This report serves as the detailed reproduction reference for each issue.
