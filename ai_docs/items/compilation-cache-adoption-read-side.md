# compilation-cache-adoption-read-side — route remaining read-side compilation fetches through ICompilationCache

**row:** `compilation-cache-adoption-read-side` · **pri:** `Medium` · **size:** `L` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompilationCache.cs`
- Group (a) static-helper read-side sites: `src/RoslynMcp.Roslyn/Services/TestReferenceMapService.cs:150/178`, `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:231`; secondary-path `ImpactSweepService`, `MutationAnalysisService`, `SymbolRelationshipService:427`
- Group (b) pre-edit live fetches in preview services (subagent-triaged, per-site confirm): `BuildService:149`, `FixAllService:306`, `BulkRefactoringService:166/554/574`, `CrossProjectRefactoringService:376`, `InterfaceExtractionService:494`, `ScaffoldingService.*`
- Group (c) HAZARD helpers: `SymbolResolver.ResolveByMetadataNameAsync:285` / `FindClosestMatchesAsync:125`, `SymbolHandleSerializer`
- `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs`

## Acceptance

- [ ] Per batch: a recording-`ICompilationCache` test asserts the migrated service invokes the cache (plain reference-equality is insufficient — Roslyn memoizes per `Project`), plus reference-equal warm compilation across successive reads at an unchanged version (see batch-1 test)
- [ ] Group (c) helpers gate on `solution == GetCurrentSolution(workspaceId)` before caching (forked-solution hazard below)
- [ ] Confirmed forked exclusions NOT cached: `InterfaceExtractionService:515`, `RefactoringService:415`, `TypeMoveService:217`

## Evidence

- ~10/24 adoption post-batch-2; `ICompilationCache` version-keyed live-solution-only contract. Source: 2026-05-29 memoization audit — first batch shipped, remainder subagent-triaged.

## Context

Read-side analysis services should obtain compilations via the singleton `ICompilationCache` (version-keyed cross-tool sharing + analyzer-bound caching + in-flight dedup) rather than raw `project.GetCompilationAsync`.

**Batches 1–2 SHIPPED:** batch 1 (#913) — `CouplingAnalysisService`, `ExceptionFlowService`, `AnalyzerInfoService`; batch 2 ([#936](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/936)) — `TypeConsumersService`, `CodePatternAnalyzer`, `SymbolSearchService`. All route through the cache (regression: `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs`), bringing adoption to ~10 of ~24 sites.

**Batch a (group-a core reads) SHIPPED:** [#1005](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1005) — `TestReferenceMapService` (2 sites) + `ReferenceService` (1 site), live-solution reads only. Remaining group-a tail: `ImpactSweepService`, `MutationAnalysisService`, `SymbolRelationshipService`.

**Scope note (2026-06-20 — re-derive):** a fresh count found **~48 raw `GetCompilationAsync` sites across ~30 files** — far more than the ~24 this row tracks. Several already-adopted batch-1/2 services retain intentional secondary/forked raw sites; `ScaffoldingService` is 3 partial files / 7 sites (cited here as one `ScaffoldingService.*` anchor); the confirmed forked exclusions (`InterfaceExtractionService:515`, `RefactoringService`, `TypeMoveService`) must stay raw. The `X/24` accounting is approximate and per-site live-vs-forked classification is required. **Re-scope this row (corrected anchors + per-site classification) before driving the group-b/c remainder.**

**HAZARD (group c):** the high-leverage `solution`-parameterized helpers are called with a FORKED solution by `CrossProjectRefactoringService:226`, so routing them through the version-keyed cache must gate on `solution == GetCurrentSolution(workspaceId)` or it serves a stale compilation.

Note: benefit is narrower than "rebuild→cached" because Roslyn already memoizes a `Project`'s compilation internally; the real wins are guaranteed cross-call sharing under GC pressure, analyzer-bound compilations, and concurrency in-flight dedup.

**Sweep-shaped follow-on — split into bounded child batches at `/backlog-sweep:prepare`.**
