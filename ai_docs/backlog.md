# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->

**updated_at:** 2026-04-13T12:00:00Z

## Agent contract

**Scope:** This file lists **unfinished work only**. It is not a changelog.

| | |
|---|---|
| **MUST** | Remove or update backlog rows when work ships; do it in the **same PR** as the implementation (or an immediate follow-up). |
| **MUST** | End implementation plans with a final todo: `backlog: sync ai_docs/backlog.md` when closing backlog-related items. |
| **MUST** | Use stable, kebab-case `id` values per open row (grep-friendly). |
| **MUST NOT** | Add "Completed" tables, dated history sections, or narrative changelogs here. |
| **MUST NOT** | Leave done items in the open table. |

## Standing rules (ongoing)

These are **not** backlog rows; do not delete them when clearing completed work.

- Reprioritize on each audit pass.
- Bug and tool items: put reproduction and session context in the PR or linked issue when it helps reviewers.
- Broader planning and complexity notes stay in this file; `ai_docs/archive/` holds policy only (see `archive/README.md`).

## Open work

Ordered by severity: P2 (contract violations / real-world blockers) → P3 (refactors, accuracy, UX) → P4 (docs + cosmetic).

| id | pri | blocker | deps | do |
|----|-----|---------|------|-----|
| `undo-clear-unwired` | P2 | — | — | **Resource leak bug:** `IUndoService.Clear(workspaceId)` and `IChangeTracker.Clear(workspaceId)` are never wired to the `WorkspaceClosed` event. `CompilationCache` IS correctly wired via `ServiceCollectionExtensions.cs`. Undo snapshots and change records accumulate without cleanup when workspaces close. **Fix:** Wire both services to `WorkspaceClosed` following the `CompilationCache` pattern. |
| `fix-all-third-party-analyzers` | P2 | — | — | `fix_all_preview` returns empty for third-party analyzer diagnostics (Meziantou MA0076, MA0159, etc.) because no code fix provider is loaded. These rules DO have code fix providers in the analyzer NuGet packages, but Roslyn MCP doesn't load them. This was the single biggest friction point in a real session — ~75 mechanical edits had to be done manually (or via subagent) that should have been two `fix_all_apply` calls. **Repro:** Load any solution with Meziantou.Analyzer, call `fix_all_preview` with `diagnosticId: "MA0076"` or `"MA0159"`, scope `"solution"`. Returns `FixedCount: 0` and guidance about missing providers. **Expected:** Third-party analyzers that ship code fix providers (Meziantou, Roslynator, SonarAnalyzer, etc.) should have their fix providers loaded so `fix_all` works. **Impact:** High — batch-fixing analyzer suggestions is a primary use case for agent-driven code quality work. |
| `fix-all-builtin-ca1859` | P2 | — | `fix-all-third-party-analyzers` | `fix_all_preview` returns `FixedCount: 0` for CA1859 (use concrete return type for improved performance), a built-in .NET Analyzer rule from `Microsoft.CodeAnalysis.NetAnalyzers`. CA1859 has a code fix provider in the analyzer package, but it isn't loaded by fix_all. May share the same root cause as `fix-all-third-party-analyzers`. **Repro:** Call `fix_all_preview` with `diagnosticId: "CA1859"`, `scope: "solution"`. Returns `FixedCount: 0`. |
| `test-fixall-service` | P3 | — | — | `FixAllService` (529 LOC, `FixAllService.cs`) has zero test coverage and applies bulk code modifications across entire solutions. Highest-risk untested service. **Add:** Integration tests for `PreviewFixAllAsync` with document/project/solution scope, diagnostic ID with no provider, and applying a previewed fix-all. |
| `test-client-root-validator` | P3 | — | — | `ClientRootPathValidator` (145 LOC) is the security boundary for path traversal and has zero direct tests. **Add:** Tests for null server, no roots capability, path inside/outside roots, symlink resolution, fail-open vs fail-closed modes. |
| `test-pure-utilities` | P3 | — | — | Five pure utility classes have zero unit tests despite pervasive use: **IdentifierValidation** (54 LOC, guards every rename), **DiffGenerator** (168 LOC, every preview/apply diff), **SymbolHandleSerializer** (138 LOC, every symbol handle round-trip), **ParameterValidation** (46 LOC, guards severity/pagination params), **SymbolLocatorFactory** (72 LOC, every symbol tool). All pure functions — highly testable quick wins. ~50 focused unit tests estimated. |
| `test-symbol-mapper` | P3 | — | — | `SymbolMapper` (251 LOC) maps all symbol kinds to DTOs and feeds every symbol-related tool response. Zero tests. **Add:** Tests for all symbol kinds: method, property, field, class, interface, enum, event. |
| `complexity-diagnostic-service` | P3 | — | — | `DiagnosticService.GetDiagnosticsAsync` (`DiagnosticService.cs:35`) is the worst complexity hotspot: CC=25, MI=29.6, 146 LOC. Handles workspace diags, compiler diags, analyzer diags, severity filtering, caching, count aggregation, and severity hints in one method. **Refactor:** Extract helpers for workspace diagnostic collection, project diagnostic collection, count aggregation, severity-hint computation. |
| `complexity-scripting-service` | P3 | — | — | `ScriptingService.EvaluateAsync` (`ScriptingService.cs:80`) has the lowest maintainability index: CC=20, MI=26.3, 204 LOC. Manages threads, timers, cancellation, heartbeats, and hard deadlines. **Refactor:** Extract thread-management, timer-management, and outcome-translation phases. Also mark `CapacityFailure` and `BuildScriptOptions` as `static` (CA1822). |
| `high-param-count-services` | P3 | — | — | Two service methods have 8 parameters each: `CompileCheckService.CheckAsync` (`CompileCheckService.cs:22`, CC=18) and `CohesionAnalysisService.GetCohesionMetricsAsync` (`CohesionAnalysisService.cs:22`, CC=15). **Refactor:** Introduce options records. |
| `ca1873-logging-perf` | P3 | — | — | ~25 CA1873 instances: expensive string interpolation in logging calls evaluated even when log level is disabled. `WorkspaceManager` already uses `LoggerMessage.Define` (lines 23-46). **Fix:** Extend the pattern to `ToolErrorHandler`, `ScriptingService`, `UndoService`, `CodeActionService`, `FileOperationService`, `FileWatcherService`, `SecurityDiagnosticService`. Candidate for systematic batch fix. |
| `dead-code-cleanup` | P3 | — | — | Two confirmed dead symbols: (1) `MsBuildMetadataHelper.FindDirectoryBuildProps` (`MsBuildMetadataHelper.cs:26`) — zero references; sibling `FindDirectoryPackagesProps` is well-used. (2) `CorrelationContext.BeginScope()` + inner `CorrelationScope` class (`McpLoggingProvider.cs:104,113`) — zero call sites. **Remove** both and compile-check. |
| `add-net-analyzers` | P3 | — | — | `Microsoft.CodeAnalysis.NetAnalyzers` (CA2xxx/CA3xxx/CA5xxx security rules) and `Microsoft.CodeAnalysis.BannedApiAnalyzers` are not installed. SecurityCodeScan.VS2019 is present but archived (v5.6.7). **Add:** Both analyzer packages to `Directory.Build.props`. Consider creating `BannedSymbols.txt` for dangerous APIs (BinaryFormatter, Process.Start with shell execute). |
| `complexity-callers-callees` | P3 | — | — | `SymbolRelationshipService.GetCallersCalleesAsync` (`SymbolRelationshipService.cs:191`, CC=20) has nesting depth 5 — deepest in the codebase. Nested `foreach` over references, locations, then invocations. **Refactor:** Extract callee-resolution loop into a helper method. |
| `diagnostics-filter-by-id` | P3 | — | — | `project_diagnostics` has `severity`, `file`, and `project` filters but no `diagnosticId` filter. When looking for all instances of a specific diagnostic, callers must page through the entire Info-severity diagnostic set and filter client-side. **Suggested API:** Add optional `diagnosticId: string?` parameter that filters server-side before pagination. |
| `test-discover-pagination-ux` | P3 | — | — | `test_discover` on a solution with 884 tests produced 214K chars of JSON, exceeding the MCP token limit. Consider: (1) a `projectName` filter to scope discovery, (2) a summary mode (counts per project), or (3) reducing per-test payload size. |
| `static-test-reference-map` | P3 | — | — | Feature request: add a tool that statically maps source symbols to test references. Current Roslyn MCP has `test_coverage` (runtime coverlet) and `test_related` (heuristic name matching), but no static reference-based mapping. **Suggested API:** `test_reference_map(workspaceId, project?)` → `{ coveredSymbols, uncoveredSymbols, coveragePercent }` based on reference analysis from test projects to source projects. |
| `test-infrastructure-services` | P4 | — | — | Three infrastructure services have zero tests: `SuppressionService` (46 LOC), `SolutionDiffHelper` (118 LOC), `TriviaNormalizationHelper` (98 LOC). Lower priority — thin wrappers or tested indirectly through integration tests. |
| `securitycodescan-currency` | P4 | — | `add-net-analyzers` | SecurityCodeScan.VS2019 (v5.6.7) is archived. Consider migrating to SecurityCodeScan.VS2022 or relying on NetAnalyzers once added. Low urgency — current rules still fire and `TreatWarningsAsErrors` is enabled. |
| `test-run-file-lock-fast-fail` | P4 | — | — | When `test_run` or `test_coverage` hits an MSBuild file lock (MSB3027/MSB3021), MSBuild retries 10 times with 1-second delays. Consider detecting the lock condition earlier and returning the FailureEnvelope sooner. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/workflow.md` | Branch/PR workflow; **Backlog closure** |
| `ai_docs/backlog.md` | This file (open work + contract) |
