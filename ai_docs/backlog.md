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

All P1 items resolved in sprint 2026-03-26. See Completed Archive.

## P2 — Important Improvements

Meaningful quality and capability improvements.

### API Surface

- [ ] **API-04**: Add truncation/pagination to prompt templates for large workspaces — `analyze_dependencies` embeds full project graph + namespace deps + NuGet deps. (`RoslynPrompts.cs:220-265`)

### Performance

- [ ] **PERF-02**: Cache test discovery results per workspace version — `DiscoverTestsAsync` iterates ALL documents in ALL test projects on every call. (`ValidationService.cs:73-148`)

### Testing

- [ ] **TEST-01**: Add negative/edge-case tests — malformed symbol handles, null/empty parameters, concurrent tool calls, malformed XML project files.
- [ ] **TEST-02**: Add dedicated tests for untested services — `BulkRefactoringService`, `CohesionAnalysisService`, `ConsumerAnalysisService`, `TypeExtractionService`, `TypeMoveService`. (5 services with no direct test coverage)

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
- [x] **CFG-01**: Wire options classes to env var overrides — Done. `ROSLYNMCP_MAX_WORKSPACES`, `ROSLYNMCP_BUILD_TIMEOUT_SECONDS`, `ROSLYNMCP_TEST_TIMEOUT_SECONDS`, `ROSLYNMCP_PREVIEW_MAX_ENTRIES`.
- [x] **PERF-03**: Reduce PreviewStore memory pressure — Done. Cap lowered from 50 to 20, configurable via env var.
- [x] **CON-02**: Per-workspace apply gate — Done. Replaced global `ApplyGateKey` with `__apply__:{workspaceId}`.
- [x] **REL-06**: Expand file watcher to project files — Done. Watches `*.csproj`, `*.props`, `*.targets`, `*.sln`, `*.slnx`.
- [x] **CQ-01**: Reduce complexity in 3 high-CC methods — Done. `ParseSemanticQuery` 32→~12, `ToDto` 28→~10, `ClassifyReferenceLocation` 27→~8.
- [x] **CQ-02**: Audit unused public symbols — Done. All 50 results are reflection-wired false positives.
- [x] **API-03**: Standardize default limit values — Done. `symbol_search` default changed from 20 to 50.
- [x] **API-02**: Add heuristic caveat to test_related descriptions — Done.
- [x] **DOC-01**: XML doc comments for 12 service interfaces — Done.
- [x] **TEST-05**: Install coverlet and establish baseline coverage — Done. 50.3% line, 34.0% branch.

## Backlog Rules

- Keep only incomplete items in the priority sections.
- Move completed items to the archive section after verification.
- Do not store session logs, handoff notes, or temporary plans in this file.
- Reprioritize on each audit pass. Last audit: 2026-03-26.
