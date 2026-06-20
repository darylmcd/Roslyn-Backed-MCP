# Backlog sweep plan — 20260620T215409Z

**Generated:** 2026-06-20T21:54:09Z
**Backlog snapshot:** 2026-06-20T04:18:39Z
**Initiative count:** 1
**Anchor verification:** performed (orchestrator pre-derived sites; deepener re-verifies)

> **Scope note (operator-directed):** This plan is a deliberately bounded LOW-RISK subset of the open row `compilation-cache-adoption-read-side`. Re-derivation found that row's broader scope is much larger than its anchors (48 raw `GetCompilationAsync` sites / 30 files, with safety-critical forked-solution exclusions). The operator chose to sweep only the cleanest live-solution reads (group-a core) now; the row stays OPEN for the remaining a-tail / group-b / group-c (hazard) work.

<!-- BSWEEP:STATUS-TABLE BEGIN — generated from state.json; do not edit by hand -->
## Status (generated)

| # | id | status | PR | rows closed |
|---|----|--------|----|-------------|
| 1 | compcache-batch-a-core-reference-reads | merged | [#1005](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1005) | — |
<!-- BSWEEP:STATUS-TABLE END -->

## Initiatives (in order)

### 1. compcache-batch-a-core-reference-reads

| Field | Content |
|---|---|
| Diagnosis | Three raw `project.GetCompilationAsync(ct)` call sites remain in the two group-a core services. **TestReferenceMapService** (`src/RoslynMcp.Roslyn/Services/TestReferenceMapService.cs`): line 150 inside `CollectProductiveSymbolsAsync` and line 178 inside `RecordTestProjectReferencesAsync`. Both private helpers are called exclusively from `BuildAsync`, which opens with `var solution = _workspace.GetCurrentSolution(workspaceId)` — confirmed live-solution only; no forked-solution path. **ReferenceService** (`src/RoslynMcp.Roslyn/Services/ReferenceService.cs`): line 245 inside private static `FindInterfaceMemberImplementationsAsync`; its sole call-site `FindSiblingInterfaceImplementationsAsync` (line 211) obtains `var solution = _workspace.GetCurrentSolution(workspaceId)` — confirmed live-solution only. The group-c HAZARD gating does NOT apply to this batch. Detail-file anchor for `ReferenceService` cites `:231`; live site is `:245` — anchor drift. Both services registered via type-based `services.AddSingleton<IInterface, Impl>()` — no factory lambda, so no DI file edit required when adding a constructor parameter. |
| Approach | **1. TestReferenceMapService** — add `private readonly ICompilationCache _compilationCache;`; extend constructor to accept `ICompilationCache compilationCache` (mirror `CouplingAnalysisService` ctor at `src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs:24-32`). Replace the line-150 and line-178 `await project.GetCompilationAsync(ct)` with `await _compilationCache.GetCompilationAsync(workspaceId, project, ct)`; thread `workspaceId` from `BuildAsync` into the two private helpers. **2. ReferenceService** — add the field + extend the constructor; replace the line-245 site with the cache call, threading `workspaceId` into `FindInterfaceMemberImplementationsAsync` (already in scope at the `FindSiblingInterfaceImplementationsAsync` caller). **3. Test ctor fixes** — `tests/RoslynMcp.Tests/TestReferenceMapServiceTests.cs:35` and `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs:89-91` (the latter already has a `compilationCache` local in scope). **4. New adoption tests** — extend `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs` with two `[TestMethod]`s (one per service) mirroring the batch-1 `RecordingCompilationCache` shape: assert `GetCompilationCallCount > 0` + shared/reference-equal warm compilation. |
| Scope | Production files touched: 2 — `src/RoslynMcp.Roslyn/Services/TestReferenceMapService.cs`, `src/RoslynMcp.Roslyn/Services/ReferenceService.cs`. No DI registration edit (type-based `AddSingleton` resolves the new ctor param). Test files: 3 — `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs` (extend), `tests/RoslynMcp.Tests/TestReferenceMapServiceTests.cs` (ctor fix), `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs` (ctor fix). Within Rule 3 (≤4 prod) and Rule 4 (≤3 test). |
| Tool policy | edit-only |
| Estimated context cost | 32000 |
| Risks | (1) `workspaceId` threading into the three private helpers (two statics in TestReferenceMapService, one static in ReferenceService) — low risk, single call-site each with `workspaceId` already in scope. (2) `TestServiceContainer.cs` manual `ReferenceService` construction has a `compilationCache` local at line 96 — verify its type is `ICompilationCache` before inserting. (3) Partial adoption — does NOT close the backlog row; the open row's progress note must be updated. (4) Anchor drift on `ReferenceService.cs` (:231 stale → :245 live) — do not rely on the stale line number. |
| Validation | (1) `mcp__roslyn__compile_check` after each edit (0 CS errors). (2) `mcp__roslyn__test_run --filter "TestReferenceMapServiceTests\|CompilationCacheAdoptionTests"` — existing pass + 2 new methods assert `GetCompilationCallCount > 0`. (3) `Grep` confirms zero remaining `GetCompilationAsync` in both production files post-edit. (4) Local gate: `fallback_compile` (`dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true`) in the worktree, then the PR CI `verify-release.ps1` is the authoritative gate. |
| Performance review | N/A — consistency/refactor fix, no hot-path behavioral change; wire-equivalent or faster under GC pressure. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Route `TestReferenceMapService` and `ReferenceService` compilation fetches through `ICompilationCache` (batch a of compilation-cache read-side adoption; 3 raw sites migrated). |
| Backlog sync | No row close (partial adoption of `compilation-cache-adoption-read-side`; a-tail / b / c remain). Update the open row's progress note to reflect batch a (core reference reads) shipped. |

