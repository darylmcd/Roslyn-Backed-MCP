# Changelog

All notable changes to Roslyn-Backed MCP Server will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

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
