# Plan: Implement Top 10 Pre-1.0 Backlog Items

Status: Completed 2026-03-26. All 3 PRs merged (#15, #16, #17).
Backlog reference: `ai_docs/backlog.md`

## Context

All P0 (release-blocking) items have been resolved. The backlog now contains P1 and P2 items that should be addressed before a stable 1.0 NuGet release. This plan covers the top 10 highest-impact remaining items, organized into 3 independent PRs by theme.

Breaking changes and refactoring are acceptable — the package is still private.

---

## PR 1: Hardening & Configuration (CFG-01, PERF-03, CON-02, REL-06)

### 1. CFG-01 — Wire options classes to env var overrides
**Files:** `src/RoslynMcp.Host.Stdio/Program.cs`, `src/RoslynMcp.Roslyn/Services/WorkspaceManagerOptions.cs`, `src/RoslynMcp.Roslyn/Services/ValidationServiceOptions.cs`, `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs`

- Read env vars in `Program.cs` at startup and bind to existing options classes
- Supported env vars:
  - `ROSLYNMCP_MAX_WORKSPACES` -> `WorkspaceManagerOptions.MaxConcurrentWorkspaces`
  - `ROSLYNMCP_BUILD_TIMEOUT_SECONDS` -> `ValidationServiceOptions.BuildTimeoutSeconds`
  - `ROSLYNMCP_TEST_TIMEOUT_SECONDS` -> `ValidationServiceOptions.TestTimeoutSeconds`
  - `ROSLYNMCP_PREVIEW_MAX_ENTRIES` -> PreviewStore max entries (new option)
- Keep current hardcoded values as defaults — env vars override only when set
- Document supported env vars in README under a new "Configuration" section

### 2. PERF-03 — Reduce PreviewStore memory pressure
**Files:** `src/RoslynMcp.Roslyn/Services/PreviewStore.cs`, `src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs`, `src/RoslynMcp.Roslyn/Services/ProjectMutationPreviewStore.cs`

- Lower default max entries from 50 to 20
- Make max entries configurable via the env var from CFG-01
- Consider storing only the changed `Document`(s) + original workspace ID instead of full `Solution` snapshots (investigate feasibility — if Solution is needed for apply, keep it but lower the cap)

### 3. CON-02 — Per-workspace apply gate
**Files:** `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs`

- Change `RunAsync` to use `$"__apply__:{workspaceId}"` instead of the global `ApplyGateKey` constant
- Callers already pass workspace-specific keys for load gates — apply the same pattern
- The `_workspaceGates` ConcurrentDictionary already handles per-key semaphore creation
- Remove or deprecate the `ApplyGateKey` constant

### 4. REL-06 — Expand file watcher to project files
**Files:** `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`

- Add watch filters for `*.csproj`, `*.props`, `*.targets`, `*.sln`, `*.slnx`
- On project file change, mark workspace stale (same behavior as `.cs` change)
- Use same `FileSystemWatcher` pattern already in place

**Verification:** `./eng/verify-release.ps1` — all 98+ tests pass

---

## PR 2: Code Quality & API Consistency (CQ-01, CQ-02, API-03, API-02)

### 5. CQ-01 — Reduce complexity in 3 high-CC methods
**Files:** `src/RoslynMcp.Roslyn/Helpers/CodePatternAnalyzer.cs`, `src/RoslynMcp.Roslyn/Helpers/SymbolMapper.cs`

- **`ParseSemanticQuery` (CC=32, ~94 LOC):** Extract keyword-to-predicate mappings into a `Dictionary<string, Func<ISymbol, bool>>`. The method becomes: split query into tokens, look up each token in the dictionary, compose predicates with AND.
- **`SymbolMapper.ToDto` (CC=28, ~86 LOC):** Extract per-`SymbolKind` mapping into private helper methods (e.g., `MapNamedType`, `MapMethod`, `MapProperty`). The main method becomes a switch dispatching to helpers.
- **`ClassifyReferenceLocation` (CC=27, ~56 LOC):** Extract parent-node-kind classification into a `Dictionary<SyntaxKind, string>` lookup table. Fall through to a small set of specific checks for complex cases.

Target: reduce each method to CC < 15.

### 6. CQ-02 — Audit unused public symbols
**Files:** various across solution

- Run `find_unused_symbols` with `includePublic: true` via the Roslyn MCP
- Cross-reference results against MCP tool/prompt/resource registration (attribute-based, resolved via reflection)
- Remove any truly dead code
- For false positives (reflection-wired), no action needed — Roslyn can't see reflection callers

### 7. API-03 — Standardize default limit values
**Files:** `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/CohesionAnalysisTools.cs`

- Change `symbol_search` default limit from 20 to 50 (aligns with all other search tools)
- Single constant `DefaultSearchLimit = 50` shared across tool classes, or just update the one outlier

### 8. API-02 — Add heuristic caveat to test_related descriptions
**Files:** `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs`

- Update `[Description]` on `test_related` and `test_related_files` tools
- Append: "Results use heuristic name matching and may not be exhaustive."
- No logic changes, just description text

**Verification:** `./eng/verify-release.ps1` — all tests pass

---

## PR 3: Documentation & Test Infrastructure (DOC-01, TEST-05)

### 9. DOC-01 — XML doc comments for 15 service interfaces
**Files:** 15 files in `src/RoslynMcp.Core/Services/`

Interfaces needing `<summary>` XML doc:
- `IBuildService` — build workspace/project and return diagnostics
- `ICodeMetricsService` — cyclomatic complexity and LOC metrics
- `ICodePatternAnalyzer` — semantic search via natural language queries
- `IDependencyAnalysisService` — NuGet/project/namespace dependency analysis
- `IMutationAnalysisService` — find mutable members and their callers
- `IReferenceService` — find references and classify usage locations
- `ISymbolNavigationService` — go-to-definition and type-definition navigation
- `ISymbolRelationshipService` — combined symbol relationship summary
- `ISymbolSearchService` — symbol search by name pattern
- `ITestDiscoveryService` — discover test methods in test projects
- `ITestRunnerService` — execute tests and return structured results
- `IUnusedCodeAnalyzer` — find symbols with zero references
- `IWorkspaceManager` — load, reload, close solution workspaces
- `ICompletionService` — IntelliSense completions at a position
- `IEditService` — apply text edits to workspace documents

Follow existing patterns (e.g., `IDiagnosticService`, `IRefactoringService` which are already documented).

### 10. TEST-05 — Install coverlet and establish baseline coverage
**Files:** `tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj`

- Add `coverlet.collector` NuGet package
- Run `dotnet test --collect:"XPlat Code Coverage"` to generate baseline Cobertura XML
- Document baseline numbers in PR description
- No hard coverage gate yet — this establishes the metric for future enforcement

**Verification:** `./eng/verify-release.ps1` — all tests pass, coverage XML generates

---

## Summary

| PR | Items | Scope | Independence |
|----|-------|-------|-------------|
| PR 1 | CFG-01, PERF-03, CON-02, REL-06 | Hardening & config | Independent |
| PR 2 | CQ-01, CQ-02, API-03, API-02 | Code quality & API | Independent |
| PR 3 | DOC-01, TEST-05 | Docs & test infra | Independent |

All 3 PRs are independent and can be implemented/merged in any order or in parallel.
