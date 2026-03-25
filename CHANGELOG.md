# Changelog

All notable changes to Roslyn-Backed MCP Server will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [0.9.0-beta.1] - Unreleased

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
