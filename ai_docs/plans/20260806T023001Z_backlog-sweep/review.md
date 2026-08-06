# Plan review — 2026-08-06 (cycle 0)

**Plan reviewed:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260806T023001Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 10 (all pending)
**Findings:** block: 0, warn: 3, info: 6
**Anchor verification:** performed (all 10 initiatives spot-checked against live source, not just the first 3)

## Summary

No block findings. All ten stanzas clear Rules 1, 3, 3b, 4 and 5 mechanically: no multi-row bundles (max `rowsClosedCount` is 1; initiative 10 closes zero by design), production-file counts peak at 4 (initiatives 1 and 9, exactly at cap, no exemption claimed or needed), test-file counts peak at 3 (initiative 1, exactly at cap), every `toolPolicy` is an explicit `edit-only` with no solution-wide-rename shape hiding behind it, and the highest `estimatedContextTokens` is 55K against an 80K ceiling. Anchor freshness is unusually good — I re-read the cited constructs for every initiative and found them live and shaped as described, including the two load-bearing claims in initiative 1 (`AtomicFileWriter.WriteAllTextAsync`'s trailing `ILogger? logger = null` really does permit an appended `Encoding?` without rebinding the three positional callers, and `CsprojSemanticEquality.CreateSnapshot(byte[])` really does return a `ProjectFileSnapshot.TextEncoding` and is `internal` in the same assembly). I also independently confirmed initiative 2's Directive-#1 escape hatch: `ai_docs/items/lru-eviction-concurrent-reader-safety-overstated.md`'s Acceptance genuinely sanctions the documentation-correction OR-branch plus a pinning characterization test, so the doc-only resolution of a live concurrency gap is licensed by the row, not a reviewer-visible shortcut. The three warnings are all about blast-radius honesty rather than budget: initiative 4 calls a compile_check-local containment a "root-cause fix" while the shared `ProjectFilterHelper.FilterProjects` `is null` gate it diagnoses is still consumed by 17 other call sites that never see a normalized filter; initiative 10 carries `fanoutEstimate: null` while its own Diagnosis counts ~14 live-exposed consumers of the semantics it is changing; and the conflict edge between initiatives 2 and 5 is tighter than the graph shows — both rewrite adjoining `<para>` blocks of the *same* `<remarks>` on `TryReloadEvictedWorkspaceForRetryAsync`, so they must be serialized and the merged prose re-read, not auto-merged. Reviewer-computed conflict graph matches the orchestrator's exactly.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| compile-check-project-filter-normalize | warn | 5b | `fanoutEstimate: null`; Risks claims `ProjectFilterHelper.FilterProjects` is "already-consistent once fed a normalized value", but that helper has 18 call sites across 14 service files (`CohesionAnalysisService:39`, `CodeMetricsService:51`, `CodePatternAnalyzer:30/152`, `AnalyzerInfoService:30`, `CouplingAnalysisService:75`, `DiRegistrationService:172`, `DuplicateMethodDetectorService:59`, `ExceptionFlowService:55`, `FixAllService:235`, `FormatVerifyService:31`, `MsBuildEvaluationService:216`, `NamespaceDependencyService:33`, `UnusedCodeAnalyzer:63/603/649/1130`) that never receive a normalized value. "Root-cause fix" overstates a compile_check-local containment. |
| compilation-cache-adoption-read-side | warn | 5b | `fanoutEstimate: null` on an initiative whose Diagnosis states "roughly 14 production call sites are live-exposed today"; no probe recorded and Risks never states the probe was skipped (unlike initiatives 2/4/6/7/9, which all do). The `CancellationToken.None` + `WaitAsync` change is behavior-affecting for all 14 consumers even though `ICompilationCache`'s signature is untouched. |
| lru-eviction-concurrent-reader-safety-overstated | warn | C2-wave-conflict | The (2,5) edge on `ToolDispatch.cs` is sub-file overlapping: initiative 2 rewrites the `<para>` at `:327-341` ("never yanked out from under a concurrent caller"), initiative 5 rewrites the immediately-adjacent `<para>` at `:342-346` ("no diagnostic is lost") — verified live, both inside ONE `<remarks>` on `TryReloadEvictedWorkspaceForRetryAsync`. Independent landings can produce self-contradictory merged prose. Serialize; the second to land must re-read the merged comment. |
| validate-workspace-diagnostic-harvest-reconcile | info | 5b | Extract-method refactor with `fanoutEstimate: null` and no Risks explanation of the skipped probe (plan Step 7 self-vet requires one). Reviewer-verified true fanout is 1 (new symbol, single call site at `WorkspaceValidationService.cs:254`). Bookkeeping gap only. |
| document-git-status-unknown-verdict | info | anchor-stale | Diagnosis cites `DetermineOverallStatus (WorkspaceValidationService.cs:552)`; no such symbol exists — the method is `ComputeOverallStatus` at `:539`, and `:552` is the `return "test-zero-run";` inside it. Initiative 3 names the same method correctly, so the plan is internally inconsistent. |
| compilation-cache-adoption-read-side | info | 3 | Backlog sync mandates four substantive amendments to `ai_docs/items/compilation-cache-adoption-read-side.md`, but Scope lists only `CompilationCache.cs` and `productionFilesTouched: 1`. Initiative 9 counts `ai_docs/**.md` as production files — the plan double-standards doc counting. Corrected count is 2, still within cap. |
| workspace-eviction-retry-swallowed-log | info | 4 | Approach copies the `RecordingLoggerFactory`/`RecordingLogger` pair from `WorkspaceCloseDrainTests.cs:639-667` into a second test file rather than extracting shared test infrastructure. Within Rule 4, but warrants a follow-up row per Directive #3. |
| mutation-write-paths-drop-original-encoding | info | 3 | Simultaneously at both caps (4 production files, 3 test files, zero headroom) while Risks (2) documents a 4th same-root-cause site (`apply_composite_preview` via `CompositeApplyOrchestrator.cs:80`) left unfixed. Load-bearing claims verified live. Any scope growth during execution breaches a cap — file the follow-up row, don't absorb it. |
| workspace-eviction-retry-untested-branches | info | 4 | Scope declares `testFilesAdded: 1` but hedges "a small addition to or new sibling of `RecordingTestRunnerService`" — a new sibling makes it 2. Within cap either way; executor should pick one. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: **yes** — exact edge-set match)

```json
{
  "edges": [ { "a": 2, "b": 5, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] } ],
  "degrees": { "1": 0, "2": 1, "3": 0, "4": 0, "5": 1, "6": 0, "7": 0, "8": 0, "9": 0, "10": 0 },
  "zeroDegreeInitiatives": [1, 3, 4, 6, 7, 8, 9, 10]
}
```

Near-miss pairs checked and correctly excluded: 3 (`WorkspaceValidationService.cs`) vs 9 (`ValidationBundleTools.cs` — 9 only *reads* `WorkspaceValidationService.cs`, no edit); 4 (`CompileCheckService.cs`) vs 5 (`CompileCheckTools.cs`). Orders 2 and 5 are not consecutive in the order-sorted sequence, so no adjacent-order wave-conflict fires; the sub-file overlap is escalated as a warn above instead.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | 2 only | no |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (+ partials) | none | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |

No hotspot adjacency. Initiative 5's claim that adding an optional DI-bound `ILoggerFactory?` parameter to two already-registered tools needs no `ServerSurfaceCatalog.cs` edit was independently verified — catalog entries record name/category/tier/flags/description only, never parameter lists (e.g. `ServerSurfaceCatalog.Workspace.cs:9`), so the RMCP001/RMCP002 gate is not tripped and the hotspot stays untouched.

## Stale-row spot check

| Row id | Present? |
|---|---|
| mutation-write-paths-drop-original-encoding | yes |
| lru-eviction-concurrent-reader-safety-overstated | yes |
| validate-workspace-diagnostic-harvest-reconcile | yes |
| compile-check-project-filter-normalize | yes |
| workspace-eviction-retry-swallowed-log | yes |
| undo-tests-assert-vacuous-noop-protection | yes |
| workspace-eviction-retry-untested-branches | yes |
| tool-error-handler-envelope-duplication | yes |
| document-git-status-unknown-verdict | yes |
| compilation-cache-adoption-read-side | yes (stays open by design — initiative 10 is a bounded batch, `backlogRowsClosed: []`) |

All ten rows present in `ai_docs/backlog.md` with matching `ai_docs/items/<id>.md` detail files. No stale rows.

## Recommended next step

`passed-with-warnings` — proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`, surfacing the three warnings in the run summary. Specifically brief the executor to (a) serialize initiatives 2 and 5 and re-read the merged `TryReloadEvictedWorkspaceForRetryAsync` `<remarks>` after the second lands, (b) file a follow-up backlog row for the 17 un-normalized `ProjectFilterHelper.FilterProjects` callers rather than widening initiative 4, and (c) treat initiative 10 as touching ~14 consumers' runtime semantics when choosing validation depth.