# Backlog

Canonical location for unfinished work items. Prioritized for 1.0 release.

Last audit: 2026-03-26.

---

## P1 — Pre-1.0

- [ ] **PERF-02**: Cache test discovery results per workspace version — `DiscoverTestsAsync` iterates all documents in all test projects on every call. (`TestDiscoveryService.cs`)
- [ ] **API-04**: Add truncation/pagination to prompt templates for large workspaces — `analyze_dependencies` embeds full project graph + namespace deps + NuGet deps. (`RoslynPrompts.cs:220-265`)
- [ ] **REL-13**: Install `SecurityCodeScan.VS2019` analyzer — the server's own `security_analyzer_status` tool recommends it but the package is not installed.
- [ ] **TEST-01**: Add negative/edge-case tests — malformed symbol handles, null/empty parameters, concurrent tool calls, malformed XML project files.
- [ ] **TEST-02**: Add dedicated tests for untested services — `BulkRefactoringService`, `CohesionAnalysisService`, `ConsumerAnalysisService`, `TypeExtractionService`, `TypeMoveService`.

## P2 — Post-1.0

- [ ] **REL-07**: Add CI badge to README.
- [ ] **REL-09**: Document cross-platform limitations.
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

## Rules

- Keep only incomplete items above. No completed items, no archive.
- Reprioritize on each audit pass.
