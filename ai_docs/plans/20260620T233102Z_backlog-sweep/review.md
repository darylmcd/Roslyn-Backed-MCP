# Plan review — 2026-06-20T23:31:02Z (cycle 0)

**Plan reviewed:** ai_docs/plans/20260620T233102Z_backlog-sweep
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 10 pending
**Findings:** block: 0, warn: 1, info: 3
**Anchor verification:** performed (first 3 initiatives spot-checked + initiatives #2, #6 verified against source)

## Summary

This plan is in good shape and clears every hard cap (Rules 1–5 + 3b + 5b). No block findings. All 10 initiatives are 1-row-per-initiative (Rule 1 trivially satisfied; only #8/#9 close zero rows by design as deliberately-bounded partial batches). File budgets are within Rule 3 (max 4 prod files at #7), test budgets within Rule 4 (max 3 test files at #8), and context within Rule 5 (max 45K at #7). The independently recomputed conflict graph matches the orchestrator's exactly (edges 1↔6, 5↔10, 6↔7; degrees and zero-degree set identical). The single warn is an adjacent-order conflict: #6 and #7 (orders 6,7) share `ExceptionFlowTools.cs` — the planner's Step 6 should have separated them, and the execute wave-scheduler must not co-wave them. Three info findings: #6 has conflict-degree 2 (expect serial scheduling), the obsolete-#2 transition is expected at execute Step 5a (not a violation), and #6's test stanza under-specifies the design's 3-method lockstep requirement. Anchor spot-checks (#1 FindDuplicatedMethodsCore@427-453, #2 test_discover@54-143, #3 SecurityTools@65-71, #6 scopeProjectFilter@28) all resolve to current source — the obsolescence claim for #2 (pagination shipped) and the stable-tool no-rename hazard for #6 are both confirmed against ground truth.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| parameter-naming-canonicalization-migration / trace-exception-flow-no-throwsite | warn | C2-wave-conflict | Adjacent-order initiatives #6 (order 6) and #7 (order 7) share file `src/RoslynMcp.Host.Stdio/Tools/ExceptionFlowTools.cs`; planner Step 6 should have separated them. Execute must not co-wave. |
| parameter-naming-canonicalization-migration | info | C2-wave-conflict | Initiative #6 conflicts with 2 peers (#1 on AdvancedAnalysisTools.cs, #7 on ExceptionFlowTools.cs); expect serial scheduling. |
| test-discover-no-autopagination | info | anchor-stale | #2 confirmed obsolete: pagination (offset/limit + returnedCount/totalCount/hasMore) shipped at ValidationTools.cs:54-143; expected to transition obsolete at execute Step 5a — not a Rule violation. |
| parameter-naming-canonicalization-migration | info | 4 | #6 Approach describes the new lockstep test asserting only the projectFilter/scopeProjectFilter absence; the design note (items/parameter-naming-canonicalization-migration.md §(f)) requires 3 methods (also `character` and `packageName` assertions). Test under-specified vs design; still within the 1-file/Rule-4 budget. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes)

```json
{
  "edges": [
    { "a": 1, "b": 6, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs"] },
    { "a": 5, "b": 10, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs"] },
    { "a": 6, "b": 7, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ExceptionFlowTools.cs"] }
  ],
  "degrees": { "1": 1, "2": 0, "3": 0, "4": 0, "5": 1, "6": 2, "7": 1, "8": 0, "9": 0, "10": 1 },
  "zeroDegreeInitiatives": [2, 3, 4, 8, 9]
}
```

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs | #4 (restore-required-vs-build-conflation) | n/a — only one initiative touches a hotspot |
| src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs (+partials) | none | — |
| src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs | none | — |

No two adjacent-order initiatives both touch a hotspot file. (#5/#10 share WorkspaceTools.cs, which is explicitly NOT on the addenda hotspot list — captured as a conflict edge instead.)

## Stale-row spot check

All eight row-closing initiatives' backlog rows are present in backlog.md. #8 (compcache) and #9 (workspace-id) have empty `backlogRowsClosed` (partial batches, do not close parent rows).

## Recommended next step

- Outcome is `passed-with-warnings`: proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`, surfacing the #6↔#7 adjacent-order conflict in the run summary so the wave-scheduler keeps them serial. No remediation cycle required.
