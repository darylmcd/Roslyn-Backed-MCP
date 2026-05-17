# Plan review — 2026-05-17T02:56:47Z (cycle 0)

**Plan reviewed:** C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260517T025647Z_backlog-sweep/
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 4 pending (1 unactionable; deepener errored)
**Findings:** block: 0, warn: 1, info: 3
**Anchor verification:** performed

## Summary

Plan is sound for the three actionable initiatives (1–3). All anchors verified live: `AnalysisTools.cs:436` confirms the bare `{count, items}` envelope; `find_type_usages` at `:403` confirms the collect-then-paginate template Init 1 will mirror; `IsolatedWorkspaceTestBase.CreateIsolatedWorkspaceCopy` and `TestBase.GetOrLoadWorkspaceIdAsync` both resolve as cited. Conflict graph computes to zero edges (matches orchestrator); no hotspot adjacency. The single notable issue is Initiative 4 (`tool-surface-pagination-or-tool-sets`): the deepener returned `status: error` because neither trigger has fired, but the planner kept the row in `state.json.initiatives` with `status: "pending"` and all contract fields set to `null` (toolPolicy, productionFilesTouched, testFilesAdded, estimatedContextTokens). The plan body explicitly recommends formal defer-section move and instructs "do not execute," yet did not action this — the executor will hit nulls. Initiative 3 sits exactly at the Rule 4 add-cap (3 new test files) AND modifies 1 existing test file, totaling 4 test-file touches; the plan's reading that this is "within the spirit and letter of Rule 4" is defensible because the cap text is `add ≤ 3 (or extend ≤ 3)`, but worth flagging.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| tool-surface-pagination-or-tool-sets | warn | planner-discipline | Initiative has `status: "pending"` but `productionFilesTouched`, `testFilesAdded`, `estimatedContextTokens`, and `toolPolicy` are all null because deepener errored. Plan body says "do not execute" and recommends formal Defer-section move but did not action it. Executor will attempt to vet this initiative against Rules 3/4/5 and hit nulls. Recommend the orchestrator transition this initiative's `status` to `deferred` (with reason: "Deepener trigger conditions not met; track-only row") before Phase F, OR exclude from the initiative array. |
| tool-surface-pagination-or-tool-sets | info | 3b | `toolPolicy: null` — consistent with deepener-errored state but flagged for completeness. |
| test-suite-expanded-surface-class-split | info | 4 | `testFilesAdded: 3` (at cap) AND modifies 1 existing file (`ExpandedSurfaceIntegrationTests.cs`); Scope acknowledges "Total test file changes: 4 (1 modified + 3 added)." Rule 4 reads "≤ 3 new (or ≤ 3 extended)" — plan's interpretation that both arms apply additively is defensible but worth surfacing. Executor should verify the boundary holds. |
| test-suite-fixture-reuse-cohesion-and-validate-git | info | naming | State.json records `testFilesAdded: 0` because no NEW test files are created; the 3 affected files are modifications. This matches Rule 4 (≤ 3 extended). Field semantics are consistent — flagging only to note that `testFilesAdded` does not capture the 3-file modification scope. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes — exact match, both report zero edges across all four initiatives.)

```json
{
  "edges": [],
  "degrees": { "1": 0, "2": 0, "3": 0, "4": 0 },
  "zeroDegreeInitiatives": [1, 2, 3, 4]
}
```

No two initiatives share any production or test file. Init 1 touches `AnalysisTools.cs` + `SemanticGrepServiceTests.cs`; Init 2 touches 3 IsolatedWorkspaceTestBase-conversion test files; Init 3 touches one existing + 3 new `ExpandedSurfaceIntegrationTests_*` test files; Init 4 has no scope at all.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (+ partials) | none | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | none | n/a |

No hotspot-adjacency findings. None of the 4 initiatives touch addenda-listed hotspot files.

## Stale-row spot check

| Row id | Present? |
|---|---|
| compact-semantic-grep-pagination | yes (line 48 of backlog.md) |
| test-suite-fixture-reuse-cohesion-and-validate-git | yes (line 55) |
| test-suite-expanded-surface-class-split | yes (line 56) |
| tool-surface-pagination-or-tool-sets | yes (line 66) |

All 4 rows present and unchanged from planning snapshot.

## Anchor verification

| Anchor | Cited at | Verified |
|---|---|---|
| `AnalysisTools.cs:436` (SemanticGrep, bare `{count, items}` envelope) | Init 1 Diagnosis | yes (matches line 437) |
| `AnalysisTools.cs:403` (FindTypeUsages collect-then-paginate template) | Init 1 Diagnosis | yes (matches lines 402–417) |
| `IsolatedWorkspaceTestBase.CreateIsolatedWorkspaceCopy()` | Init 2 Diagnosis | yes (file present, method at line 5) |
| `TestBase.GetOrLoadWorkspaceIdAsync(SampleSolutionPath, ct)` | Init 2 Diagnosis | yes (file present, method at line 257) |
| `ExpandedSurfaceIntegrationTests.cs` (683 lines) | Init 3 Diagnosis | yes (file present, 682 lines — off by 1, negligible) |

## Recommended next step

- **passed-with-warnings.** Proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`.
- **Pre-execute action recommended:** the orchestrator should transition Initiative 4 (`tool-surface-pagination-or-tool-sets`) to `status: "deferred"` with reason "Deepener trigger conditions not met; track-only row per backlog wording" — OR exclude it from the initiative array entirely — so the executor's Step 5a re-vet doesn't choke on null contract fields. The plan body already endorses this outcome but did not action it.
- Initiatives 1, 2, 3 are ready to ship as 3 parallel-eligible waves (zero conflict-graph edges → can parallel-batch all three; serial mode also fine given small initiative count).
