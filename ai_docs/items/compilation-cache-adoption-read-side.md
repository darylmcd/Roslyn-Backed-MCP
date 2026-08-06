# compilation-cache-adoption-read-side — route remaining read-side compilation fetches through ICompilationCache

**row:** `compilation-cache-adoption-read-side` · **pri:** `Medium` · **size:** `L` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompilationCache.cs`
- Group (a) static-helper read-side sites — **all SHIPPED** (batch a #1005 + #1010): `TestReferenceMapService.cs:150/178`, `ReferenceService.cs:231`, `ImpactSweepService.cs:138/254`, `MutationAnalysisService.cs:354`, `SymbolRelationshipService.cs:430`
- Group (b) pre-edit live fetches in preview services (subagent-triaged, per-site confirm): `BuildService:149`, `FixAllService:306`, `BulkRefactoringService:166/554/574`, `CrossProjectRefactoringService:376` — **SHIPPED** (#1147); tail still open: `InterfaceExtractionService.cs:394/415`. ~~`ScaffoldingService.*`~~ — anchor RETRACTED, see Context
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

**Batch a (group-a core reads) SHIPPED:** [#1005](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1005) — `TestReferenceMapService` (2 sites) + `ReferenceService` (1 site), live-solution reads only. **Group-a tail also SHIPPED** in #1010 — `ImpactSweepService`, `MutationAnalysisService`, and `SymbolRelationshipService` all route through `ICompilationCache` today (`ImpactSweepService.cs:138/254`, `MutationAnalysisService.cs:354`, `SymbolRelationshipService.cs:430`) with regression coverage in `CompilationCacheAdoptionTests.cs`. **Group (a) is closed.**

**Scope note (2026-06-20 — re-derive; corrected 2026-08-06):** a fresh count found **~48 raw `GetCompilationAsync` sites across ~30 files** — far more than the ~24 this row tracks. Several already-adopted batch-1/2 services retain intentional secondary/forked raw sites; the confirmed forked exclusions (`InterfaceExtractionService:515`, `RefactoringService`, `TypeMoveService`) must stay raw. The `X/24` accounting is approximate and per-site live-vs-forked classification is required. **Re-scope this row (corrected anchors + per-site classification) before driving the group-b/c remainder.**

**Anchor retraction (2026-08-06):** the `ScaffoldingService` entry ("3 partial files / 7 sites") is WITHDRAWN — `ScaffoldingService.cs`, `ScaffoldingService.TestPreview.cs`, `ScaffoldingService.TestBatchAndFirstTestPreview.cs`, and `IScaffoldingService.cs` contain **zero** `Compilation` / `GetCompilationAsync` references today. The anchor either never matched or was resolved without a doc update; do not re-scope work against it.

**HAZARD (group c):** the high-leverage `solution`-parameterized helpers are called with a FORKED solution by `CrossProjectRefactoringService:226`, so routing them through the version-keyed cache must gate on `solution == GetCurrentSolution(workspaceId)` or it serves a stale compilation.

Note: benefit is narrower than "rebuild→cached" because Roslyn already memoizes a `Project`'s compilation internally; the real wins are guaranteed cross-call sharing under GC pressure, analyzer-bound compilations, and concurrency in-flight dedup.

**Sweep-shaped follow-on — split into bounded child batches at `/backlog-sweep:prepare`.**
**Batch b (group-b preview-path partial slice) SHIPPED:** [#1147](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1147) — `BuildService`, `FixAllService`, `BulkRefactoringService`, `CrossProjectRefactoringService`, 4 of group (b)'s listed sites. Remaining group-b tail: `InterfaceExtractionService.cs:394/415` (both reachable off a `Solution` parameter rather than a live `workspaceId` read — needs the same forked-vs-live classification the group-c hazard note demands). Row deliberately left OPEN (`backlogRowsClosed: []`) — this was a bounded child batch of a size-L row, not the full close.

**~~New hazard found during PR #1147 code-quality review~~ — FIXED 2026-08-06 (batch: shared-task cancellation decoupling):** `CompilationCache.GetCompilationAsync` cached the `Task` created with the FIRST caller's `CancellationToken` and returned it on later hits with only a version check — no task-status check. A canceled/faulted task stayed installed until the workspace version bumped, so an unrelated caller at the same version observed `OperationCanceledException` from a token it never canceled. `GetCompilationWithAnalyzersAsync`/`_analyzerBound` had the identical exposure. **Resolution:** both shared tasks are now started with `CancellationToken.None`; each caller observes the shared task through `ObserveWithCallerToken` (`Task.WaitAsync(ct)`, with an already-canceled token short-circuited to `Task.FromCanceled`) so a caller's cancellation affects only that caller; and `EvictWhenBroken` attaches a `NotOnRanToCompletion` continuation that compare-and-removes the exact entry from `_compilations`/`_analyzerBound` when its shared task ends canceled or faulted, so the next caller re-populates instead of replaying the failure. Regression: `CompilationCacheAdoptionTests.GetCompilationAsync_OneCallersCancellation_DoesNotPoisonSharedEntry` and `GetCompilationWithAnalyzersAsync_OneCallersCancellation_DoesNotPoisonSharedEntry` (both verified failing against the pre-fix source).

**New coverage gap found during PR #1170 code-quality review (amend, not a new row — do not file a sibling):** the two regression tests above cancel the caller's token BEFORE the cache call, so `ObserveWithCallerToken` short-circuits via `Task.FromCanceled` and never reaches `shared.WaitAsync(ct)` — the branch that actually decouples a mid-flight caller from a canceling one. Because the shared task then runs to completion, `EvictWhenBroken`'s `NotOnRanToCompletion` continuation never fires in either test either. Separately, `GetCompilationAsync` (`CompilationCache.cs:89`) starts a full `project.GetCompilationAsync(CancellationToken.None)` before checking whether the caller's own `ct` is already canceled, so an already-canceled request now triggers a full uncancelable compile that pre-fix did not occur. Fix: (1) add a test that cancels caller A's token MID-FETCH (after the fetch is in flight) and asserts caller B at the unchanged version receives a real `Compilation` — this exercises `shared.WaitAsync(ct)`. (2) Add a test that faults or mid-fetch-cancels the shared task and asserts `EvictWhenBroken` removed the entry so the next caller re-populates. (3) Guard `GetCompilationAsync`'s entry: `if (ct.IsCancellationRequested) return Task.FromCanceled<Compilation?>(ct);` before installing a new cache entry — land together with the mid-flight test, since the guard makes the two current already-canceled regressions vacuous. (4) Restate the cancellation contract (ct aborts only your own await, never the shared fetch) on `ICompilationCache`'s remarks, not just the implementation.

**Remaining scope (as of 2026-08-06):** group-b tail `InterfaceExtractionService.cs:394/415` and group (c) helpers (`SymbolResolver.ResolveByMetadataNameAsync:285` / `FindClosestMatchesAsync:125`, `SymbolHandleSerializer`). Both need fresh per-site forked-vs-live classification — do NOT drive them off this doc's older inventory, which was found materially stale on 2026-08-06.
