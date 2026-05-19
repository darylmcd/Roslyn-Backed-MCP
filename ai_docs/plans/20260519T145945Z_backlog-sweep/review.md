# Plan review — 20260519T145945Z (cycle 0)

**Plan reviewed:** C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260519T145945Z_backlog-sweep/
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 6 pending
**Findings:** block: 0, warn: 1, info: 6
**Anchor verification:** performed (spot-checked initiatives 1, 2, 3, 4, 6 directly via Read; init 5 callers via Grep)

## Summary

Six P3 single-row initiatives from the 2026-05-16 self-audit. All zero-degree on the conflict graph (no shared production files), none touch hotspots, all use `edit-only` tool policy with file counts well within Rule 3 (1-2 files per initiative). All six backlog rows still exist. Anchor verification confirmed every cited file:line resolves to the described construct; five of six initiatives correctly acknowledge stale backlog anchors and rewrite Diagnosis to the real fix sites. One Rule 5b warn on initiative 3 (BREAKING DTO-shape change with `fanoutEstimate: null` despite cross-cutting public-API churn). Six info findings: four anchor-stale acknowledgments already surfaced by the plan (good signal-to-reviewer) plus three counting-convention info notes where state.json's `testFilesAdded` is recorded as 0 while the Scope field says "1 test file extended" — Rule 4 is not breached either way (cap is on count, not naming).

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| find-type-mutations-single-scope-misses-compound-io | warn | 5b | Approach renames `MutatingMemberDto.MutationScope` (string) to `MutationScopes` (`IReadOnlyList<string>`) — explicit BREAKING wire-format change to a public DTO field. fanoutEstimate is null. Cross-checked: only 1 consumer file (test file) references `.MutationScope` today, so the executor's surface remains bounded, but the planner should have probed and recorded `fanoutEstimate: 2` (1 test consumer + the DTO declaration) per Rule 5b's "refactor-shaped initiative" trigger. |
| find-type-mutations-single-scope-misses-compound-io | info | rule-4-counting | state.json testFilesAdded=0 but Scope says "Test files modified (1)" — extending an existing test file should still count as 1 under Rule 4 semantics ("≤3 new OR ≤3 extended"). |
| format-document-preview-empty-diff-instead-of-noop | info | rule-4-counting | state.json testFilesAdded=0 but Scope says "Test files modified (1)". Same convention concern; Rule 4 cap not threatened. |
| callers-callees-previewtext-asymmetry | info | rule-4-counting | state.json testFilesAdded=0 but Scope says "Test files modified: 1". Same convention concern; Rule 4 cap not threatened. |
| workspace-changes-atomic-batch-split-without-batchid | info | anchor-stale | Backlog row cited `WorkspaceChangeLedger.cs` (does not exist). Plan correctly identifies `EditService.cs:577-579` as the real fix site — acknowledged in stanza Diagnosis. Heads-up surfaces upstream for the executor. |
| find-type-mutations-single-scope-misses-compound-io | info | anchor-stale | Backlog cited `MutationAnalysisService.cs:87` (unrelated string interpolation). Plan correctly identifies `ClassifyMethodMutationScope` at line 713. Acknowledged in stanza. |
| format-document-preview-empty-diff-instead-of-noop | info | anchor-stale | Backlog cited `FormatService.cs` (does not exist). Plan correctly identifies `DiffGenerator.cs:38-40` + `SolutionDiffHelper.cs`. Acknowledged in stanza. |
| callers-callees-previewtext-asymmetry | info | anchor-stale | Backlog cited `CallersCalleesService.cs` (consolidated into `SymbolRelationshipService.cs`). Acknowledged in stanza. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes)

```json
{
  "edges": [],
  "degrees": { "1": 0, "2": 0, "3": 0, "4": 0, "5": 0, "6": 0 },
  "zeroDegreeInitiatives": [1, 2, 3, 4, 5, 6]
}
```

Reviewer rebuilt the edge set from each initiative's Scope field. Pairwise intersection of production-file sets is empty for every pair. Matches orchestrator's `conflictGraph` exactly.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (+ partials) | none | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | none | n/a |

None of the six initiatives touches a hotspot file. The wave rule (≤1 hotspot per wave) does not bind. All six are safely parallelizable.

## Stale-row spot check

| Row id | Present? |
|---|---|
| workspace-changes-atomic-batch-split-without-batchid | yes (Low §62) |
| validate-workspace-changetracker-no-disk-reconcile-after-git-checkout | yes (Low §60) |
| find-type-mutations-single-scope-misses-compound-io | yes (Low §63) |
| analyze-control-flow-partial-slice-warning-on-full-method | yes (Low §65) |
| format-document-preview-empty-diff-instead-of-noop | yes (Low §61) |
| callers-callees-previewtext-asymmetry | yes (Low §64) |

No stale-row findings.

## Recommended next step

- Outcome is `passed-with-warnings`. Proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`. Surface the Rule 5b warn on initiative 3 in the run summary so the executor is aware that the planner skipped the fanout probe on a BREAKING DTO-shape change. The actual real-world fanout is bounded (one test consumer touched), so this is genuinely warn-not-block, but the executor should not assume zero-impact callers without re-verifying that no integration test or external SDK consumes `MutatingMemberDto.MutationScope` by name beyond the two assertions already noted.
