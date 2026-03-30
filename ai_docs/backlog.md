# Backlog

Canonical location for all unfinished work items. Prioritized by impact.

Last audit: 2026-03-29. Consolidated from deep-review-report.md, mcp-server-audit-report.md, roslyn-gap-analysis.md, and prior backlog.

---

## P0 — Critical Server Bugs (blocking core workflows)

These block common agent and user workflows. See `mcp-server-audit-report.md` for full reproduction details.

- [ ] **BUG-01**: `fix_all_preview` crashes at all scopes on all solutions tested (10/10 attempts). Primary batch-refactoring tool is non-functional.
- [ ] **BUG-02**: `callers_callees` resolves async method declarations to `Task<T>` return type instead of the method itself. Returns completely wrong data for most modern C# code.
- [ ] **BUG-03**: `IsTestProject` detection fails for xUnit/NUnit projects (works for MSTest only). Cascades to `test_discover` and `test_related_files` returning empty.
- [ ] **BUG-04**: Workspace sessions drop silently after inactivity with no error context. All state is lost.
- [ ] **BUG-05**: All tool crashes return identical `"An error occurred invoking '<tool>'."` with no exception type, message, or stack trace. Makes debugging impossible.

---

## P1 — High-Priority Server Bugs (crashes or wrong output on some solutions)

- [ ] **BUG-06**: `extract_interface_preview` / `extract_interface_cross_project_preview` — crash on some solutions; wrong namespace on others.
- [ ] **BUG-07**: `get_source_text` crashes on some solutions (works on others). Fundamental tool.
- [ ] **BUG-08**: `find_reflection_usages` / `get_di_registrations` crash on ITChatBot.sln but work on other solutions.
- [ ] **BUG-09**: `get_cohesion_metrics` crashes on NetworkDocumentation.sln but works on others.
- [ ] **BUG-10**: `test_discover` crashes on ITChatBot.sln, returns empty on NetworkDocumentation.sln.
- [ ] **BUG-11**: `analyze_data_flow` / `analyze_control_flow` crash on try-catch / large method bodies. Works on smaller ranges.
- [ ] **BUG-12**: `move_type_to_file_preview` keeps invalid `private` modifier on top-level types; copies unnecessary usings.
- [ ] **BUG-13**: `test_run` structured output reports `Skipped: 0` when tests were actually skipped (stdout shows correct count).
- [ ] **BUG-14**: `find_type_mutations` Dispose() over-matches all `IDisposable.Dispose()` calls solution-wide instead of scoping to the target type.
- [ ] **BUG-15**: `analyze_snippet` `usings` parameter adds using directives but not assembly references — `System.Text.Json` etc. fail to resolve.
- [ ] **BUG-16**: `revert_last_apply` returns generic crash error when nothing to revert instead of structured "nothing to revert" response.

---

## P2 — Code Quality Improvements

Source: deep-review-report.md (2026-03-28). Codebase is healthy (0 errors, 143 tests pass) but has actionable improvements.

- [ ] **CODE-01**: Decompose `GetCohesionMetricsAsync` (CC=24) — extract LCOM4 cluster computation and field-access analysis into separate methods.
- [ ] **CODE-02**: Decompose `ParseSemanticQuery` (CC=22) — extract predicate builders into a dictionary-of-strategies pattern. Remove redundant inner guard (lines 218-220).
- [ ] **CODE-03**: Fix fragile closure capture of `accessibility` loop variable in `ParseSemanticQuery` line 241 — capture to local variable before lambda.
- [ ] **CODE-04**: Decompose `ExecuteAsync` in ToolErrorHandler (CC=21) — consider dictionary-based exception handler mapping for 9 catch clauses.
- [ ] **CODE-05**: Remove dead method `CreateFixtureCopy` in `TestBase.cs` (confirmed zero callers, safe to remove).
- [ ] **CODE-06**: Add `.editorconfig` — none exists. Add one to enforce consistent code style across the solution.
- [ ] **CODE-07**: Adopt `LoggerMessage.Define` pattern (CA1848) for performance-critical logging paths in hot service methods.
- [ ] **CODE-08**: Extract shared `DotnetCommandRunner` from `BuildService` and `TestRunnerService` (both have LCOM4=2 with identical infrastructure cluster).

---

## P3 — Server Data Quality & Usability

- [ ] **DATA-01**: `find_shared_members` returns empty for types with readonly fields and static classes. Should include field reads, not just writes.
- [ ] **DATA-02**: `find_unused_symbols` false positives — deduplicate results, exclude `obj/` and NuGet `_content/` paths, consider attribute-aware analysis for framework-invoked methods.
- [ ] **DATA-03**: `TargetFrameworks` shows "unknown" for most projects across all solutions. Fix MSBuildWorkspace TFM resolution.
- [ ] **DATA-04**: `get_complexity_metrics` returns alphabetical order without `minComplexity` filter. Should sort by CC descending by default.
- [ ] **DATA-05**: `get_cohesion_metrics` includes interfaces in LCOM4 results (trivially LCOM4 = method count). Filter or flag separately.
- [ ] **DATA-06**: `get_code_actions` returns empty at most positions. May need additional code fix providers loaded in MSBuildWorkspace.
- [ ] **DATA-07**: Add `limit`/`offset` to `find_references`, `find_type_mutations`, `find_type_usages`, `symbol_relationships`, `callers_callees` — prevent multi-hundred-KB output overflows.
- [ ] **DATA-08**: Add severity filter default to `project_diagnostics` — avoid multi-MB output for Hidden diagnostics.
- [ ] **DATA-09**: `extract_interface_preview` missing spaces in generated base type list. Also copies unnecessary usings.
- [ ] **DATA-10**: `type_hierarchy` / `symbol_info` return null instead of empty arrays for absent collections.
- [ ] **DATA-11**: `get_syntax_tree` line range filter does not scope root — returns entire class with all siblings.

---

## P4 — Feature Additions

- [ ] **FEAT-01**: Add security diagnostic surface — curated mapping of Roslyn diagnostic IDs to OWASP categories with fix guidance. See `ai_docs/prompts/add-security-diagnostic-surface.prompt.md` for full specification.
- [ ] **FEAT-02**: `.editorconfig` write support — current `get_editorconfig_options` is read-only. Add ability to modify style rules programmatically.
- [ ] **FEAT-03**: MSBuild property/item evaluation — query resolved MSBuild properties, conditional values, SDK metadata.
- [ ] **FEAT-04**: Suppression & severity management — programmatic pragma suppression, SuppressMessage, and .editorconfig severity overrides.
- [ ] **FEAT-05**: Maintainability Index — composite metric extending existing complexity_metrics.
- [ ] **REL-10**: Create 512x512 PNG icon for MCP directory listing and manifest. Required by [Anthropic MCP submission guide](https://support.claude.com/en/articles/12922832-local-mcp-server-submission-guide).

---

## Rules

- Keep only incomplete items above. No completed items, no archive.
- Reprioritize on each audit pass.
- Bug items reference `mcp-server-audit-report.md` for full reproduction details.
- Code items reference `ai_docs/archive/deep-review-report.md` for analysis context.
