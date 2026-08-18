# Plan review — 20260818T211226Z (cycle 0)

**Plan reviewed:** C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260818T211226Z_backlog-sweep
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 6 pending
**Findings:** block: 0, warn: 6, info: 9
**Anchor verification:** performed

## Summary

No block findings. All six initiatives are single-row (Rule 1 not engaged), 1-2 production files (Rule 3 cap 4, no exemption claimed or needed), 1-2 test files (Rule 4 cap 3), 35K-45K estimated tokens (Rule 5 cap 80K), and all carry an explicit `edit-only` toolPolicy consistent with their Approach (no solution-wide symbolic refactor described; the repo's PreToolUse `*_apply` hook is therefore a non-issue). The dominant structural fact is that **every one of the six initiatives edits `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs`** — the conflict graph is a complete K6, degree 5 on every node, zero parallelizable initiatives. Consequently all five adjacent-order pairs carry a conflict edge; Step 6 ordering could not have avoided this (there is no non-conflicting ordering of a complete graph), so the five wave-conflict warnings are informational-in-effect: the correct response is to run this plan strictly serially, not to reorder. That file is not in the addenda hotspot table but behaves as one for this sweep; consider adding it. The one substantive rule warning is initiative 3 (`parameter-object-value-type-mutation-semantics`), which records `fanoutEstimate: null` while its Approach performs a private-method signature change and asserts a caller count — the planner should record the measured count rather than declaring the probe skipped. Two initiatives (2 and 3) self-declare deviations from their backlog rows' written acceptance criteria (runtime-trace regression substituted with a structural assertion; the lowering-with-write-back acceptance arm deliberately not taken) — both are defensible and disclosed, but they are operator-visible judgment calls, flagged as info. All six `backlogRowsClosed` ids still exist in `ai_docs/backlog.md`. Anchor spot-checks on the first three initiatives resolved cleanly against the live tree.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| parameter-object-value-type-mutation-semantics | warn | 5b | `fanoutEstimate: null` while the Approach changes a private method signature (`ClassifyVariableRequiredUse` gains an `IParameterSymbol`) and asserts "its only caller is line 392"; Risks declares the probe skipped. Record the measured count. |
| parameter-object-target-method-contract-validation | warn | C2-wave-conflict | Adjacent-order 1 and 2 share `ParameterObjectService.cs`. |
| parameter-object-callsite-semantic-argument-binding | warn | C2-wave-conflict | Adjacent-order 2 and 3 share `ParameterObjectService.cs`. |
| parameter-object-value-type-mutation-semantics | warn | C2-wave-conflict | Adjacent-order 3 and 4 share `ParameterObjectService.cs`. |
| parameter-object-generic-dto-type-validity | warn | C2-wave-conflict | Adjacent-order 4 and 5 share `ParameterObjectService.cs`. |
| parameter-object-dto-reference-qualification | warn | C2-wave-conflict | Adjacent-order 5 and 6 share `ParameterObjectService.cs`. |
| (all six) | info | C2-wave-conflict | Conflict-degree 5 on every initiative; no `heroic-last` hints. Fully serial scheduling required. |
| parameter-object-callsite-semantic-argument-binding | info | acceptance-deviation | Structural post-apply source-text assertion substituted for the amendment's "observe a runtime trace" regression. |
| parameter-object-value-type-mutation-semantics | info | acceptance-deviation | Acceptance item 4 discharged via refusal + positive readonly-struct apply; lowering-with-write-back arm intentionally skipped. |
| parameter-object-target-method-contract-validation | info | 5b | Risks item (4) calls the service file "the third and last edit target" while Scope lists 2 files; the count (2) is correct, the prose is not. |
| parameter-object-dto-output-boundary-validation | info | anchor-stale | No stale anchors — all cited lines resolve; `ProjectRelativePathValidation.cs` correctly does not yet exist. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes — exact match, 15 edges, degrees 5/5/5/5/5/5, zero-degree set empty)

```json
{
  "edges": [
    {"a": 1, "b": 2, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 1, "b": 3, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 1, "b": 4, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 1, "b": 5, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 1, "b": 6, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 2, "b": 3, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 2, "b": 4, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 2, "b": 5, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 2, "b": 6, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 3, "b": 4, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 3, "b": 5, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 3, "b": 6, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 4, "b": 5, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 4, "b": 6, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]},
    {"a": 5, "b": 6, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs"]}
  ],
  "degrees": {"1": 5, "2": 5, "3": 5, "4": 5, "5": 5, "6": 5},
  "zeroDegreeInitiatives": []
}
```

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `ServerSurfaceCatalog*.cs` | none (initiative 1 explicitly checked `ServerSurfaceCatalog.Refactoring.cs:19` and requires no edit) | n/a |
| `ServiceCollectionExtensions.cs` | none | n/a |
| `WorkspaceManager.cs` | none | n/a |

No addenda-listed hotspot is touched. Note: `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` (1171 lines, 6/6 initiatives) is a de-facto hotspot for this sweep and should be added to the addenda hotspot table.

## Stale-row spot check

| Row id | Present? |
|---|---|
| parameter-object-target-method-contract-validation | yes (backlog.md:53) |
| parameter-object-callsite-semantic-argument-binding | yes (backlog.md:52) |
| parameter-object-value-type-mutation-semantics | yes (backlog.md:54) |
| parameter-object-generic-dto-type-validity | yes (backlog.md:55) |
| parameter-object-dto-reference-qualification | yes (backlog.md:56) |
| parameter-object-dto-output-boundary-validation | yes (backlog.md:57) |

## Recommended next step

Proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`, surfacing the warnings in the run summary. Execute MUST run this plan serially — the conflict graph is complete, so no parallel wave larger than one initiative is legal. Two additional scheduling facts from the stanzas that the graph cannot see: the Low row `parameter-object-rewrite-planner-decomposition` depends on initiative 2 and must ship strictly after it, and the Medium row `parameter-object-declaration-metadata-preservation` shares the same file and must not be scheduled alongside any of these.