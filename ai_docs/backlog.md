# Backlog

This is the only canonical location for unfinished work items.
Prioritized for public NuGet publication readiness.

Last audit: 2026-03-26 (comprehensive pre-publication audit).

---

## P0 — Blocks Public Release

These must be resolved before any public NuGet publish.

All P0 items resolved in audit sprint 2026-03-26. See Completed Archive.

## P1 — High Value, Pre-1.0

Quality, reliability, and usability issues that should be addressed before a stable 1.0 release.

### Configuration

- [ ] **CFG-01**: Wire options classes to configuration — `ValidationServiceOptions` (build/test timeouts), `WorkspaceManagerOptions` (max workspaces), `WorkspaceExecutionGate` timeout are all hardcoded defaults with no way for users to override via env vars or config. (`Program.cs`, options classes)

### Performance

- [ ] **PERF-03**: Reduce `PreviewStore` memory pressure — stores full `Solution` snapshots (immutable but large). 50 entries x large solutions = significant memory. Consider size-aware eviction or lower cap. (`PreviewStore.cs:8-9`)

### Code Quality

- [ ] **CQ-01**: Reduce complexity in hot-path methods — `ParseSemanticQuery` (CC=32, 94 LOC), `SymbolMapper.ToDto` (CC=28, 86 LOC), `ClassifyReferenceLocation` (CC=27, 56 LOC). These are brittle and hard to test. Consider extracting sub-methods or lookup tables. (`CodePatternAnalyzer.cs:174`, `SymbolMapper.cs:21`, `SymbolMapper.cs:133`)
- [ ] **CQ-02**: Audit 50+ unused public symbols — `find_unused_symbols` reports 50+ hits (tools, prompts, resources). Most are likely wired via reflection/attributes, but should be verified. Any truly dead code should be removed.

### API Consistency

- [ ] **API-03**: Standardize default `limit` values — `symbol_search` defaults to 20, `find_unused_symbols`/`semantic_search`/`get_complexity_metrics`/`get_cohesion_metrics` default to 50. Pick one default and apply consistently.

### Documentation

- [ ] **DOC-01**: Add XML doc comments to 15 undocumented service interfaces — `IBuildService`, `ICodeMetricsService`, `ICodePatternAnalyzer`, `IDependencyAnalysisService`, `IMutationAnalysisService`, `IReferenceService`, `ISymbolNavigationService`, `ISymbolRelationshipService`, `ISymbolSearchService`, `ITestDiscoveryService`, `ITestRunnerService`, `IUnusedCodeAnalyzer`, `IWorkspaceManager`, `ICompletionService`, `IEditService`. DTOs are fully documented. (`src/RoslynMcp.Core/Services/`)

## P2 — Important Improvements

Meaningful quality and capability improvements.

### Concurrency

- [ ] **CON-02**: Apply gate per-workspace instead of global `__apply__` — `ApplyGateKey` shares a single semaphore for ALL apply operations across ALL workspaces, serializing unrelated operations. (`WorkspaceExecutionGate.cs:22,37`)

### API Surface

- [ ] **API-02**: Add caveat to `test_related` / `test_related_files` descriptions — these use heuristic name matching, not semantic analysis. AI agents may incorrectly trust results as exhaustive. (`ValidationTools.cs:83-117`)
- [ ] **API-04**: Add truncation/pagination to prompt templates for large workspaces — `analyze_dependencies` embeds full project graph + namespace deps + NuGet deps. (`RoslynPrompts.cs:220-265`)

### Performance

- [ ] **PERF-02**: Cache test discovery results per workspace version — `DiscoverTestsAsync` iterates ALL documents in ALL test projects on every call. (`ValidationService.cs:73-148`)

### File Watching

- [ ] **REL-06**: Expand file watcher to include `.csproj`, `.props`, `.targets` — currently only watches `*.cs` files. Changes to project files won't mark workspace stale. (`FileWatcherService.cs:22`)

### Testing

- [ ] **TEST-01**: Add negative/edge-case tests — malformed symbol handles, null/empty parameters, concurrent tool calls, malformed XML project files.
- [ ] **TEST-02**: Add dedicated tests for untested services — `BulkRefactoringService`, `CohesionAnalysisService`, `ConsumerAnalysisService`, `TypeExtractionService`, `TypeMoveService`. (5 services with no direct test coverage)
- [ ] **TEST-05**: Install `coverlet.collector` and establish baseline coverage metrics — currently no line/branch coverage data available.

### Release

- [ ] **REL-07**: Add CI badge to README.
- [ ] **REL-09**: Document cross-platform limitations.
- [ ] **REL-13**: Install `SecurityCodeScan.VS2019` analyzer — the server's own `security_analyzer_status` tool recommends it but the package is not installed.

## P3 — Nice to Have

Low-priority polish and future considerations.

- [ ] **CODE-01**: Extract shared `JsonSerializerOptions` — duplicated `new() { WriteIndented = true }` in 14+ tool classes.
- [ ] **CODE-02**: Fix triple `GetSummary()` call in `server_info`. (`ServerTools.cs:36-53`)
- [ ] **CODE-03**: Extract `ProjectMetadataReader` from `WorkspaceManager` static helpers. (`WorkspaceManager.cs:356-415`)
- [ ] **CODE-04**: Generalize `PreviewStore`/`CompositePreviewStore` with `BoundedStore<T>` base class — nearly identical TTL/eviction logic.
- [ ] **API-05**: Consider promoting `test_coverage` to stable — functionally complete, uses standard `coverlet.collector`.
- [ ] **API-06**: Consider adding `undo`/`revert_last_apply` capability.
- [ ] **PERF-04**: Make `DotnetCommandRunner.OutputLimit` (12000 chars) configurable for verbose build output.
- [ ] **TEST-03**: Add performance baseline tests for large solutions.
- [ ] **TEST-04**: Clean up temp directories in test infrastructure — add `[AssemblyCleanup]` handler.

---

## Completed (Archive)

Items completed in previous sprints. Kept for audit trail; remove periodically.

- [x] **SEC-01**: Add path validation to EditTools + MultiFileEditTools — Done.
- [x] **SEC-02**: Fix path prefix match in ClientRootPathValidator — Done.
- [x] **REL-01**: Rename `Company.` namespace prefix — Done. Renamed to `RoslynMcp.*`.
- [x] **REL-02**: Add CONTRIBUTING.md — Done.
- [x] **REL-03**: Add CHANGELOG.md — Done.
- [x] **REL-04**: Set explicit version — Done. Set `0.9.0-beta.1` in `Directory.Build.props`.
- [x] **ERR-01**: Add FileNotFoundException handler to ToolErrorHandler — Done.
- [x] **ERR-02**: Wrap resource handlers in error handling — Done.
- [x] **ERR-03**: Wrap prompt handlers in error handling — Done. All 11 methods have try/catch.
- [x] **API-01**: Fix source_file resource for missing docs — Done.
- [x] **PERF-01**: Fix triple .csproj parse in BuildProjectStatuses — Done.
- [x] **REL-08**: Add `SECURITY.md` and `CODE_OF_CONDUCT.md` — Promoted to P0 as REL-10/REL-11.
- [x] **SEC-03**: Document MSBuild loading security model in README — Done. Added Security Considerations section.
- [x] **SEC-04**: Fix symlink bypass in `ClientRootPathValidator` — Done. Added `ResolveFinalTarget()` with symlink/junction resolution.
- [x] **SEC-05**: Fix silent security bypass on roots lookup failure — Done. Changed to fail-closed + `Console.Error` logging.
- [x] **CON-01**: Fix `GetStatus()` race in `WorkspaceManager` — Done. Snapshot session fields under `LoadLock`.
- [x] **ERR-04**: Add logging to silent catch blocks — Done. `ProjectMetadataParser` and `ClientRootPathValidator`.
- [x] **ERR-05**: Capture diagnostics on workspace load failure — Done. Preserve diagnostics before disposing failed session.
- [x] **ERR-06**: Bound `WorkspaceDiagnostics` queue — Done. Added 500-entry cap with dequeue on overflow.
- [x] **REL-10**: Add `SECURITY.md` — Done.
- [x] **REL-11**: Add `CODE_OF_CONDUCT.md` — Done.
- [x] **REL-12**: Add `.github/ISSUE_TEMPLATE/` — Done. Bug report and feature request templates.

## Backlog Rules

- Keep only incomplete items in the priority sections.
- Move completed items to the archive section after verification.
- Do not store session logs, handoff notes, or temporary plans in this file.
- Reprioritize on each audit pass. Last audit: 2026-03-26.
