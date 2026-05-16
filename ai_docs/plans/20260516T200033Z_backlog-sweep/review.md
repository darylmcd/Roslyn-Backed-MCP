# Plan review — 2026-05-16T20:00:33Z (cycle 0)

**Plan reviewed:** ai_docs/plans/20260516T200033Z_backlog-sweep/
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 15 pending
**Findings:** block: 0, warn: 2, info: 2
**Anchor verification:** performed (first 3 + cross-checks on initiatives 7, 14)

## Summary

The plan is structurally sound. Every initiative passes Rules 1, 3, 4, 5, and 5b. Tool-surface-only exemptions are cited correctly where claimed (initiatives 2, 5, 9). Anchor staleness is honestly disclosed inline (initiatives 9, 10, 15 explicitly call out the corrected anchors in their Diagnosis fields rather than hiding the divergence).

Two warnings: (a) adjacent-order initiatives 1 and 2 share `AdvancedAnalysisTools.cs` — the planner's Step 6 sort did not separate them; the parallel-mode executor will be forced into a serial wave for those two. (b) Initiative 3 omits a `fanoutEstimate` despite the Approach describing surgical refactors of two prompt methods — defensible given the local scope, but the rule expects an explicit value.

Two informational items flag (i) initiative 4's Risks field admitting "executor may need to add an overload in a second service file" without committing it to scope, and (ii) initiative 6's conflict-degree on `RoslynPrompts.RefactoringWorkflows.cs` with initiative 3 (non-adjacent — orchestrator already spaced them 3 slots apart).

Conflict graph matches the orchestrator's edge set on production files; the orchestrator additionally listed `tests/RoslynMcp.Tests/PromptSmokeTests.cs` on the (3,6) edge — that test file IS shared by both initiatives but the canonical rule operates on production-file Scope lists. Net effect on scheduling is identical.

Recommend proceeding to Phase F handoff-readiness with the wave-conflict surfaced in the run summary.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| (1, 2) | warn | C2-wave-conflict | Adjacent-order initiatives 1 and 2 share `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs`; planner Step 6 should have separated them so the parallel executor can co-schedule. |
| get-prompt-text-side-effects-in-rendering | warn | 5b | Planner skipped fanout probe on a refactor-shaped initiative (Approach: "Refactor `DebugTestFailure` … Refactor `SecurityReview`"). Risk is bounded (scope is two named methods in two named files) but rule expects an explicit `fanoutEstimate` for refactor-flavored Approach text. |
| symbol-refactor-preview-auto-applies-without-explicit-apply-call | info | 3 | Risks field explicitly states "executor may need to add an overload in a second service file (still within Rule 3 if limited to 1-2 additional files, but needs verification at execution time)." `productionFilesTouched=1` is honest but the plan invites a 1-file expansion at execute time. Acceptable since 2 files still fits the cap; flagging so executor budgets accordingly. |
| guided-extract-interface-prompt-payload-cap | info | C2-wave-conflict | Conflict edge with initiative 3 on `RoslynPrompts.RefactoringWorkflows.cs` (different methods). Orchestrator placed init 3 at order 3 and init 6 at order 6 — already correctly separated; degree=1 each. Informational only. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes on production-file edges. Orchestrator additionally lists `tests/RoslynMcp.Tests/PromptSmokeTests.cs` on the (3,6) edge — extends past the canonical production-file scope but does not change scheduling outcomes.)

```json
{
  "edges": [
    { "a": 1, "b": 2, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs"] },
    { "a": 3, "b": 6, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs"] }
  ],
  "degrees": {
    "1": 1, "2": 1, "3": 1, "4": 0, "5": 0, "6": 1, "7": 0, "8": 0,
    "9": 0, "10": 0, "11": 0, "12": 0, "13": 0, "14": 0, "15": 0
  },
  "zeroDegreeInitiatives": [4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15]
}
```

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | 7 only | n/a (single toucher) |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (any partial) | none | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |

No hotspot adjacency conflicts. Initiative 14 explicitly avoids the `WorkspaceManager.cs` hotspot by taking the classifier-enhancement path instead of the new-tool path — correctly noted in its Diagnosis as eliminating the wave conflict with initiative 7.

## Stale-row spot check

All 15 backlog rows still present in `ai_docs/backlog.md` snapshot. No stale-row conditions.

## Recommended next step

Plan passes with warnings. Proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`. Surface the (1,2) wave-conflict and the initiative-3 missing-fanout warnings in the run summary so the executor can either (a) reorder 1 and 2 across waves manually before parallel dispatch, or (b) run them serially.
