# Backlog

Canonical location for all unfinished work items. Prioritized by impact.

Last audit: 2026-03-30. Consolidated from deep-review-report.md, mcp-server-audit-report.md (35 issues across 4 solutions), and prior backlog.

---

## P0 — Critical Server Bugs (blocking core workflows)

These block common agent and user workflows. See `mcp-server-audit-report.md` for full reproduction details.

- [ ] **BUG-01**: `fix_all_preview` crashes at all scopes on some solutions tested (audit 2026-03-28). Partially works on SampleSolution (returns clear error for compiler warnings lacking fix providers). Needs wider retest.
- [ ] **BUG-04**: Workspace sessions drop silently after inactivity with no error context. All state is lost.
- [ ] **BUG-05**: Many tool crashes return identical `"An error occurred invoking '<tool>'."` with no exception type, message, or stack trace.

### Resolved (P0)
- [x] **BUG-02**: `callers_callees` async method resolution — verified working correctly in v1.2.0 (2026-03-30 audit).
- [x] **BUG-03**: `IsTestProject` detection — verified working for MSTest in v1.2.0.

---

## P1 — High-Priority Server Bugs (crashes or wrong output on some solutions)

- [ ] **BUG-06**: `extract_interface_preview` — crash on some solutions. Cross-project namespace issue fixed (AUDIT-22); conflict detection added (AUDIT-28).
- [ ] **BUG-07**: `get_source_text` crashes on some solutions (works on others). Fundamental tool.
- [ ] **BUG-08**: `find_reflection_usages` / `get_di_registrations` previously crashed on ITChatBot.sln (2026-03-28 audit). Now functional in v1.2.0 and NuGet content pollution fixed (AUDIT-29). May need retest for crash regression.
- [ ] **BUG-09**: `get_cohesion_metrics` crashes on NetworkDocumentation.sln but works on others.
- [ ] **BUG-10**: `test_discover` crashes on ITChatBot.sln, returns empty on NetworkDocumentation.sln.
- [ ] **BUG-11**: `analyze_data_flow` / `analyze_control_flow` crash on try-catch / large method bodies. Works on smaller ranges.
- [ ] **BUG-12**: `move_type_to_file_preview` keeps invalid `private` modifier on top-level types; copies unnecessary usings.
- [ ] **BUG-14**: `find_type_mutations` Dispose() over-matches all `IDisposable.Dispose()` calls solution-wide instead of scoping to the target type.
- [ ] **BUG-15**: `analyze_snippet` `usings` parameter adds using directives but not assembly references — `System.Text.Json` etc. fail to resolve.
### Resolved (P1)
- [x] **BUG-13**: `test_run` skipped count — fixed in PR #39.
- [x] **BUG-16**: `revert_last_apply` — now returns structured "nothing to revert" response (verified 2026-03-30).
- [x] **AUDIT-01**: `scaffold_test_preview` — fixed type resolution and removed hardcoded `using SampleLib;`. Now resolves target type namespace and constructor parameters from the Roslyn compilation.
- [x] **AUDIT-22**: `extract_interface_cross_project_preview` — now derives namespace from target project's default namespace for cross-project extractions. Filters usings to those needed by the interface signature.
- [x] **AUDIT-28**: `extract_interface_cross_project_preview` — now checks all projects in the solution for existing types with the same name before generating the interface.
- [x] **AUDIT-29**: `find_reflection_usages` — now filters out NuGet content files (`_content/`, `obj/`, `.nuget/`, `contentFiles/`) from results via shared `PathFilter` helper.

---

## P2 — Code Quality Improvements

Source: deep-review-report.md (2026-03-30). Codebase is healthy (0 errors, 143 tests pass, 49.8% line coverage).

- [ ] **CODE-04**: Decompose `ExecuteAsync` in ToolErrorHandler (CC=21) — consider dictionary-based exception handler mapping for 9 catch clauses.
- [ ] **CODE-07**: Adopt `LoggerMessage.Define` pattern (CA1848) for performance-critical logging paths in hot service methods.
- [ ] **CODE-08**: Extract shared `DotnetCommandRunner` from `BuildService` and `TestRunnerService` (both have LCOM4=2 with identical infrastructure cluster).
- [ ] **CODE-09**: Decompose `PreviewExtractInterfaceAsync` (CC=35, 199 LOC, 57 locals, nesting=6). Strongest refactoring candidate. Extract type-finding, member-filtering, interface-generation, and diff-computation sub-methods.
- [ ] **CODE-10**: Add XML documentation to `InterfaceExtractionService` and `RefactoringService` (currently missing, `WorkspaceManager` has good docs).
- [ ] **CODE-11**: Increase test coverage from 49.8% line / 37.6% branch. Focus on high-complexity methods in Roslyn.Services layer.

### Resolved (P2)
- [x] **CODE-01**: Decompose `GetCohesionMetricsAsync` — done in PR #41.
- [x] **CODE-02**: Decompose `ParseSemanticQuery` — done in PR #41.
- [x] **CODE-03**: Fix closure capture in `ParseSemanticQuery` — done in PR #41.
- [x] **CODE-05**: Remove dead `CreateFixtureCopy` — done in PR #42.
- [x] **CODE-06**: Add `.editorconfig` — done in PR #42.

---

## P3 — Server Data Quality & Usability

- [ ] **DATA-01**: `find_shared_members` returns empty for types with readonly fields and static classes. Should include field reads, not just writes.
- [ ] **DATA-02**: `find_unused_symbols` false positives — deduplication and generated-file filtering fixed (AUDIT-19, AUDIT-20). Remaining: consider attribute-aware analysis for framework-invoked methods (e.g. `[Fact]`, `[DataMember]`).
- [ ] **DATA-03**: `TargetFrameworks` shows "unknown" for most projects across all solutions. Fix MSBuildWorkspace TFM resolution. (Still observed in 2026-03-30 audit — AUDIT-07.)
- [ ] **DATA-05**: `get_cohesion_metrics` includes interfaces in LCOM4 results (trivially LCOM4 = method count). `excludeTests` filter added (AUDIT-36). Remaining: filter/flag interfaces separately.
- [ ] **DATA-06**: `get_code_actions` returns empty at most positions. May need additional code fix providers loaded in MSBuildWorkspace.
- [ ] **DATA-07**: Add `limit`/`offset` to `find_references`, `find_type_mutations`, `find_type_usages`, `symbol_relationships`, `callers_callees` — prevent multi-hundred-KB output overflows.
- [ ] **DATA-09**: `extract_interface_preview` missing spaces in generated base type list. Also copies unnecessary usings.
- [ ] **AUDIT-08**: `list_analyzers` (175K-252K chars) and `project_diagnostics` (626K chars on ITChatBot) have no effective pagination. `project` filter barely helps for analyzers (workspace-level). Add `offset`/`limit` params to both.
- [ ] **AUDIT-10**: `split_class_preview` reformats unrelated code during partial class split.
- [ ] **AUDIT-11**: `source_generated_documents` shows duplicate entries for Debug/Release/platform obj folders.
- [ ] **AUDIT-12**: `add_package_reference_preview` generates inline XML without indentation.
- [ ] **AUDIT-14**: Resource listing omits dynamic workspace-specific resources; not discoverable via MCP resource listing.
- [ ] **AUDIT-15**: Resource DTO property name inconsistency (`Name` vs `ProjectName` across resource responses).
- [ ] **AUDIT-06**: `server_info` surface counts (116/6/16) don't match catalog counts. Minor discrepancy.
- [ ] **AUDIT-21**: `fix_all_preview` returns "No code fix provider" for IDE0005, CS8019, CS0414, CA5351, CA1707. IDE and CA analyzers/fixers not loaded in MSBuildWorkspace (SDK-implicit only active during `dotnet build`). `project_diagnostics` reports 1,107 CA warnings on ITChatBot but none are fixable. Workaround: `organize_usings_preview` for unused usings.
- [ ] **AUDIT-23**: `semantic_search` unreliable for generic return type queries (e.g., `Task<bool>`). Returns 0 on NetworkDocumentation; intermittent on ITChatBot (0 on first call, 2 correct on second). Non-generic version works fine.
- [ ] **AUDIT-32**: `get_syntax_tree` exceeds output (142K chars) for methods >100 LOC at `maxDepth=3`. Workaround: use smaller line ranges or `maxDepth=2`.
- [ ] **AUDIT-33**: `analyze_data_flow` variable names not scope-disambiguated. Same-named variables in different loop scopes appear as duplicates in flat list. Add scope/line info per entry.
- [ ] **AUDIT-34**: `find_consumers` misses DI generic argument registration pattern (e.g., `AddSingleton<IFoo, Foo>`). `find_references` captures it but `find_consumers` does not classify it.
- [ ] **AUDIT-35**: `find_unused_symbols` no confidence scoring for public symbols. Flags public enum values and DTO properties as unused. Needs high/medium/low confidence or `excludeEnums`/`excludeRecordProperties` options.

### Resolved (P3)
- [x] **DATA-04**: `get_complexity_metrics` sorting — fixed in PR #40.
- [x] **DATA-08**: `project_diagnostics` severity filter — fixed in PR #40.
- [x] **DATA-10**: `type_hierarchy` / `symbol_info` null vs empty arrays — fixed in PR #40.
- [x] **DATA-11**: `get_syntax_tree` line range scoping — fixed in PR #40.
- [x] **AUDIT-02**: `test_run` — now returns structured error with list of available test projects when none found (PR #45).
- [x] **AUDIT-03**: `move_type_to_file_preview` — error message now clarifies nested types cannot be extracted (PR #46).
- [x] **AUDIT-04**: `set_project_property_preview` — creates missing PropertyGroup; detects and reports no-op when property already set (PR #46).
- [x] **AUDIT-05**: `find_overrides` token resolution — now falls back to preceding token when cursor lands mid-identifier (PR #45).
- [x] **AUDIT-09**: `build_workspace` duplicate diagnostics — deduped by (Id, FilePath, Line, Column) in `DotnetOutputParser` (PR #46).
- [x] **AUDIT-13**: `goto_type_definition` — now throws descriptive error for built-in/primitive types with no source location (PR #46).
- [x] **AUDIT-19**: `find_unused_symbols` duplicate results — deduped via `HashSet<ISymbol>(SymbolEqualityComparer.Default)` (PR #45).
- [x] **AUDIT-20**: `find_unused_symbols` source-gen false positives — generated/content files filtered via `PathFilter` (PR #45).
- [x] **AUDIT-24**: `test_discover` output size — added `projectName` filter and `limit` parameter (PR #47).
- [x] **AUDIT-25**: `get_namespace_dependencies` graph size — added `circularOnly` filter parameter (PR #47).
- [x] **AUDIT-30**: `find_base_members` implicit interface implementations — now yields base interface members via `FindImplementationForInterfaceMember` (PR #45).
- [x] **AUDIT-31**: `compile_check` tool description — clarified CS* only, no CA*/IDE*, and `emitValidation` 10-18x slowdown (PR #48).
- [x] **AUDIT-36**: `get_cohesion_metrics` test class pollution — added `excludeTests` filter parameter (PR #47).

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

- Keep only incomplete items above (resolved items in "Resolved" sub-sections for audit trail).
- Reprioritize on each audit pass.
- Bug items reference `mcp-server-audit-report.md` for full reproduction details.
- Code items reference `ai_docs/deep-review-report.md` for analysis context.
