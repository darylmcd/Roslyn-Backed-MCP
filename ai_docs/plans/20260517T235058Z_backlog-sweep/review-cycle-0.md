# Plan review — 2026-05-17T23:50:58Z (cycle 0)

**Plan reviewed:** `ai_docs/plans/20260517T235058Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 10 pending (8 P1 High + 2 P2 Medium; init 10 closes 2 rows via Rule 1 bundle)
**Findings:** block: 0, warn: 2, info: 2
**Anchor verification:** performed (initiatives 1, 2, 3 spot-checked — all anchors resolve; minor ±1-line drift acknowledged on init 1 and init 3, well within tolerance)

## Summary

The plan is structurally sound. Rule 1 bundling at initiative 10 is well-justified: `FindOverridesAsync` in `ReferenceService.cs` conflates `SymbolFinder.FindOverridesAsync` results with `FindInterfaceMemberImplementationsAsync` results on lines 158-160, which is the actual shared code path producing both gh #736 and gh #737 symptoms. All four Rule 1 conditions are cited with file:line evidence. The reviewer-computed conflict graph matches the orchestrator's exactly (single edge: initiatives 3 and 10 share `SymbolRelationshipService.cs`, touching different methods — `GetSymbolRelationshipsAsync` vs `GetMemberHierarchyAsync` — but the same file, so the conflict edge is real). Order separation of 7 means no wave-adjacency concern. No hotspot files touched. No stale backlog rows. Two Rule 5b warnings: initiatives 6 and 10 perform refactor-shaped work (signature changes / interface additions affecting multiple in-file call sites) without a recorded `fanoutEstimate`; both are confined to the listed files and the planner consciously skipped the probe, but Rule 5b requires the probe on refactor-shaped initiatives. Two info advisories for the planner's own `judgmentHeavy` (init 7 — field-migration strategy) and `anchorStale` (init 10 — original backlog anchors `MemberHierarchyService.cs` / `OverridesService.cs` do not exist; plan rewrote to live anchors) flags. Initiative 8 sits at the Rule 3 4-file ceiling with explicit deferral of the broader ~18-tool description-string update as a follow-on initiative — disciplined scope-keeping rather than under-scoping. Initiative 10 is also at the 4-file ceiling; the plan acknowledges `SymbolTools.cs` "may or may not" need touch — verification shows `GetMemberHierarchy` wrapper does `JsonSerializer.Serialize(result, JsonDefaults.Indented)` on the DTO directly, so the DTO field addition auto-serializes without wrapper edits and the 4-file count is sustainable.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| extract-interface-cross-project-uncompilable (6) | warn | 5b | Approach mentions signature change ("accept `IReadOnlyCollection<string> requiredNamespaces` instead of `filterUsings` bool") and updates two call sites at `PreviewExtractInterfaceAsync:156` + `CreateInterfaceExtractionSolutionAsync:755`; `fanoutEstimate: null`. Planner skipped fanout probe on a refactor-shaped initiative. Confined to 1 file in practice, but Rule 5b requires the probe. |
| member-hierarchy-overrides-mislabels-sibling-interface-impls (10) | warn | 5b | Approach mentions splitting `FindOverridesAsync` into two methods, adding new public `FindSiblingInterfaceImplementationsAsync` to `IReferenceService`, and changing the `Overrides` bucket semantics across both `member_hierarchy` and `symbol_relationships`. Risks section explicitly says "Fanout probe skipped — surgical fix to coupled service methods." `fanoutEstimate: null`. Per Rule 5b: warn — refactor-shaped initiative without fanout probe. |
| split-service-with-di-broken-output (7) | info | judgment | Plan flags `judgmentHeavy=true` and documents two plausible field-migration strategies (duplicate shared fields into every referencing partition vs require callers to list fields in `SplitServicePartition`). Executor must resolve. Already disclosed; advisory. |
| member-hierarchy-overrides-mislabels-sibling-interface-impls (10) | info | anchor-stale | Original backlog rows (gh #736, gh #737) cite `MemberHierarchyService.cs` and `OverridesService.cs` which do not exist in the repo. Plan correctly rewrote citations to live anchors `ReferenceService.cs:142-174` + `SymbolRelationshipService.cs:116-130`. Disclosed in Diagnosis. Heads-up for executor. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: **yes** — exact match)

```json
{
  "edges": [
    { "a": 3, "b": 10, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/SymbolRelationshipService.cs"] }
  ],
  "degrees": { "1": 0, "2": 0, "3": 1, "4": 0, "5": 0, "6": 0, "7": 0, "8": 0, "9": 0, "10": 1 },
  "zeroDegreeInitiatives": [1, 2, 4, 5, 6, 7, 8, 9]
}
```

Notes: initiatives 3 and 10 touch different methods in `SymbolRelationshipService.cs` (`GetSymbolRelationshipsAsync` line 131 vs `GetMemberHierarchyAsync` line 116-130), so they could conceivably ship in parallel without semantic conflict — but git-level file overlap still requires serialization. Adjacent-order gap is 7 (orders 3 and 10), so no wave-adjacency violation. Both have conflict-degree 1, below the ≥2 threshold for an info advisory.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `ServerSurfaceCatalog.cs` (+partials) | none | n/a |
| `ServiceCollectionExtensions.cs` | none | n/a |
| `WorkspaceManager.cs` | none | n/a |

No hotspot adjacency concerns. None of the 10 initiatives touch addenda-listed hotspot files.

## Stale-row spot check

All 11 row IDs (including both bundled rows) are still present in the current backlog.

## Recommended next step

Outcome is **passed-with-warnings**. Proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`. Surface the two Rule 5b warnings in the run summary so the executor knows initiatives 6 and 10 had no fanout probe recorded — if discovery at runtime shows broader fanout than `productionFilesTouched`, the executor can fall back to Step 5a re-vet and defer if Rule 3 would be exceeded. The 4-file ceiling on initiatives 8 and 10 leaves no headroom; executor must NOT widen scope inside those initiatives. The judgmentHeavy decision in init 7 (field-migration strategy) and the anchor-rewrite acknowledgment in init 10 are pre-disclosed — executor should pick the safe minimal strategy in init 7 (per the Risks section: "include all instance fields in every referencing partition, omit from facade") unless source verification justifies otherwise.
