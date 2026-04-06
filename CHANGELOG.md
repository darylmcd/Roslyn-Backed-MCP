# Changelog

All notable changes to Roslyn-Backed MCP Server will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added

- **Claude Code plugin**: packaged as a first-class Claude Code plugin installable via `/plugin install`. Includes plugin manifest (`.claude-plugin/plugin.json`), marketplace descriptor (`.claude-plugin/marketplace.json`), and user-configurable environment variables.
- **10 plugin skills**: `/roslyn-mcp:analyze` (solution health), `/roslyn-mcp:refactor` (guided refactoring), `/roslyn-mcp:review` (semantic code review), `/roslyn-mcp:document` (XML doc generation), `/roslyn-mcp:security` (security audit), `/roslyn-mcp:dead-code` (dead code cleanup), `/roslyn-mcp:test-coverage` (coverage analysis), `/roslyn-mcp:migrate-package` (NuGet migration), `/roslyn-mcp:explain-error` (diagnostic fixer), `/roslyn-mcp:complexity` (hotspot analysis).
- **Plugin safety hooks**: PreToolUse guard enforcing preview-before-apply pattern; PostToolUse reminder to compile-check after structural refactorings.
- `ValidationServiceOptions.VulnerabilityScanTimeout` (default 2 minutes) and `ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS` env var to make the NuGet vulnerability scan timeout configurable.

### Fixed

- **Test infrastructure: eliminate MSBuild contention causing intermittent 5-minute timeouts.** Previously every `[ClassInitialize]` called `InitializeServices()` which constructed a new `WorkspaceManager`, and 22 test classes called `WorkspaceManager.LoadAsync(SampleSolutionPath)` independently — spawning 22 separate `MSBuildWorkspace` instances with overlapping file locks on the shared sample fixture. Under load this caused `dotnet build` invocations to hit the service-level cancellation timeout (5 min) and fail. `TestBase` is now idempotent: services are created once per assembly, the workspace manager is disposed in `[AssemblyCleanup]`, and a new `GetOrLoadWorkspaceIdAsync` cache helper makes 22 redundant `LoadAsync` calls collapse into a single MSBuild evaluation. Total test runtime dropped from ~2.3 min to ~2.0 min on a clean run, with the timeout-flake mode eliminated.
- `DependencyAnalysisService.ScanNuGetVulnerabilitiesAsync` no longer hard-codes a 120-second timeout; it now honors `ValidationServiceOptions.VulnerabilityScanTimeout` (still 2 min default). Previously this ignored every `ROSLYNMCP_*_TIMEOUT_SECONDS` env var.

### Changed

- `ValidationServiceOptions` is now a `record` (was `sealed class`), enabling `with` expression updates for cleaner option-binding code in `Program.cs`.
- Test workspace cap raised to 64 (from production default of 8) since the shared `WorkspaceManager` now retains workspaces across the full assembly run instead of per-class disposal cycles.

## [1.6.0] - 2026-04-04

### Added

- Integration tests targeting P1/P2 coverage gaps: `HighValueCoverageIntegrationTests`, `BoundedStoreEvictionTests`, `ServiceCollectionExtensionsTests`.
- Sample-solution profiling smoke record and methodology notes in `docs/large-solution-profiling-baseline.md`.
- Post-1.5 surface audit summary in `ai_docs/audit-reports/2026-04-04-post-1.5-surface-audit.md`.

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
