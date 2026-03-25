# Backlog

This is the only canonical location for unfinished work items.

## Pre-Release Audit Findings (2026-03-25)

### Critical — Must Fix Before Public Release

- [x] **SEC-01**: ~~Add path validation to EditTools + MultiFileEditTools~~ — Done.
- [x] **SEC-02**: ~~Fix path prefix match in ClientRootPathValidator~~ — Done. Appends directory separator before comparison.
- [x] **REL-01**: ~~Rename `Company.` namespace prefix~~ — Done. Renamed to `RoslynMcp.*`.
- [x] **REL-02**: ~~Add CONTRIBUTING.md~~ — Done.
- [x] **REL-03**: ~~Add CHANGELOG.md~~ — Done.
- [x] **REL-04**: ~~Set explicit version~~ — Done. Set `0.9.0-beta.1` in `Directory.Build.props`.

### High — Should Fix, Significant Quality Impact

- [x] **ERR-01**: ~~Add FileNotFoundException handler to ToolErrorHandler~~ — Done. Added `FileNotFoundException` and `DirectoryNotFoundException` handlers.
- [x] **ERR-02**: ~~Wrap resource handlers in error handling~~ — Done. Added try/catch to all `WorkspaceResources` methods.
- [ ] **ERR-03**: Wrap prompt handlers in error handling — `RoslynPrompts.cs` methods have no error wrapping. If data fetching fails mid-prompt, raw exceptions propagate. (`RoslynPrompts.cs`)
- [x] **API-01**: ~~Fix source_file resource for missing docs~~ — Done. Now throws `KeyNotFoundException`.
- [ ] **CON-01**: Fix `GetStatus()` race in `WorkspaceManager` — reads session state (`Workspace`, `LoadedPath`, `ProjectStatuses`) without holding `LoadLock` while `LoadIntoSessionAsync` can concurrently modify them. Returns semantically incorrect data during reload. (`WorkspaceManager.cs:96-321`)
- [x] **PERF-01**: ~~Fix triple .csproj parse in BuildProjectStatuses~~ — Done. `LoadProjectDocument` called once per project, `XDocument` passed to extractors.
- [ ] **CFG-01**: Wire options classes to configuration — `ValidationServiceOptions` (build/test timeouts), `WorkspaceManagerOptions` (max workspaces), `WorkspaceExecutionGate` timeout are all hardcoded defaults with no way for users to override via env vars or config. (`Program.cs`, options classes)

### Medium — Important Improvements

- [ ] **SEC-03**: Document MSBuild loading security model — loading a solution executes MSBuild targets with no sandboxing. A malicious `.csproj` can run arbitrary code. Document prominently in README. (`WorkspaceManager.cs:230`)
- [ ] **SEC-04**: Symlink/junction bypass in `ClientRootPathValidator` — `Path.GetFullPath()` does not resolve symlinks. A symlink inside an allowed root can point outside it. (`ClientRootPathValidator.cs:40`)
- [ ] **ERR-04**: Add logging to bare `catch` blocks — `ClientRootPathValidator.cs:61-64` silently swallows all exceptions when roots request fails; `WorkspaceManager.cs:411-414` silently swallows XML parse failures.
- [ ] **ERR-05**: Capture workspace diagnostics on load failure — when `OpenSolutionAsync` throws, the diagnostic handler may have captured useful info, but the exception path disposes the session without returning those diagnostics. (`WorkspaceManager.cs:235-264`)
- [ ] **ERR-06**: Bound `WorkspaceDiagnostics` queue — `ConcurrentQueue<DiagnosticDto>` grows unbounded on repeated load failures. Add cap or LRU eviction. (`WorkspaceManager.cs:439`)
- [ ] **PERF-02**: Cache test discovery results per workspace version — `DiscoverTestsAsync` iterates ALL documents in ALL test projects on every call, including from `FindRelatedTestsAsync`. (`ValidationService.cs:73-148`)
- [ ] **PERF-03**: Reduce `PreviewStore` memory pressure — stores full `Solution` snapshots (immutable but large). 50 entries x large solutions = significant memory. Consider size-aware eviction or lower cap. (`PreviewStore.cs:8-9`)
- [ ] **API-02**: Add caveat to `test_related` / `test_related_files` descriptions — these use heuristic name matching, not semantic analysis. AI agents may incorrectly trust results as exhaustive. (`ValidationTools.cs:83-117`)
- [ ] **API-03**: Standardize default `limit` values — `symbol_search` defaults to 20, `find_unused_symbols` to 50, `semantic_search` to 50. Should be consistent.
- [ ] **REL-05**: Add GitHub issue/PR templates. (`.github/`)
- [ ] **REL-06**: Expand file watcher to include `.csproj`, `.props`, `.targets` — currently only watches `*.cs` files. Changes to project files won't mark workspace stale. (`FileWatcherService.cs:22`)
- [ ] **CON-02**: Apply gate per-workspace instead of global `__apply__` — `ApplyGateKey` shares a single semaphore for ALL apply operations across ALL workspaces, serializing unrelated operations. (`WorkspaceExecutionGate.cs:22,37`)
- [ ] **TEST-01**: Add negative/edge-case tests — malformed symbol handles, null/empty parameters, concurrent tool calls, malformed XML project files.
- [ ] **TEST-02**: Add dedicated tests for `FileWatcherService`, `CompletionService`, `SyntaxService`, `EditService`.

### Low — Nice to Have

- [ ] **CODE-01**: Extract shared `JsonSerializerOptions` — duplicated `new() { WriteIndented = true }` in 14+ tool classes. (`All tool classes`)
- [ ] **CODE-02**: Fix triple `GetSummary()` call in `server_info`. (`ServerTools.cs:36-53`)
- [ ] **CODE-03**: Extract `ProjectMetadataReader` from `WorkspaceManager` static helpers. (`WorkspaceManager.cs:356-415`)
- [ ] **CODE-04**: Generalize `PreviewStore`/`CompositePreviewStore` with `BoundedStore<T>` base class — nearly identical TTL/eviction logic.
- [ ] **API-04**: Add truncation/pagination to prompt templates for large workspaces — `analyze_dependencies` embeds full project graph + namespace deps + NuGet deps. (`RoslynPrompts.cs:220-265`)
- [ ] **API-05**: Consider promoting `test_coverage` to stable — functionally complete, uses standard `coverlet.collector`.
- [ ] **API-06**: Consider adding `undo`/`revert_last_apply` capability.
- [ ] **REL-07**: Add CI badge to README.
- [ ] **REL-08**: Add `SECURITY.md` and `CODE_OF_CONDUCT.md`.
- [ ] **REL-09**: Document cross-platform limitations.
- [ ] **TEST-03**: Add performance baseline tests for large solutions.
- [ ] **TEST-04**: Clean up temp directories in test infrastructure — add `[AssemblyCleanup]` handler.
- [ ] **PERF-04**: Make `DotnetCommandRunner.OutputLimit` (12000 chars) configurable for verbose build output.

## Backlog Rules

- Keep only incomplete items here.
- Remove completed items after verification.
- Do not store session logs, handoff notes, or temporary plans in this file.
