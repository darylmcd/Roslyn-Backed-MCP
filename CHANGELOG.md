# Changelog

All notable changes to Roslyn-Backed MCP Server will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added

- **Selection-range code action tests** — verified which Roslyn refactoring providers work in MSBuildWorkspace with non-zero-length TextSpans. Introduce parameter and inline temporary variable work; extract method and introduce local variable do not (providers require internal IDE services). Updated `get_code_actions` tool description and added "Selection-Range Refactoring" workflow hint.
- **`RefactoringProbe.cs`** sample fixture for selection-range refactoring tests.
- **`extract_method_preview` / `extract_method_apply`** — custom extract-method refactoring tool pair using Roslyn's `DataFlowAnalysis` for parameter inference and `ControlFlowAnalysis` for single-exit validation. Supports void and single-return-value extraction with automatic call-site generation.
- **`workspace_changes`** — read-only tool listing all mutations applied to a workspace during the session. Returns ordered entries with descriptions, affected files, tool names, and timestamps. Integrated into `RefactoringService` and `EditService` apply paths. 126 tools total (66 stable / 60 experimental).

## [1.9.0] - 2026-04-11

### Added

- **Stable promotions (4 tools):** `semantic_search`, `analyze_data_flow`, `analyze_control_flow`, `evaluate_csharp` promoted from experimental to stable. Catalog `2026.04` now ships 66 stable / 57 experimental tools (123 total).
  - `semantic_search` — verbose-query fallback (#110) and exact-match implementing predicate (#123) shipped; backlog cleared.
  - `analyze_data_flow` / `analyze_control_flow` — expression-bodied member support added; read-only, well-tested.
  - `evaluate_csharp` — timeout budget, infinite-loop safety, abandoned-cap, and outer-cancellation all exercised in integration tests.

### Fixed

- **`apply_text_edit` line-break preservation** (`dr-apply-text-edit-line-break-corruption`). When an edit span ends at column 1 of a line (swallowing the line break), and the replacement text does not end with a newline, the original line ending is now appended to prevent line collapse at method/declaration boundaries. (#121)
- **`diagnostic_details` curated fix contract** (`dr-code-fix-preview-vs-diagnostic-details-curated-gap`). Removed false curated fix promises for CS0414, CS8600/8602/8603, CA2234, CA1852 from `GetSupportedFixes` — `code_fix_preview` only supports CS8019/IDE0005. Added `GuidanceMessage` field to `DiagnosticDetailsDto` directing users to `get_code_actions` + `preview_code_action`. (#122)
- **`semantic_search` implementing predicate accuracy** (`dr-semantic-search-idisposable-predicate-accuracy`). Replaced `Contains()` with `Equals()` on `ToDisplayString()` in `AddImplementingPredicate` to prevent false positives where "IDisposable" matched "IAsyncDisposable" via substring. (#123)
- **`find_shared_members` partial-class crash** (`dr-find-shared-members-locator-invalidargument`). Built a semantic model map for all partial declarations so `MethodAccessesMember` uses the correct model for each method's syntax tree, preventing "Syntax node is not within syntax tree" exceptions. (#124)
- **`SecurityDiagnosticIntegrationTests` fixture** (`dr-security-integration-insecurelib-not-in-sln`). Added `EnableNETAnalyzers=true` and `AnalysisLevel=latest-all` to InsecureLib.csproj so CA5350/CA5351 fire in MSBuildWorkspace. Fixes 4 previously failing tests. (#125)
- **`project_diagnostics` empty first page hint** (`dr-project-diagnostics-info-only-empty-first-page`). Added `SeverityHint` field when returned arrays are empty but lower-severity diagnostics exist. (#126)
- **`get_editorconfig_options` completeness** (`dr-get-editorconfig-options-incomplete-after-set`). Supplemented Roslyn-enumerated keys with on-disk keys from `.editorconfig` after `set_editorconfig_option` writes. (#126)
- **`get_code_actions` parameter validation** (`dr-get-code-actions-opaque-error-on-bad-contract`). Validates `startLine`/`startColumn` >= 1 with a helpful error when callers pass wrong parameter names. (#126)

### Changed

- **`project_diagnostics` tool description** updated with response JSON field names (`totalErrors`, `totalWarnings`, `totalInfo`, `compilerErrors`, `analyzerErrors`, `workspaceErrors`, `severityHint`) and default severity documentation. (#126)
- **`find_references_bulk` tool description** now includes a concrete JSON parameter shape example to prevent first-invocation failures. (#127)

## [1.8.2] - 2026-04-09

### Fixed

- **Structured `test_run` failure envelope** (`test-run-failure-envelope`). When `dotnet test` exits without TRX output (MSB3027/3021 file locks, build failures, timeouts), the result now carries a `TestRunFailureEnvelopeDto` with `ErrorKind` (FileLock/BuildFailure/Timeout/Unknown), `IsRetryable`, `Summary`, and `StdOutTail`/`StdErrTail` instead of throwing a bare invocation error.
- **`apply_text_edit` range validation** (`apply-text-edit-invalid-edit-corrupt-diff`). Malformed `TextEditDto` values (null `NewText`, non-positive line/column, out-of-bounds, reversed ranges) are now rejected with a structured `ArgumentException` before any disk write or diff generation.
- **`revert_last_apply` disk consistency** (`revert-last-apply-disk-consistency`). `IUndoService` now accepts explicit `FileSnapshotDto` pairs; `RevertAsync` restores disk directly from these authoritative snapshots instead of relying on the fragile `Solution.GetChanges` path. Legacy solution-based path gains a disk-walk safety net.
- **`set_editorconfig_option` undo retrofit** (`set-editorconfig-option-not-undoable`). Now participates in the undo stack via the file-snapshot path; `revert_last_apply` restores pre-write content or deletes the file if the set call created it. Tool description updated.
- **`semantic_search` verbose-query fallback** (`semantic-search-zero-results-verbose-query`). Long natural-language queries that fail structured parsing decompose into stopword-filtered tokens; the token-OR fallback matches symbols whose names contain any token. New `SemanticSearchDebugDto` on the response shows parsed tokens, applied predicates, and fallback strategy.
- **`workspace_load` idempotent by path** (`workspace-session-deduplication`). Repeat loads of the same solution path return the existing `WorkspaceId` instead of creating a duplicate. Includes a race-window check for concurrent callers.
- **`find_overrides` auto-promotes to virtual root** (`find-overrides-virtual-declaration-site-doc`). Invoking at an override site now walks back to the original virtual/interface declaration before the search, producing the same complete result set as invoking at the virtual declaration.
- **`find_type_usages` cref classification** (`find-type-usages-cref-classification`). New `TypeUsageClassification.Documentation` value; `<see cref="X"/>` doc-comment references are now classified as `Documentation` instead of `Other`.
- **`find_references_bulk` schema error UX** (`find-references-bulk-schema-error-ux`). Invalid `BulkSymbolLocator` shapes now include an inline JSON example in the error message.
- **MSBuild tools bad-argument message** (`msbuild-tools-bad-argument-message`). `ResolveRoslynProject` now lists loaded project names in the error when the caller passes an unknown project.
- **`move_file_preview` truncation marker** (`move-file-preview-large-diff-truncation`). The FLAG-6A truncation marker now includes the paths of omitted files.
- **`analyze_snippet` CS0029 span** (`analyze-snippet-cs0029-literal-span`). Regression test confirms the diagnostic span covers the string literal in user coordinates.

### Changed

- Tool description clarifications for `compile_check`, `project_diagnostics`, `workspace_load`, `evaluate_msbuild_items`, `evaluate_msbuild_property`, `add_package_reference_preview`, `semantic_search`, `symbol_search`, `server_info`, `set_editorconfig_option`, `find_overrides`, `test_run`.
- `eng/verify-release.ps1` now passes `--logger "console;verbosity=normal"` so failing test names surface through wrapper scripts.
- `eng/verify-version-drift.ps1` — new automated version-string drift check across all 5 version files; wired into `verify-release.ps1`.
- Deep-review audit intake: 3 raw audit reports, 1 rollup, updated procedures and eng scripts.

## [1.8.1] - 2026-04-08

### Changed

- **Package release under the `Darylmcd.RoslynMcp` NuGet id.** Version bump from `1.8.0` to `1.8.1` so the first published artifact carries the renamed id from a clean version slot. No source/feature changes vs `1.8.0` — the bump exists purely to give the renamed package a fresh version slot.
- **Package consumer README.** The NuGet package now ships a focused `src/RoslynMcp.Host.Stdio/README.md` aimed at consumers (install + use + MCP client config + security/privacy + configuration env vars + cross-platform notes). The previous behavior was to pack the repo root `README.md`, which is a much longer contributor-facing document. The repo root README stays unchanged — only the `<PackageReadmeFile>` source path changed.

## [1.8.0] - 2026-04-08

### Changed

- **Catalog `2026.04` — promote six read-only advanced-analysis tools to stable** (`find_unused_symbols`, `get_di_registrations`, `get_complexity_metrics`, `find_reflection_usages`, `get_namespace_dependencies`, `get_nuget_dependencies`). Evidence: 2026-04-08 multi-repo deep-review raw audits and rollup in `ai_docs/reports/`. `semantic_search` remains **experimental** (open ranking/UX backlog items). Updated `docs/product-contract.md` and `docs/experimental-promotion-analysis.md` accordingly.
- **NuGet package id renamed `RoslynMcp` → `Darylmcd.RoslynMcp`.** The unprefixed `RoslynMcp` id was published by another author (`chrismo80`, v1.1.1) on 2026-04-08 shortly before our first publish attempt, so it is permanently unavailable. The CLI command name remains `roslynmcp` — `.mcp.json`, plugin manifests, and any user config that references the binary on `PATH` are unaffected. Install command becomes `dotnet tool install -g Darylmcd.RoslynMcp`. Existing local installs of the bare `RoslynMcp` package should be uninstalled (`dotnet tool uninstall -g RoslynMcp`) before installing the new id; the local-dev `PackAndReinstallGlobalTool` MSBuild target now uninstalls both ids before reinstalling.

## [1.7.0] - 2026-04-08

### Fixed

- **P1 / `rename_preview` accepts illegal C# identifiers** (`rename-preview-validate-identifier`). Calling `rename_preview newName="2foo"` (numeric prefix), `"class"` (reserved keyword), `"var"` (contextual keyword), or `""` (empty) previously produced a multi-file preview that, if applied, would break compilation across the entire solution. `PreviewRenameAsync` now calls a new `IdentifierValidation.ThrowIfInvalidIdentifier` helper that validates the new name against `SyntaxFacts.IsValidIdentifier`, rejects reserved and contextual C# keywords, and accepts verbatim identifiers (`@class`) by stripping the `@` and skipping the keyword guards. Throws `InvalidOperationException` with a descriptive message before any cross-file changes are computed.
- **`rename_preview` no-op silently returns empty Changes** (`rename-preview-noop-warning`). Renaming a symbol to its current name now returns `Warnings: ["New name '...' matches the existing name; no changes were produced."]` so callers can distinguish "rename succeeded with no actual references" from "no-op rename". Comparison is case-sensitive (`Ordinal`) so `Foo → foo` is still treated as a real rename.
- **`organize_usings_preview` and `move_type_to_file_preview` leave stray blank lines** (`organize-usings-and-move-type-trivia`). Both tools shared the same trivia bug: `organize_usings_preview` used `SyntaxRemoveOptions.KeepExteriorTrivia` which preserved the trailing newline of each removed `using`, leaving a blank line behind; `move_type_to_file_preview` used `NormalizeWhitespace()` which inserted artificial blank lines before the namespace. Both paths now route through a new shared `TriviaNormalizationHelper` (`NormalizeLeadingTrivia`, `NormalizeUsingToMemberSeparator`, `CollapseBlankLinesInUsingBlock`) that canonicalizes leading/trailing trivia after deletes and moves while preserving XML doc comments and other non-whitespace trivia. Same fix also applied to `TypeMoveService.RemoveUnusedUsingsAsync`.

### Added

- **`apply_text_edit` and `apply_multi_file_edit` are now revertible** (`apply-text-edit-undo-stack`). Both tools were previously documented as "DISK-DIRECT, NOT REVERTIBLE" — they did not enter the undo stack so `revert_last_apply` could not undo them. `EditService` now takes an optional `IUndoService` and captures a single pre-apply `Solution` snapshot before mutating. A new `IEditService.ApplyMultiFileTextEditsAsync` method captures **one** snapshot at the batch boundary so a multi-file batch is reverted atomically. Tool descriptions for `apply_text_edit`, `apply_multi_file_edit`, and `revert_last_apply` updated to reflect the new contract; only file create/delete/move and project file mutations remain unrevertible.
- **`find_unused_symbols` is now convention-aware by default** (`find-unused-symbols-convention-aware`). New `excludeConventionInvoked` parameter (default `true`) skips symbols recognized as convention-invoked: EF Core `*ModelSnapshot`, xUnit/NUnit/MSTest fixtures, ASP.NET middleware (`Invoke`/`InvokeAsync(HttpContext)` shape), SignalR `Hub` subclasses, FluentValidation `AbstractValidator<T>` subclasses, and Razor `PageModel` subclasses. Detection is name-shape based via two new private helpers `IsConventionInvokedType` (walks the base-type chain by simple name plus method-shape detection) and `IsConventionInvokedMember` (delegates to type detection plus `[DbContext]`/`[Migration]` attribute checks). Set `excludeConventionInvoked=false` to opt out and see raw counts. No NuGet dependencies pulled — detection works against stub base types in user fixtures.
- New shared helper `src/RoslynMcp.Roslyn/Helpers/IdentifierValidation.cs` for the rename validation path.
- New shared helper `src/RoslynMcp.Roslyn/Helpers/TriviaNormalizationHelper.cs` for the trivia/blank-line fixes.
- New sample fixture `samples/SampleSolution/SampleLib/ConventionFixtures.cs` with stub base types and 5 convention-shaped sample classes (`SampleValidator`, `ChatHub`, `IndexModel`, `MyDbContextModelSnapshot`, `LoggingMiddleware`) plus a control type that should still be reported.
- New test class `tests/RoslynMcp.Tests/EditUndoIntegrationTests.cs` with 4 tests covering the text-edit undo path including single-slot snapshot semantics across rename → text edit → revert.
- 6 new rename-validation tests in `RefactoringToolsIntegrationTests.cs`, 3 new convention-aware tests in `DeadCodeIntegrationTests.cs`, 3 new trivia tests across `RefactoringToolsIntegrationTests.cs` and `TypeMoveTests.cs`. Total new test count: 16. Suite is now 267/267 (was 251/251).

### Refactored

- **`ScriptingService.EvaluateAsync` complexity reduction.** Cyclomatic complexity 36 → 20, LOC 282 → 204, MaintainabilityIndex 19.5 → 26.3. Extracted `MarkAbandonedIfWorkerStillRunning`, `LogHardDeadlineCritical`, `BuildHardDeadlineDto`, and `BuildOutcomeDto` so the FLAG-5C concurrency race steps (cancel → deadline → outcome) are no longer buried under DTO boilerplate. Behavior preserved — verified by all 8 ScriptingService characterization tests including the abandoned-cap and outer-cancel paths.
- **`MutationAnalysisService.ClassifyTypeUsageAfterWalk` complexity reduction.** Cyclomatic complexity 31 → 21, LOC 58 → 31, MaintainabilityIndex 40.1 → 49.7. Replaced an if-chain with a switch expression using type patterns and `when` guards.
- **`CodeMetricsService.VisitControlFlowNesting` complexity reduction.** Original 109-LOC method (cyclomatic 28) decomposed into a small `VisitChildForNesting` dispatcher plus per-shape helpers `VisitForLoop` and `VisitTryStatement`. Single-body statement cases grouped under a comment band so multi-body cases stay visually distinct.
- **`UnusedCodeAnalyzer.ShouldSkipSymbolForUnusedAnalysis` complexity reduction.** Cyclomatic complexity 24 → no longer in the hotspot list. Decomposed into 11 named predicate helpers (`IsTestFixtureFiltered`, `IsPublicFiltered`, `IsEnumMemberFiltered`, etc.) aggregated via `||`, making it trivial to add new convention exclusions in this release.

## [1.6.1] - 2026-04-07

### Added

- **Claude Code plugin**: packaged as a first-class Claude Code plugin installable via `/plugin install`. Includes plugin manifest (`.claude-plugin/plugin.json`), marketplace descriptor (`.claude-plugin/marketplace.json`), and user-configurable environment variables.
- **10 plugin skills**: `/roslyn-mcp:analyze` (solution health), `/roslyn-mcp:refactor` (guided refactoring), `/roslyn-mcp:review` (semantic code review), `/roslyn-mcp:document` (XML doc generation), `/roslyn-mcp:security` (security audit), `/roslyn-mcp:dead-code` (dead code cleanup), `/roslyn-mcp:test-coverage` (coverage analysis), `/roslyn-mcp:migrate-package` (NuGet migration), `/roslyn-mcp:explain-error` (diagnostic fixer), `/roslyn-mcp:complexity` (hotspot analysis).
- **Plugin safety hooks**: PreToolUse guard enforcing preview-before-apply pattern; PostToolUse reminder to compile-check after structural refactorings.
- `ValidationServiceOptions.VulnerabilityScanTimeout` (default 2 minutes) and `ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS` env var to make the NuGet vulnerability scan timeout configurable.
- **Cross-tool compilation cache** (`ICompilationCache`): per-workspace, version-keyed cache that shares warm `Compilation` instances across diagnostics, unused-code analysis, and dependency analysis — eliminating redundant Roslyn compilation passes.
- `eng/update-claude-plugin.ps1` utility to refresh the locally cached Claude Code plugin from the marketplace repository without opening the REPL.
- `REINSTALL.md` consolidated installation and reinstall reference.
- **Per-workspace reader/writer lock**: `IWorkspaceExecutionGate` exposes `RunReadAsync<T>` (concurrent reads against the same workspace), `RunWriteAsync<T>` (writes exclusive against all reads/writes on the same workspace), and `RunLoadGateAsync<T>` (the global load gate used by `workspace_load`/`reload`/`close`). Each workspace is gated by a `Nito.AsyncEx.AsyncReaderWriterLock` so reader fan-out is bounded only by the global throttle. There is no opt-out — this is the only lock model the gate ships.
- `Nito.AsyncEx 5.1.2` dependency on `RoslynMcp.Roslyn` (transitive into the host).
- `tests/RoslynMcp.Tests/WorkspaceExecutionGateTests.cs` — dedicated unit-test class for the gate covering read/read concurrency, read/write exclusion, write/write serialization, FIFO writer fairness, post-close `RunReadAsync` rejection, the workspace-close double-acquire race, and rate-limit / request-timeout / global-throttle enforcement.
- `tests/RoslynMcp.Tests/Benchmarks/WorkspaceReadConcurrencyBenchmark.cs` — `[TestCategory("Benchmark")]` benchmark that 4 parallel reads against the same workspace complete in roughly 1× single-read wall time on the per-workspace RW lock.

### Performance

- **Parallelized hot paths:** `ReferenceService`, `UnusedCodeAnalyzer`, `DiagnosticService`, `MutationAnalysisService`, and `ConsumerAnalysisService` now fan out per-location and per-project work concurrently, bounded by processor-count-derived semaphores.
- **Per-workspace diagnostic cache** in `DiagnosticService` avoids redundant recompilation for consecutive diagnostic queries against the same workspace version.
- **Compilation cache lifecycle:** cache entries are eagerly invalidated when a workspace is closed via the new `IWorkspaceManager.WorkspaceClosed` event, preventing stale entry accumulation.
- **Shared reference materializer:** `ReferenceLocationMaterializer` extracts the parallel preview-text + containing-symbol resolution pattern into a reusable helper consumed by reference, mutation, and consumer analysis services.
- `CancellationToken` propagation to `SymbolSearchService.CollectSymbols` / `CollectMembers` for cancellable document-symbol walks on large files.
- **Per-workspace concurrent reads**: parallel `find_references` / `project_diagnostics` / `symbol_search` calls against the same workspace now run truly in parallel, bounded only by the global throttle. The benchmark `WorkspaceReadConcurrencyBenchmark` covers the 4-parallel-read shape.

### Fixed

- **Test infrastructure: eliminate MSBuild contention causing intermittent 5-minute timeouts.** Previously every `[ClassInitialize]` called `InitializeServices()` which constructed a new `WorkspaceManager`, and 22 test classes called `WorkspaceManager.LoadAsync(SampleSolutionPath)` independently — spawning 22 separate `MSBuildWorkspace` instances with overlapping file locks on the shared sample fixture. Under load this caused `dotnet build` invocations to hit the service-level cancellation timeout (5 min) and fail. `TestBase` is now idempotent: services are created once per assembly, the workspace manager is disposed in `[AssemblyCleanup]`, and a new `GetOrLoadWorkspaceIdAsync` cache helper makes 22 redundant `LoadAsync` calls collapse into a single MSBuild evaluation. Total test runtime dropped from ~2.3 min to ~2.0 min on a clean run, with the timeout-flake mode eliminated.
- `DependencyAnalysisService.ScanNuGetVulnerabilitiesAsync` no longer hard-codes a 120-second timeout; it now honors `ValidationServiceOptions.VulnerabilityScanTimeout` (still 2 min default). Previously this ignored every `ROSLYNMCP_*_TIMEOUT_SECONDS` env var.
- Symbol handle deserialization rejects malformed base64/JSON input with `ArgumentException` instead of propagating opaque errors.
- `project_diagnostics` tool returns structured compiler/analyzer/workspace error counts with proper camelCase JSON serialization.
- Code metrics: control-flow nesting depth now accounts for braceless bodies.
- `fix_all` tool includes `guidanceMessage` in preview results.
- Type extraction, scaffolding, and project XML formatting correctness improvements.
- Code action provider loading reliability.
- Central Package Management (CPM) version resolution in dependency analysis.
- `test_run` tool generates unique TRX paths per project and reports stderr on failure.
- **Six mis-classified writers correctly take a write lock.** `apply_text_edit`, `apply_multi_file_edit`, `revert_last_apply`, `set_editorconfig_option`, `set_diagnostic_severity`, and `add_pragma_suppression` previously used the bare-`workspaceId` (read) gate while internally calling `WorkspaceManager.TryApplyChanges` or doing disk-direct file mutations. All six now call `RunWriteAsync` explicitly so writes are exclusive against in-flight reads on the same workspace.
- **`workspace_close` TOCTOU race closed.** Added a post-acquire `EnsureWorkspaceStillExists` recheck inside the per-workspace lock so a reader that passed the initial existence check while a concurrent close was queued cannot operate against a workspace that was removed while the reader was waiting for its read lock. The recheck throws `KeyNotFoundException` instead.
- **Nested `LoadGate → RunWriteAsync` deadlock avoided.** `workspace_reload` and `workspace_close` now nest a per-workspace write-lock acquire inside the global load gate so in-flight readers complete before the solution is replaced or removed. To prevent the nested call from consuming two `_globalThrottle` slots (which can deadlock under saturation when `MaxGlobalConcurrency` is small), `WorkspaceExecutionGate` tracks throttle ownership via an `AsyncLocal<int>` re-entrance counter and skips the inner physical wait.

### Changed

- `ValidationServiceOptions` is now a `record` (was `sealed class`), enabling `with` expression updates for cleaner option-binding code in `Program.cs`.
- Test workspace cap raised to 64 (from production default of 8) since the shared `WorkspaceManager` now retains workspaces across the full assembly run instead of per-class disposal cycles.
- Tool descriptions refined for `server_info`, security diagnostics, analyzer listing, undo, and edit tools.
- **`RefactoringTools.ApplyGateKeyFor` helper deleted.** All 12 tool files (~19 sites) that previously composed the synthetic `__apply__:<wsId>` gate key now call `RunWriteAsync(wsId, …)` directly with an explicit `KeyNotFoundException` for stale preview tokens. The bare-`workspaceId` reader sites in 28 tool files (~91 calls) now call `RunReadAsync(workspaceId, …)`. `workspace_load`, `workspace_reload`, and `workspace_close` (`WorkspaceTools`) call `RunLoadGateAsync(…)` for the global lifecycle gate; reload and close additionally nest an inner `RunWriteAsync(workspaceId, …)` so in-flight readers complete before the workspace state is replaced or removed.

### Removed

- **`ROSLYNMCP_WORKSPACE_RW_LOCK` environment variable, `ExecutionGateOptions.UseReaderWriterLock` property, and the legacy per-workspace `SemaphoreSlim` branch in `WorkspaceExecutionGate`.** The per-workspace `AsyncReaderWriterLock` is now the only lock model. The `eng/flip-rw-lock.ps1`, `eng/rw-lock-on.ps1`, `eng/rw-lock-off.ps1`, and `eng/rw-lock-common.ps1` PowerShell helpers used to flip the env var have been deleted along with `ai_docs/reports/2026-04-06-workspace-rw-lock-design-note.md`.
- **`IWorkspaceExecutionGate.RunAsync<T>(string gateKey, …)` compat shim and `LoadGateKey` constant.** Replaced by the explicit `RunLoadGateAsync<T>` method on the interface; production callers (`WorkspaceTools`) and tests have been migrated.

## [1.6.0] - 2026-04-04

### Added

- Integration tests targeting P1/P2 coverage gaps: `HighValueCoverageIntegrationTests`, `BoundedStoreEvictionTests`, `ServiceCollectionExtensionsTests`.
- Sample-solution profiling smoke record and methodology notes in `docs/large-solution-profiling-baseline.md`.
- Post-1.5 surface checks aligned catalog, tier promotions, and integration coverage with CI (`verify-release.ps1`); evidence for later releases uses timestamped files under `ai_docs/audit-reports/`.

### Changed

- **Stable surface:** promoted six read-only analysis/validation tools from experimental to stable — `compile_check`, `list_analyzers`, `find_consumers`, `get_cohesion_metrics`, `find_shared_members`, `analyze_snippet` (`ServerSurfaceCatalog`, `docs/product-contract.md`, `docs/experimental-promotion-analysis.md`).

## [1.5.0] - 2026-04-04

### Added

- P4 MCP surface: NuGet vulnerability scan, `.editorconfig` write, MSBuild evaluation tools, suppression tools, cohesion `excludeTestProjects`, maintainability index, and related refactors; MCP manifest icon and integration tests.
- CodeQL workflow (PR path filter, query suites) with CI policy note.
- Human-facing `docs/setup.md` (build, test, global tool, Docker, CI artifacts); standardized AI doc prompts and indexes; backlog agent-contract and hygiene workflow.
- `docs/parity-gap-implementation-plan.md`; environment bindings for source-gen docs cap, related-test scan cap, and preview TTL (see `ai_docs/runtime.md`).

### Changed

- `WorkspaceManager` / `WorkspaceExecutionGate`: concurrent session limits, workspace validation, apply gate keys, bounded gates; `PreviewStore` and `IWorkspaceManager` extensions; DI registration updates.

### Fixed

- Parity-gap hardening across Roslyn services (diagnostics, fix-all, mutations, flow/control/syntax, unused confidence, TRX aggregation, workspace generated docs, and more from P1–P3 backlog audits).
- Host tools and DTOs (semantic search warning, JSON enums, resource name keys, server/script/syntax surfaces, prompts).

## [1.4.0] - 2026-04-03

### Added

- Security diagnostic surface: `SecurityTestProject` with `InsecureLib`, `SecurityDiagnosticIntegrationTests`, and `SecurityCodeScan` coverage for `get_security_diagnostics` (FEAT-01).
- Workspace-specific resources now discoverable via MCP resource listing; `ServerResources` extended with workspace-scoped entries (AUDIT-14 partial).
- `WorkspaceStatusDto` extended with additional workspace metadata fields.

### Fixed

- `CohesionMetricsDto` contract field alignment (`CohesionAnalysisService`, `ICohesionAnalysisService`).
- `UnusedCodeAnalyzer` improvements: additional symbol categories and reliability fixes.
- `InterfaceExtractionService` edge-case correctness fixes.
- `CodeActionService` action-application reliability.
- `ProjectMutationService` correctness and edge-case handling.
- `WorkspaceManager` concurrency correctness fixes.
- `ServerSurfaceCatalog` surface count updated.

### Changed

- Repository line-ending policy switched to LF; `.gitattributes` enforces LF for all common text assets.

## [1.3.0] - 2026-04-01

### Added

- `GatedCommandExecutor` / `IGatedCommandExecutor` abstraction: shared CLI execution logic extracted from `BuildService` and `TestRunnerService`, eliminating ~200 lines of duplication.
- Offset/limit pagination for `project_diagnostics` and `list_analyzers` tools.
- Evaluated target-framework resolution via `ProjectMetadataParser`: reads MSBuild-evaluated `TargetFramework(s)` instead of raw XML, fixing inherited values from `Directory.Build.props`.
- 11 new integration tests (`BacklogFixTests.cs`); total test count 154.

### Fixed

- `WorkspaceManager` concurrency: apply-gate and version tracking hardened.
- `TestDiscoveryService` reliability for large solutions.
- `FixAllService` exception handling and partial-success reporting.
- `TypeMoveService` correctness for types with nested members.
- `ToolErrorHandler` structured error response improvements.
- `RefactoringService` edge cases for partial classes.
- `FlowAnalysisService` data-flow accuracy improvements.
- `MutationAnalysisService` stale-result detection fixes.

## [1.2.0] - 2026-03-29

### Added

- Offset/limit pagination and filter parameters for `find_references`, `symbol_search`, and `list_members` (AUDIT-24, AUDIT-25, AUDIT-36).

### Fixed

- `compile_check` tool description clarified to reflect actual behaviour (AUDIT-31, AUDIT-26).

## [1.1.0] - 2026-03-27

### Fixed

- `PathFilter` path-validation hardening; symlink/junction resolution correctness (AUDIT-01).
- `CrossProjectRefactoringService` null-safety and project-graph edge cases (AUDIT-22).
- `InterfaceExtractionService` member-extraction edge cases (AUDIT-28).
- `ScaffoldingService` null-safety and generated-code correctness (AUDIT-29).
- `SymbolResolver` resolution reliability for overloaded members (AUDIT-05, AUDIT-19).
- `SymbolServiceHelpers` deduplication and ranking fixes (AUDIT-20, AUDIT-30).
- `TestRunnerService` output-capture and exit-code handling (AUDIT-02).
- `UnusedCodeAnalyzer` false-positive reduction (initial pass).
- `DotnetOutputParser` edge cases for multi-target builds (AUDIT-03, AUDIT-04).
- `ProjectMutationService` property allowlist enforcement tightened (AUDIT-09).
- `SymbolNavigationService` location mapping for generated files (AUDIT-13).
- `TypeMoveService` namespace-update correctness.

### Changed

- Removed dead `CreateFixtureCopy` test fixture helper; added `.editorconfig` to repo root (CODE-05, CODE-06).

## [1.0.0] - 2026-03-26

First stable release.

### Added

- 48 stable tools: workspace management, symbol navigation, references, diagnostics, build/test, refactoring (preview/apply), code actions, cohesion analysis, consumer analysis, bulk refactoring, type extraction, type move, interface extraction, cross-project refactoring, orchestration.
- 55 experimental tools: advanced analysis, direct edits, file operations, project mutations, scaffolding, dead-code removal, syntax inspection, coverage collection.
- 6 stable resources: server catalog, workspace list, workspace status, project graph, diagnostics, source files.
- 16 experimental prompts: error explanation, refactoring suggestions, code review, dependency analysis, test debugging, guided workflows, security review, dead-code audit, complexity review, cohesion analysis, consumer impact, capability discovery, test coverage review.
- Environment variable configuration: `ROSLYNMCP_MAX_WORKSPACES`, `ROSLYNMCP_BUILD_TIMEOUT_SECONDS`, `ROSLYNMCP_TEST_TIMEOUT_SECONDS`, `ROSLYNMCP_PREVIEW_MAX_ENTRIES`.
- Test discovery caching per workspace version for faster repeated lookups.
- Prompt truncation for large workspaces to prevent context overflow.
- XML doc comments on all 15 core service interfaces.
- SecurityCodeScan.VS2019 static analysis integrated globally.
- Code coverage baseline via coverlet.collector (50.3% line, 34.0% branch).
- 121 integration and behavior tests.

### Changed

- Preview store default cap lowered from 50 to 20 entries (configurable via env var).
- `symbol_search` default limit standardized to 50 (was 20).
- Apply gate scoped per-workspace instead of global semaphore.
- File watcher expanded to include `*.csproj`, `*.props`, `*.targets`, `*.sln`, `*.slnx`.
- Reduced cyclomatic complexity in `ParseSemanticQuery`, `SymbolMapper.ToDto`, and `ClassifyReferenceLocation`.
- `test_related` and `test_related_files` descriptions note heuristic matching.

### Security

- Client root path validation with symlink/junction resolution.
- Property allowlist for project mutations.
- Bounded output capture for CLI commands.
- Per-request timeout enforcement (2 minutes default).
- Per-workspace concurrency gating for all apply operations.
- SecurityCodeScan.VS2019 analyzer with zero findings.

## [0.9.0-beta.1] - 2026-03-26

### Added

- Initial public beta release.
- 40 stable tools: workspace management, symbol navigation, diagnostics, build/test, refactoring (preview/apply).
- 50 experimental tools: advanced analysis, direct edits, file operations, project mutations, scaffolding, dead-code removal, cross-project refactoring, orchestration, syntax inspection, code actions, coverage.
- 6 stable resources: server catalog, workspace status, project graph, diagnostics, source files.
- 9 experimental prompts: error explanation, refactoring suggestions, code review, dependency analysis, test debugging, guided workflows.
- Transport-agnostic core with DTOs at boundaries (no raw Roslyn types leak).
- Session-aware workspaces via `workspaceId` with concurrent workspace support.
- Preview/apply with version gating for all mutations.
- Per-workspace and global concurrency throttling.
- File watcher for stale-state detection.
- stderr-only logging with MCP client notification forwarding.
- Graceful shutdown with workspace disposal.
- CI pipeline with release verification and vulnerability scanning.

### Security

- Client root path validation for file operations and text edits.
- Property allowlist for project mutations.
- Bounded output capture for CLI commands.
- Per-request timeout enforcement (2 minutes default).
