# Roslyn MCP Server Audit Report

**repo:** FirewallAnalyzer (DotNet-Firewall-Analyzer)
**date:** 2026-04-06
**server version:** Roslyn MCP v1.6.0 — catalog 2026.03
**roslyn version:** 5.3.0.0 / .NET 10
**workspace:** `FirewallAnalyzer.slnx` — 11 projects, 230 documents, 0 workspace diagnostics
**audit mode:** conservative (preview-only; no applies to main branch)
**auditor:** Claude Sonnet 4.6 via deep-review-and-refactor prompt

---

## Catalog Surface

| Category | Stable | Experimental | Total |
|----------|--------|-------------|-------|
| Tools | 56 | 67 | 123 |
| Resources | 7 | — | 7 |
| Prompts | — | 16 | 16 |

All 18 audit phases executed (Phase 18 N/A — no prior MCP issues in backlog).

---

## FLAGS (Bugs / Incorrect Behavior)

### FLAG-001 — `compile_check`: emitValidation speedup claim inaccurate
**Severity:** Low (doc only)
**Tool:** `compile_check`
**Description:** Documentation states `emitValidation=true` is "10-18x slower". Measured: 17ms (false) → 1598ms (true) ≈ **94× slower** on this solution.
**Impact:** Misleading perf guidance; agents may use emitValidation more liberally than warranted.

---

### FLAG-002 — `get_nuget_dependencies`: CPM versions not resolved
**Severity:** Medium
**Tool:** `get_nuget_dependencies`
**Description:** In a Central Package Management repo, all package versions are reported as `"centrally-managed"` — actual resolved version numbers (from `Directory.Packages.props`) are not surfaced.
**Impact:** Manual security auditing of versions requires a separate tool (`nuget_vulnerability_scan` uses dotnet CLI and resolves correctly; `get_nuget_dependencies` is not a reliable source for version data in CPM repos).

---

### FLAG-003 — `impact_analysis` vs `find_references`: inconsistent `Classification` contract
**Severity:** Medium
**Tools:** `impact_analysis`, `find_references`
**Description:** `impact_analysis` returns `"Classification": null` for all reference entries. `find_references` returns `"Classification": "Read"` for the same references. Sibling tools with different response contracts for the same field on the same symbol (`YamlConfigLoader.CollectUnknownYamlKeyMessages`, 19 references).
**Impact:** Consumers cannot rely on `Classification` from `impact_analysis` for cross-reference categorisation.

---

### FLAG-004 — `callers_callees`: duplicate callee entries for chained calls
**Severity:** Medium
**Tool:** `callers_callees`
**Description:** Calling `callers_callees` on `DriftDetector.RulesEqual` returns 9 duplicate callee entries for `ListsEqual` all pointing to the same file/line (line 67), all with `PreviewText: null`. `ListsEqual` is called 9 times in a `&&` expression chain; each call produces a separate (identical) callee record rather than being de-duplicated.
**Impact:** Callee lists are noisy/misleading for methods called multiple times in the same expression.

---

### FLAG-005 — `find_type_usages`: all static-class call sites classified as `"other"`
**Severity:** Low
**Tool:** `find_type_usages`
**Description:** All 18 usages of `TrafficSimulator` (a static class) are classified as `"other"` rather than more specific categories (e.g. `MethodInvocation`). The classification appears to only activate for type usages in declarations (variable types, base types), not for method-call-site references to static classes.
**Impact:** Reduced utility for automated static analysis; call-site usages are indistinguishable from other references.

---

### FLAG-006 — `symbol_signature_help`: cursor at return type resolves to return type, not method
**Severity:** Low
**Tool:** `symbol_signature_help`
**Description:** When called at a method signature declaration's return type position (e.g. line 84 col 28 of `YamlConfigLoader.cs`, inside `IReadOnlyList<string>`), the tool resolves the BCL type token rather than the method being declared. The documentation notes caret position matters but does not warn that positions within the return type of a method declaration resolve to the return type, not the method itself.
**Impact:** Confusing results; no actionable signature help for the intended method.

---

### FLAG-007 — `analyze_snippet` kind=`statements`: `return <value>` fails CS0127
**Severity:** Low
**Tool:** `analyze_snippet`
**Description:** The `statements` kind wraps code in a `void Run()` method, so `return y;` fails with CS0127. The `evaluate_csharp` scripting API handles `return` fine. The documentation describes `statements` as "method body" but does not clarify the void return constraint.
**Impact:** Confusing failures for users migrating snippets between `analyze_snippet` and `evaluate_csharp`.

---

### FLAG-008 — `evaluate_csharp`: `AppliedScriptTimeoutSeconds` always null
**Severity:** Low
**Tool:** `evaluate_csharp`
**Description:** `AppliedScriptTimeoutSeconds` field is always `null` in every response, regardless of execution time. The tool description says "The server enforces a script timeout (default 10 seconds)" but this cannot be verified from client-side results.
**Impact:** Agents cannot confirm whether server timeout protection is active for long-running scripts.

---

### FLAG-009 — `rename_preview`: non-differentiating error for BCL/metadata symbols
**Severity:** Low
**Tool:** `rename_preview`
**Description:** When called at a BCL type position (e.g. `IReadOnlyList` in a return type), returns `category: "InvalidArgument"` with "Symbol 'IReadOnlyList' is not from source" — the same error category used for genuinely invalid arguments (wrong types, out-of-range values). Should use a distinct category or message (e.g. "cannot rename metadata symbols").
**Impact:** Hard to distinguish invalid argument from "cannot rename this symbol type" in automated pipelines.

---

### FLAG-010 — `get_code_actions`: consistently returns 0 actions
**Severity:** High
**Tool:** `get_code_actions`
**Description:** Returns 0 actions at every tested position: class name (`JobTracker` line 9 col 14), switch expression range (lines 117-126), catch clause (lines 142-145). Expected Roslyn refactoring suggestions (Extract Interface, Move to File, Convert switch, etc.) are never surfaced.
**Impact:** The tool is non-functional in practice for this solution; agents using it to discover available refactorings get no results.

---

### FLAG-011 — Misleading "workspace may need to be reloaded" suffix on non-stale errors
**Severity:** Medium
**Tools:** `remove_dead_code_preview`, `evaluate_msbuild_property`, `analyze_data_flow`, and others
**Description:** Multiple tools append "The workspace may need to be reloaded (workspace_reload) if the state is stale." to `InvalidOperation` errors that have nothing to do with workspace staleness — e.g. "Symbol 'JobTracker' still has references and cannot be removed safely" or "Project 'NonExistentProject' not found in workspace."
**Impact:** Agents may attempt unnecessary workspace reloads, or mistrust valid error messages. The suffix should only appear when staleness is a plausible cause (e.g., file-not-found-in-workspace after external edits).

---

### FLAG-012 — `extract_type_preview`: generated field declaration has no whitespace
**Severity:** High
**Tool:** `extract_type_preview`
**Description:** The patch for `JobTracker.cs` after extracting 3 members into `JobTrackerScavenger` inserts the new field as `privatereadonlyJobTrackerScavenger_jobTrackerScavenger;` — all tokens concatenated with no whitespace. This is non-compilable output.
**Impact:** Applying this preview would break the source file. The preview itself is silently incorrect.

---

### FLAG-013 — `extract_type_preview`: extracted class references parent-only fields — non-compilable
**Severity:** High
**Tool:** `extract_type_preview`
**Description:** `JobTrackerScavenger.cs` references `_cancellationTokens`, `_jobs`, `_retentionPeriod`, and `MaxRetainedJobs`, which are fields/constants of `JobTracker` and are not injected into the extracted class. No constructor, no parameters, no warning emitted. The resulting code would fail to compile.
**Impact:** Applying this preview would produce non-compilable code with no advance warning.

---

### FLAG-014 — `extract_interface_preview` / `extract_interface_cross_project_preview`: missing space before `{`
**Severity:** Low
**Tools:** `extract_interface_preview`, `extract_interface_cross_project_preview`
**Description:** Both tools generate a `JobTracker.cs` patch with `public class JobTracker\n+: IJobTracker{` — no space between `IJobTracker` and `{`. Violates standard C# formatting conventions and the project's own editorconfig (`csharp_new_line_before_open_brace: all`).
**Impact:** Minor formatting debt; would be caught by `format_document_preview` after apply.

---

### FLAG-015 — `extract_interface_cross_project_preview`: wrong target directory + spurious using
**Severity:** Medium
**Tool:** `extract_interface_cross_project_preview`
**Description:** When extracting `IJobTracker` to `FirewallAnalyzer.Domain`, the interface file is placed at the project root (`src/FirewallAnalyzer.Domain/IJobTracker.cs`) rather than the existing `Interfaces/` subdirectory that holds `IPanosClient.cs` and `ISnapshotStore.cs`. Also adds `using System.Collections.Concurrent;` which is not used in the interface contract.
**Impact:** Convention violation; spurious import would be flagged by `organize_usings_preview`.

---

### FLAG-016 — `move_type_to_file_preview`: 3 leading blank lines + lost property spacing
**Severity:** Low
**Tool:** `move_type_to_file_preview`
**Description:** Generated `DriftReport.cs` has 3 leading blank lines before the namespace declaration and collapses the blank lines between properties that existed in the source file. Formatting is inconsistent with the project's editorconfig style.
**Impact:** Minor formatting debt; would be caught by `format_document_preview` after apply.

---

### FLAG-017 — `scaffold_test_preview`: generates MSTest despite project using xUnit
**Severity:** High
**Tool:** `scaffold_test_preview`
**Description:** Scaffolds `[TestClass]`, `[TestMethod]`, `Assert.IsNotNull`, `Assert.Fail` (MSTest API) when the target test project (`FirewallAnalyzer.Infrastructure.Tests`) uses xUnit. The project has no MSTest dependency, so applying this scaffold would produce unresolvable references.
**Impact:** Applying the scaffold would produce non-compilable test files. No test framework detection is performed.

---

### FLAG-018 — `scaffold_test_preview`: does not call the requested private method
**Severity:** Medium
**Tool:** `scaffold_test_preview`
**Description:** When `targetMethodName: "ScavengeUnsafe"` is specified (a `private` method), the scaffold creates an `[TestMethod]` that instantiates `JobTracker` and then calls `Assert.Fail("Add real assertions.")` — `ScavengeUnsafe` is never mentioned. There is no access-modifier warning.
**Impact:** The scaffold is a non-functional placeholder that doesn't reflect the requested test target.

---

### FLAG-019 — `set_project_property_preview`: malformed XML — inline `<PropertyGroup>`
**Severity:** High
**Tool:** `set_project_property_preview`
**Description:** When setting `LangVersion=13` on `FirewallAnalyzer.Api`, the generated diff appends `<PropertyGroup><LangVersion>13</LangVersion></PropertyGroup>` on the same line as the opening `<Project>` tag: `<Project Sdk="..."><PropertyGroup>...`. This produces malformed, unreadable XML that violates standard `.csproj` structure.
**Impact:** Applying this preview would produce a csproj that may confuse MSBuild or tooling that formats project files.

---

### FLAG-020 — `add_package_reference_preview`: missing newline before `</Project>`
**Severity:** Low
**Tool:** `add_package_reference_preview`
**Description:** The generated diff places `</ItemGroup></Project>` on the same line without a separating newline. The resulting csproj would have the closing `</Project>` on the same line as `</ItemGroup>`.
**Impact:** Minor formatting issue; MSBuild handles this but tooling/diffing is harder to read.

---

### FLAG-021 — `go_to_definition` + `impact_analysis`: `Classification: null` for all results
**Severity:** Low
**Tools:** `go_to_definition`, `impact_analysis`
**Description:** Both tools return `"Classification": null` for all results. `find_references` returns `"Classification": "Read"` for the same symbol. Inconsistent classification contract across three related navigation tools.
**Impact:** Automated post-processing of results cannot rely on classification across tool families.

---

### FLAG-022 — `get_completions`: `InlineDescription` always empty; no pagination
**Severity:** Low
**Tool:** `get_completions`
**Description:** `InlineDescription` is `""` for every completion item regardless of type — no type signatures or documentation snippets are included. `IsIncomplete: true` indicates a larger list exists, but there is no `limit` or `offset` parameter to retrieve the remaining items.
**Impact:** Completions are less informative than IDE IntelliSense; paginating through completions is impossible.

---

### FLAG-023 — `go_to_definition` out-of-bounds line: exposes internal parameter name
**Severity:** Low
**Tool:** `go_to_definition`
**Description:** Requesting line 99999 (beyond file length) returns `ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'index')`. "Parameter 'index'" is an internal implementation detail; the user-facing message should say "line 99999 is beyond the file's line count".
**Impact:** Confusing error message for invalid line numbers.

---

### FLAG-024 — `document_symbols` on nonexistent file: silent empty result
**Severity:** Medium
**Tool:** `document_symbols`
**Description:** Requesting `document_symbols` on `src/DOES_NOT_EXIST/Fake.cs` returns `[]` with no error. This is identical to the result for a valid file that contains no type declarations.
**Impact:** Agents cannot distinguish "file not found" from "file exists but has no symbols". Should return an error or include a `found: false` field.

---

### FLAG-025 — Resource discovery gap: workspace-scoped resources not in `resources/list`
**Severity:** Low
**Tool:** `ListMcpResourcesTool` / MCP resource surface
**Description:** `resources/list` returns only 3 static resources (`workspaces`, `server/catalog`, `server/resource-templates`). The 4 workspace-scoped resource templates (`source_file`, `workspace_diagnostics`, `workspace_projects`, `workspace_status`) are invisible without first reading `roslyn://server/resource-templates`. The `resource-templates` description does acknowledge this, but it requires a priori knowledge to discover.
**Impact:** MCP clients that rely solely on `resources/list` for discovery will miss 4 of 7 resources.

---

## Observations (Not Bugs)

| # | Tool / Area | Observation |
|---|-------------|-------------|
| OBS-1 | Project config | `AnalysisLevel=9.0` with `TargetFramework=net10.0` — analysis level trails TFM by one version; .NET 10 analyzer rules inactive |
| OBS-2 | Project config | `EnforceCodeStyleInBuild=false` — editorconfig `warning`-severity rules (namespace declarations, using placement) are IDE-only; won't fail CI |
| OBS-3 | Coverage | `ObjectInventoryQuery` (412 lines) and `RuleQuery` (162 lines) both at 0% line coverage in Application.Tests |
| OBS-4 | Usings | 2 unused imports in `ApiHostBuilder.cs` (`PanosClient`, `Microsoft.Extensions.Logging`) — found by `organize_usings_preview`, not enforced in CI |
| OBS-5 | `IpInCidr` / `CollectUnknownYamlKeyMessages` | Both use catch-all exception handlers that silently swallow exceptions — intentional design per code comments |
| OBS-6 | `Build` (ApiHostBuilder) | `settings` and `resilienceTimeoutSeconds` captured by closures; 40 local variables — CC=18 is accurate; method is a structural startup orchestration method, not algorithm logic |
| OBS-7 | `callers_callees` on `Simulate` | 18 callers correctly found across 2 projects — 14 test, 4 production |
| OBS-8 | `semantic_search` | `Signature` field omits return type and `async` keyword — cannot verify query match from result alone |
| OBS-9 | `fix_all_preview` for IDE0005 | Correctly redirects to `organize_usings_preview` with a guidance message — good defensive UX |
| OBS-10 | `revert_last_apply` | Correctly returns `reverted: false` on empty undo stack with clear message |

---

## Tool Coverage Ledger

Tools exercised by category during this audit:

| Category | Exercised | Notes |
|----------|-----------|-------|
| **Workspace** | `workspace_load`, `workspace_list`, `workspace_status`, `workspace_close` (via load), `workspace_reload` (indirect) | All stable workspace ops covered |
| **Diagnostics** | `project_diagnostics`, `compile_check`, `security_diagnostics`, `nuget_vulnerability_scan`, `list_analyzers`, `diagnostic_details` | Full diagnostic surface |
| **Metrics** | `get_complexity_metrics`, `get_cohesion_metrics`, `get_namespace_dependencies`, `get_coupling_metrics` | All 4 metric tools |
| **Symbol analysis** | `find_references`, `find_references_bulk`, `impact_analysis`, `callers_callees`, `find_shared_members`, `find_type_mutations`, `find_type_usages`, `symbol_signature_help`, `find_property_writes`, `member_hierarchy`, `find_implementations`, `find_consumers`, `symbol_relationships`, `find_base_members`, `find_overrides` | Near-complete symbol surface |
| **Flow analysis** | `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `get_syntax_tree`, `get_source_text` | All 5 flow tools |
| **Snippet/Script** | `analyze_snippet`, `evaluate_csharp` | Both tools |
| **Refactoring** | `rename_preview`, `format_document_preview`, `organize_usings_preview`, `extract_interface_preview`, `extract_interface_cross_project_preview`, `extract_type_preview`, `remove_dead_code_preview`, `get_code_actions`, `code_fix_preview`, `fix_all_preview`, `move_file_preview`, `move_type_to_file_preview`, `create_file_preview`, `revert_last_apply` | All major refactoring preview ops |
| **EditorConfig/MSBuild** | `get_editorconfig_options`, `evaluate_msbuild_property`, `evaluate_msbuild_items`, `get_msbuild_properties` (skipped — large), `set_editorconfig_option` (skipped — no preview) | Core MSBuild evaluation covered |
| **Build/Test** | `build_workspace`, `test_discover`, `test_run`, `test_coverage` | All 4 tools |
| **Project mutation** | `add_package_reference_preview`, `set_project_property_preview` | Core mutation previews |
| **Scaffolding** | `scaffold_test_preview`, `scaffold_type_preview` | Both tools |
| **Navigation** | `go_to_definition`, `goto_type_definition`, `document_symbols`, `get_completions`, `symbol_search`, `semantic_search` | Core navigation tools |
| **Search** | `find_unused_symbols`, `symbol_search`, `semantic_search` | Dead code + semantic search |
| **Resources** | `roslyn://workspaces`, `roslyn://server/catalog`, `roslyn://server/resource-templates` | All 3 static resources; 4 workspace-scoped verified via templates |
| **Prompts** | Verified via catalog (16 prompts listed) | All experimental; verified names and summaries |
| **Boundary testing** | Invalid workspaceId, OOB line, nonexistent file, same-name rename, keyword rename, inverted range, missing project | 7 negative cases |

**Tools not exercised** (experimental / out-of-scope for this audit):
- `apply_*` family (conservative mode)
- `set_diagnostic_severity`, `add_pragma_suppression`
- `split_class_preview`, `dependency_inversion_preview`
- `bulk_replace_type_*`
- `evaluate_msbuild_items` for Compile items (large output)
- `source_generated_documents`
- `get_inlay_hints`, `get_semantic_tokens`, `get_folding_ranges`, `selection_range`

---

## Codebase Health Summary

| Metric | Value |
|--------|-------|
| Build | ✅ 0 errors, 0 warnings |
| Tests (unit + integration) | ✅ 297/297 pass |
| Security diagnostics | ✅ 0 findings |
| Unused symbols (private/internal) | ✅ 0 |
| Nullable annotations | ✅ enabled |
| TreatWarningsAsErrors | ✅ true |
| Cyclomatic complexity hotspots | `Build` CC=18, `CollectUnknownYamlKeyMessages` CC=20, `IpInCidr` CC=14 |
| Line coverage (Application.Tests) | 56% line / 47.6% branch |
| Zero-coverage classes | `ObjectInventoryQuery` (412L), `RuleQuery` (162L) |

---

## Priority Summary

| Priority | Count | IDs |
|----------|-------|-----|
| **High** | 4 | FLAG-010, FLAG-012, FLAG-013, FLAG-017, FLAG-019 |
| **Medium** | 7 | FLAG-002, FLAG-003, FLAG-004, FLAG-011, FLAG-015, FLAG-018, FLAG-021(?), FLAG-024 |
| **Low** | 14 | FLAG-001, FLAG-005 through FLAG-009, FLAG-014, FLAG-016, FLAG-020, FLAG-021, FLAG-022, FLAG-023, FLAG-025 |

> **High** = would produce non-compilable output on apply, or a major tool surface that returns nothing useful
> **Medium** = incorrect data or misleading behavior that could misdirect agent workflows
> **Low** = documentation gaps, minor formatting issues, convenience deficits

---

*Report generated by Claude Sonnet 4.6 via `ai_docs/prompts/deep-review-and-refactor.md`*
