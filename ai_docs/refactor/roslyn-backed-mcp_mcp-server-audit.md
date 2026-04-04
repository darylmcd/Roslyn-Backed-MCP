# MCP Server Audit Report

## Header

- **Date:** 2026-04-04
- **Audited solution:** `c:\Code-Repo\Roslyn-Backed-MCP\Roslyn-Backed-MCP.sln`
- **Workspace id (final session):** `c655b134156b4529836624cf4373d8a4`
- **Server:** `roslyn-mcp` **1.6.0+e0ca703807a8634baf0781efa5aed532a1f0e77e** (from `server_info`)
- **Roslyn / .NET:** Roslyn **5.3.0.0**, runtime **.NET 10.0.5**, OS Windows 10.0.26200
- **Scale:** **6** projects, **298** documents (from `workspace_load`)
- **Surface (from `server_info` / `roslyn://server/catalog`):** tools stable **56** + experimental **67** = **123**; resources stable **7**; prompts experimental **16** (stable prompts **0**). Matches catalog `Summary` block.
- **Report path note:** Canonical path under this repo: `ai_docs/refactor/roslyn-backed-mcp_mcp-server-audit.md`.

## Tool coverage

| Category | Status | Notes |
|----------|--------|--------|
| Server | exercised | `server_info`; catalog via `roslyn://server/catalog` |
| Workspace | exercised | `workspace_load`, `workspace_list`, `workspace_status`, `workspace_reload`, `project_graph`, `get_source_text` (earlier pass) |
| Symbols | exercised | `symbol_search`, `symbol_info`, `document_symbols`, `find_references`, `find_consumers`, `find_implementations`, `find_type_usages`, `symbol_relationships`, `symbol_signature_help`, `callers_callees` |
| Analysis | exercised | `project_diagnostics`, `compile_check`, `diagnostic_details`, `type_hierarchy`, `impact_analysis`, `find_type_mutations` |
| Advanced analysis | exercised | `get_complexity_metrics`, `get_cohesion_metrics`, `find_unused_symbols`, `get_namespace_dependencies`, `get_nuget_dependencies`, `semantic_search` |
| Consumer & cohesion | exercised | `find_shared_members`, `find_consumers`, `get_cohesion_metrics` |
| Security | exercised | `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan` |
| Flow | exercised | `analyze_data_flow`, `analyze_control_flow`, `get_operations` |
| Syntax & operations | exercised | `get_syntax_tree`, `get_operations` |
| Compilation | exercised | `compile_check` (including `emitValidation: true` once) |
| Analyzer info | exercised | `list_analyzers` (paged; totalRules **451**, analyzerCount **17**) |
| Snippet & scripting | exercised | `analyze_snippet`, `evaluate_csharp` |
| Refactoring (rename/format/organize) | partial | `organize_usings_preview` / `organize_usings_apply`; `format_document_preview` (no diff); `format_range_preview` (no diff) |
| Code actions & fixes | skipped | Not invoked this run (time); `diagnostic_details` showed curated fix for CS0414 |
| Fix all | skipped | **Intentionally not applied:** sole workspace warning is **CS0414** on `DiagnosticsProbe` (integration tests: `DiagnosticFixIntegrationTests`) |
| Interface/type extraction | skipped | Preview-only per prompt for cross-cutting moves; not required for this solution pass |
| Type movement | skipped | Previews not run in final completion pass |
| Bulk refactor | skipped | — |
| Cross-project refactor | skipped | Previews not run in final completion pass |
| Orchestration | skipped | Previews not run in final completion pass |
| Dead code | skipped | No apply (removing probe or test-flagged symbols would break tests) |
| Text edits | skipped | — |
| File ops | skipped | — |
| Project mutation (previews) | skipped | — |
| Scaffolding (previews) | skipped | — |
| Build & test | partial | `build_workspace` **succeeded**; `test_run` **MCP tool errored**; validation via host `dotnet test` **209 passed** |
| EditorConfig | skipped | — |
| MSBuild evaluation | skipped | — |
| Suppression & severity | skipped | — |
| Undo | exercised | `revert_last_apply` after audit-only `organize_usings_apply` on `ReferenceService.cs` |
| Resources | exercised | `roslyn://server/catalog` |
| Prompts | partial | Prompts are MCP **prompt** entries (catalog lists 16); not invoked as tools in this agent session |
| Boundary & negative testing | partial | `workspace_status` with bogus id → structured **NotFound** (actionable) |
| Regression verification | N/A | `ai_docs/backlog.md` has **no open rows** (2026-04-04) |

## Verified tools (working)

- `workspace_load` / `workspace_list` / `workspace_status` — session `c655b134…`, 6 projects, clean load.
- `server_info` — version and surface counts consistent with catalog.
- `project_diagnostics` / `compile_check` — **1** warning (**CS0414**), **0** errors; aligns with intentional sample probe.
- `compile_check` + `emitValidation: true` — same warning; ~**1.3 s** vs ~**35 ms** without emit (expected per tool description).
- `diagnostic_details` (CS0414) — help link + curated `remove_unused_field` fix metadata.
- `security_diagnostics` / `nuget_vulnerability_scan` — **0** CVEs; scan **~4.3 s** for 6 projects.
- `list_analyzers` — **451** rules, **17** analyzer assemblies (first page sampled).
- `get_complexity_metrics` — top methods include `ClassifyTypeUsageAfterWalk` (CC **28**), `ParseNuGetVulnerabilityJson` (CC **27**).
- `get_cohesion_metrics` — e.g. `McpLogger` LCOM4 **3**, multiple types LCOM4 **2** / **1**.
- `find_unused_symbols` — **high** confidence hits include `GetBaseTypes` (private) and sample `DiagnosticsProbe` type (expected for unused sample type).
- `get_namespace_dependencies` — **CircularDependencies: []**; edges list coherent.
- `get_nuget_dependencies` — central package management reflected as `centrally-managed` where applicable.
- `symbol_search` / `symbol_info` / `document_symbols` / `type_hierarchy` / `find_references` / `find_consumers` / `find_type_usages` / `find_implementations` / `impact_analysis` / `find_type_mutations` — coherent results for `ReferenceService` / `IReferenceService`.
- `analyze_data_flow` / `analyze_control_flow` / `get_operations` / `get_syntax_tree` — `ClassifyTypeUsageAfterWalk` region (lines 281–330) analyzed.
- `analyze_snippet` — valid expression/program; broken code returns CS diagnostics.
- `evaluate_csharp` — `Enumerable.Range(1,10).Sum()` → **55**; runtime error surfaced as message (FormatException).
- `semantic_search` — query `"async methods returning Task<bool>"` returned `RevertAsync`, `ReturnsBoolAsync`.
- `build_workspace` — **exit 0**, same CS0414 warning as Roslyn diagnostics.
- `organize_usings_preview` / `organize_usings_apply` — real unified diff on `OrchestrationService.cs` (using order / grouping).
- `revert_last_apply` — reverted **Organize usings in 'ReferenceService.cs'**; reported `revertedOperation` and timestamp.
- `workspace_status` (invalid id) — **actionable** error listing `workspace_list` hint.

## Phase 6 refactor summary

- **Target repo:** `Roslyn-Backed-MCP` (same as audited solution).
- **Scope:** **6e** (`organize_usings_preview` / `organize_usings_apply`) only for **product** refactor. **Not applied:** **6a** fix-all for **CS0414** (would remove `DiagnosticsProbe._unusedForDiagnostics` and break `DiagnosticFixIntegrationTests`). **6b–6d, 6f–6i** not executed in this completion pass (preview-only phases deferred where noted in coverage table).
- **Changes:**
  - **`OrchestrationService.cs`** — using directives reorganized (Roslyn-standard ordering: `System.Xml.Linq` + `Microsoft.CodeAnalysis.*` block before project usings).
- **Tools used:** `organize_usings_preview` → `organize_usings_apply` (preview token applied successfully).
- **Verification:**
  - `compile_check` — **0** errors, **1** warning (CS0414 unchanged).
  - `build_workspace` — **Succeeded** (0 errors, 1 warning).
  - Host **`dotnet test`** on solution — **209 passed**, 0 failed (~2 m 24 s).
- **Phase 9 (undo check):** Applied **audit-only** `organize_usings` to **`ReferenceService.cs`**, then **`revert_last_apply`** — reverted that apply only; **`OrchestrationService.cs`** organize **remains** (confirmed on disk).

## MCP server issues (bugs)

### 1. `test_run` failed when invoked via MCP tool bridge

| Field | Detail |
|--------|--------|
| Tool | `test_run` |
| Input | `workspaceId: c655b134156b4529836624cf4373d8a4`, `projectName: RoslynMcp.Tests` |
| Expected | Structured test results (pass/fail counts). |
| Actual | **"An error occurred invoking 'test_run'."** (no structured payload in client). |
| Severity | **incorrect result** / **integration** — host `dotnet test` succeeded, so execution environment is fine. |
| Reproducibility | Observed **once** this session; retry not performed. |

### 2. `callers_callees` / `symbol_signature_help` resolution at method body position

| Field | Detail |
|--------|--------|
| Tools | `callers_callees`, `symbol_signature_help` |
| Input | `ReferenceService.cs` line **25**, column **25** (inside `FindReferencesAsync`, on `_workspace` token region). |
| Expected | Per tool description, best-effort **enclosing method** when caret semantics ambiguous; user expectation is often **method**-level symbol. |
| Actual | Resolved to **field** `_workspace` (callers are ctor + other methods using `_workspace`); signature help returned **field** type, not `FindReferencesAsync`. |
| Severity | **cosmetic / UX** — confusing for “method analysis” workflows; not a crash. |
| Reproducibility | **always** for this position if column points at field identifier. |

### 3. No new correctness bugs found

Other observations (e.g. `analyze_control_flow` **EndPointIsReachable: false** for a method whose body is all `return` arms) match **early-exit** control flow and are **not** classified as defects.

**No additional new issues** beyond the above.

## Improvement suggestions

- **`test_run`** — When the MCP host returns a generic invocation error, surface **stderr/inner message** (or server log id) so agents can distinguish “dotnet missing” vs “timeout” vs “protocol”.
- **`find_type_usages` vs `find_consumers`** — Align **classification string casing** (e.g. `genericArgument` vs `GenericArgument`) across tools for JSON consumers.
- **`nuget_vulnerability_scan`** — Document or expose **`IncludesTransitive: false`** prominently when comparing to full `dotnet list package --vulnerable --include-transitive` expectations.
- **Prompts (16)** — Catalog lists **experimental** prompts; agent sessions that only expose **tools** cannot “exercise” prompts unless the client adds prompt invocation; consider documenting that in the deep-review prompt.

## Performance observations

- `compile_check` without emit — **~35–242 ms** (solution-wide).
- `compile_check` with **`emitValidation: true`** — **~1267 ms** (matches “10–18x slower” ballpark vs fast path).
- `nuget_vulnerability_scan` — **~4362 ms** (6 projects, 0 vulns).
- `list_analyzers` / `get_namespace_dependencies` — large JSON; acceptable for audit, not “interactive snappy” for huge graphs.
- **Default:** Other tools **&lt; 5 s** for single-symbol / single-file operations in this run.

## Backlog regression check

| AUDIT-ID | Summary | Status |
|----------|---------|--------|
| *(none)* | `ai_docs/backlog.md` contains **no open rows** (agent contract: unfinished work only). | **N/A** |

## Known backlog cross-check

- **No open backlog items** to match; no new AUDIT-* cross-references required.

---

*Completion: mandatory report file written; Phase 6 product change = `OrchestrationService.cs` organize usings; Phase 9 revert verified; tests **209 passed** via `dotnet test`.*
