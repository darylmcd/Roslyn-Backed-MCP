# Plan review — 2026-07-22 (cycle 0)

**Plan reviewed:** ai_docs/plans/20260722T194931Z_backlog-sweep/
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 10 (all pending)
**Findings:** block: 0, warn: 3, info: 4
**Anchor verification:** performed (first 3 initiatives spot-checked; all fresh)

## Summary

The plan is sound and ships. Every initiative is within Rules 3 (<=4 prod files), 4 (<=3 test files), and 5 (<=80K; max observed 55K). No Rule 1 bundling (all initiatives close exactly one row), no `fanoutOversize`, no under-scoped `fanoutEstimate > productionFilesTouched + 2`, and every initiative carries an explicit `edit-only` toolPolicy (the repo's self-edit caveat forbids `*_apply`/`*_preview` in the main checkout, so edit-only is the only viable classification for the extraction/move work anyway). My independently-rebuilt conflict graph is an exact match with the orchestrator's. The only findings concern the four-initiative scaffolding decomposition chain (orders 6-9), which all share `ScaffoldingService.cs` and are consecutive in order — but this is correct-by-construction: backlog `dependsOn` edges (7 deps 6, 8 deps 7, 9 deps 8) plus the file-overlap conflict edges force the executor to serialize them across waves regardless. The warns surface that fact; they are not defects.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| scaffolding-single-test-collaborator-extraction | warn | C2-wave-conflict | Adjacent-order 6 & 7 share `ScaffoldingService.cs`; mitigated by dependsOn(7->6) + conflict edge (serial-forced). |
| scaffolding-batch-first-test-collaborator-extraction | warn | C2-wave-conflict | Adjacent-order 7 & 8 share `ScaffoldingService.cs` + `.TestBatchAndFirstTestPreview.cs`; mitigated by dependsOn(8->7). |
| scaffolding-hotspot-complexity-reduction | warn | C2-wave-conflict | Adjacent-order 8 & 9 share `ScaffoldingService.cs` + `.TestBatchAndFirstTestPreview.cs`; mitigated by dependsOn(9->8). |
| scaffolding-type-collaborator-extraction | info | C2-wave-conflict | Initiative 6 conflicts with 3 peers (7,8,9); expect serial scheduling. |
| scaffolding-single-test-collaborator-extraction | info | C2-wave-conflict | Initiative 7 conflicts with 3 peers (6,8,9); expect serial scheduling. |
| scaffolding-batch-first-test-collaborator-extraction | info | C2-wave-conflict | Initiative 8 conflicts with 3 peers (6,7,9); expect serial scheduling. |
| scaffolding-hotspot-complexity-reduction | info | C2-wave-conflict | Initiative 9 conflicts with 3 peers (6,7,8); expect serial scheduling. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes — exact match)

```json
{
  "edges": [
    { "a": 1, "b": 5, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs"] },
    { "a": 6, "b": 7, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs"] },
    { "a": 6, "b": 8, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs"] },
    { "a": 6, "b": 9, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs"] },
    { "a": 7, "b": 8, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs", "src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestBatchAndFirstTestPreview.cs"] },
    { "a": 7, "b": 9, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs", "src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestPreview.cs", "src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestBatchAndFirstTestPreview.cs"] },
    { "a": 8, "b": 9, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs", "src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestBatchAndFirstTestPreview.cs"] }
  ],
  "degrees": { "1": 1, "2": 0, "3": 0, "4": 0, "5": 1, "6": 3, "7": 3, "8": 3, "9": 3, "10": 0 },
  "zeroDegreeInitiatives": [2, 3, 4, 10]
}
```

Note: every `dependsOn` build-order edge (5->1, 7->6, 8->7, 9->8) coincides with a file-overlap conflict edge, so the executor's wave batcher will not accidentally parallelize a compile-dependent pair — there is no zero-degree-but-compile-dependent hazard in this plan. Initiative 5 (`apply-undo-workflow-service-extraction`) shares `ApplyWithVerifyTool.cs` with initiative 1 (its unlanded prerequisite `apply-with-verify-cancelled-result-compensation`); the edge {1,5} keeps them out of the same wave and initiative 1's lower order lands it first.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs (+partials) | none | n/a |
| src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs | none | n/a |
| src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs | none | n/a |

Init 5 touches `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` (Roslyn layer) — a distinct file from the Host.Stdio hotspot; not a hotspot match. `ScaffoldingService.cs` is not on the addenda hotspot list (its serialization is handled via the conflict graph above).

## Stale-row spot check

| Row id | Present? |
|---|---|
| apply-with-verify-cancelled-result-compensation | yes |
| undo-revert-preserve-snapshot-on-failure-or-cancellation | yes |
| symbol-disambiguation-agent-first-default | yes |
| structuredcalltoolfilter-hotspot-decomposition-followup | yes |
| apply-undo-workflow-service-extraction | yes |
| scaffolding-type-collaborator-extraction | yes |
| scaffolding-single-test-collaborator-extraction | yes |
| scaffolding-batch-first-test-collaborator-extraction | yes |
| scaffolding-hotspot-complexity-reduction | yes |
| analysis-type-traversal-enumeration-helper | yes |

## Anchor freshness (first 3 initiatives)

- Init 1 (`ApplyWithVerifyTool.cs`): preBaseline read at L80-82, apply at L85, post-check try-block L105-161, `catch(OperationCanceledException)` at L163-191 — all cited anchors match. Fresh.
- Init 2 (`UndoService.cs`): `RevertAsync` L97-135 with `_snapshots.TryRemove` at L99, history removal L110-123, restore L127-134; `RevertBySequenceAsync` at L137 — cited anchors match. Fresh.
- Init 3 (`SymbolTools.cs`): `SearchSymbols` elicitation gate at L90, `TryElicitChoiceAsync` at L102, `chosenViaElicitation` at L123 — cited anchors match. Fresh.

## Recommended next step

- Outcome is `passed-with-warnings`: proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`, surfacing the three scaffolding-chain wave-conflict warns in the run summary. The executor's wave batcher already serializes orders 6->7->8->9 via the conflict graph + dependsOn; no remediation required.