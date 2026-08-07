
# Plan review — 2026-08-07 (cycle 0)

**Plan reviewed:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260807T133441Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 15 pending
**Findings:** block: 0, warn: 5, info: 9
**Anchor verification:** performed (initiatives 1–3 in full; spot checks on 5, 8, 15)

> Reviewed against the DEEPENED in-memory plan supplied by the orchestrator. `plan.md` / `state.json` on disk are a stale pre-deepening skeleton (`productionFilesTouched: null` throughout, empty `conflictGraph`) and were NOT the review target. `state.json.schemaVersion` is 4 — gate passes.

## Summary

No block findings. Every initiative clears the hard Rules 1–5 gates: max `productionFilesTouched` is 4 (initiatives 2, 9, 15, all at the cap with no exemption claimed and none needed), max `testFilesAdded` is 2, max `estimatedContextTokens` is 55K against an 80K ceiling, every initiative carries an explicit `toolPolicy: "edit-only"`, and no initiative closes more than one row (so Rule 1 is n/a throughout). Rule 5b produces no block: no initiative sets `fanoutOversize`, and no recorded `fanoutEstimate` exceeds `productionFilesTouched + 2` — initiative 15 sits exactly on the boundary (6 vs 4+2). My independently rebuilt conflict graph is byte-identical to the orchestrator's.

The five warnings cluster into two themes. **Fanout-probe honesty (initiatives 4 and 8):** initiative 4 skipped the probe entirely on a three-signature widening and pushed caller verification into the executor's lap; initiative 8 records `fanoutEstimate: 2` while explicitly stating in Risks that the actual probe returned ~17 sites — a value chosen to describe intent rather than measurement, which defeats the field's purpose even though its 2-file scope is genuinely correct. **Row-closure discipline (initiatives 3 and 9):** initiative 9 closes its row while its own Risks admit the item's Acceptance criterion 1 is unmet (4 of 6 required surfaces converted), whereas initiatives 2 and 15 face the identical partial-slice situation and correctly keep their rows open — the plan is internally inconsistent about when a partial slice may close a row. Initiative 3 is worse in kind: it closes `core-dto-location-quartet-consolidation-primary` while instructing that the `...-secondary` row's `deps` cell (verified at `ai_docs/backlog.md:59` to name the primary) be left unchanged so secondary "stays blocked" — but closing primary *satisfies* that dependency and unblocks secondary before Stage 1's code has landed, directly contradicting the initiative's own Acceptance bullet 5. The remaining warning is scheduling: adjacent-order initiatives 7 and 8 share `CompileCheckService.cs`, and initiatives 7 and 12 both rewrite the same `BuildHint` method — Step 6 should have spread these apart.

All 15 `backlogRowsClosed` ids still exist in `ai_docs/backlog.md` (no stale rows). Only one initiative (5) touches an addenda-listed hotspot, so there is no hotspot adjacency problem.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| composite-apply-undo-encoding-still-lossy | warn | 5b | `fanoutEstimate: null` on a three-signature widening (`CollectChangesFromSolutionDiffAsync` / `CollectChangesFromDiskWalkAsync` / `PersistRevertChangesAsync`); Risks defer caller verification to the executor instead of probing at plan time. |
| project-filter-helper-whitespace-normalize | warn | 5b | `fanoutEstimate: 2` is self-declared NOT to be the probe result ("reflects files touched (2), not the much larger use-site count"). Independent check: 15 files under `src/` reference `FilterProjects(` — plan's "16 other service files" is an over-count. 2-file scope is correct; the recorded value is not a measurement, and ~14 tools change behavior on one new test. |
| compile-check-zero-resolution-false-success | warn | C2-wave-conflict | Adjacent-order 7 and 8 share `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs`. Aggravated: 8 deletes line 42 of that file while 7 edits `ResolveProjectScope`/`BuildHint`, and 12 also edits `BuildHint`. |
| single-source-overallstatus-verdict-table | warn | backlog-sync | Closes the row while Risks (a) admit AC1 unmet. Item Acceptance requires 6 surfaces (2 skill files + 2 tool Descriptions + 2 prompt surfaces); plan converts 4, defers `skills/refactor-loop/SKILL.md` and `ValidationBundleTools.cs`. Initiatives 2 and 15 keep their rows open under identical conditions. |
| core-dto-location-quartet-consolidation-primary | warn | backlog-sync | Closes `...-primary` yet instructs that `...-secondary`'s `deps` cell (verified `ai_docs/backlog.md:59` = `core-dto-location-quartet-consolidation-primary`) be left unchanged so it "stays blocked". Closing primary satisfies the dep and unblocks secondary before Stage 1 lands — contradicts the initiative's own Acceptance bullet 5. Retarget `deps` to the new Stage-1 row. |
| lru-eviction-gate-layer-execution | info | 5b | `fanoutEstimate: 0` with `productionFilesTouched: 3` — not a probe result; Risks describe a Grep finding 8 `new WorkspaceManager(` sites + 7 test files. Does not trip the block, but `:execute`'s scheduler reads this field. |
| lru-eviction-gate-layer-execution | info | scope | Gate wired via optional ctor param defaulting to `null`; the safety guarantee holds only under production DI + the one rewritten test. The other 7 test constructions retain the unguarded `Close()` path, so eviction characterizations in tests no longer mirror production. |
| compile-check-zero-resolution-false-success | info | C2-wave-conflict | Order 7 conflicts with 2 peers (8, 12) on `CompileCheckService.cs`; expect serial scheduling. |
| project-filter-helper-whitespace-normalize | info | C2-wave-conflict | Order 8 conflicts with 2 peers (7, 12) on `CompileCheckService.cs`; expect serial scheduling. |
| compile-check-restorehint-empty-not-null | info | C2-wave-conflict | Order 12 conflicts with 2 peers (7, 8). Note 7 and 12 both rewrite `BuildHint` specifically — a same-method textual conflict, not merely a same-file one. |
| compilation-cache-adoption-read-side | info | 3 | `TestServiceContainer.cs` counted under `testFilesAdded`; the addenda's "Mandatory addenda (counted in file budget)" table names that exact file as counting toward `productionFilesTouched`. Immaterial (2 of 4) but diverges from the addenda contract. |
| compilation-cache-adoption-read-side | info | anchor-stale | Diagnosis says "474 lines total"; actual is 473. Risks cites "394/395" where only 394 carries a call. Both real sites (394, 415) verified fresh; the self-flagged stale `:515` item-doc citation is correctly identified. |
| deep-review-shape-list-single-source | info | 5b | `fanoutEstimate: 6` vs `productionFilesTouched: 4` — exactly on the Rule 5b boundary (6 > 6 is false). Deliberate partial slice, row correctly left open. Minor: Diagnosis says "7 independent copies" but enumerates 6 sites. |
| workspace-validation-dead-path-and-duplicated-default | info | dependsOn | Adjacent-order 13 and 14 are semantically coupled on `ValidationServiceOptions.GitStatusTimeout` with zero file overlap (13 reads it via `new ValidationServiceOptions().GitStatusTimeout`; 14 rewrites its XML doc). Parallel-safe today, invisible to the graph if 14 ever moves the field. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: **yes** — exact match on all 4 edges, all 15 degrees, and the zero-degree set.)

```json
{
  "edges": [
    { "a": 6, "b": 13, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs"] },
    { "a": 7, "b": 8, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/CompileCheckService.cs"] },
    { "a": 7, "b": 12, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/CompileCheckService.cs"] },
    { "a": 8, "b": 12, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/CompileCheckService.cs"] }
  ],
  "degrees": { "1": 0, "2": 0, "3": 0, "4": 0, "5": 0, "6": 1, "7": 2, "8": 2, "9": 0, "10": 0, "11": 0, "12": 2, "13": 1, "14": 0, "15": 0 },
  "zeroDegreeInitiatives": [1, 2, 3, 4, 5, 9, 10, 11, 14, 15]
}
```

Near-misses checked and correctly excluded: initiative 10 (`src/RoslynMcp.Core/Services/IWorkspaceValidationService.cs`) vs 6/13 (`src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`) — distinct files, no edge. Initiative 5's `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` is unique to it. Initiative 9's `skills/**` paths and initiative 15's `.claude/skills/**` paths both resolve on disk and do not overlap.

## Hotspot scheduling

Addenda hotspots: `ServerSurfaceCatalog.cs` (+ partials), `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs`, `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`.

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | 5 (lru-eviction-gate-layer-execution) | n/a — sole toucher |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (+ partials) | none | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |

No hotspot adjacency violation. Note initiative 5 also touches `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`, which is a **different** file from the addenda's Host.Stdio hotspot of the same leaf name — verified both paths exist; no hotspot hit.

## Stale-row spot check

| Row id | Present? |
|---|---|
| compilation-cache-adoption-read-side | yes |
| file-snapshot-capture-helper-consolidation | yes |
| core-dto-location-quartet-consolidation-primary | yes |
| composite-apply-undo-encoding-still-lossy | yes |
| lru-eviction-gate-layer-execution | yes |
| validate-workspace-compiler-gate-scope-to-category | yes |
| compile-check-zero-resolution-false-success | yes |
| project-filter-helper-whitespace-normalize | yes |
| single-source-overallstatus-verdict-table | yes |
| workspace-validation-service-overallstatus-xmldoc-inverted | yes |
| compile-check-restorehint-empty-not-null | yes |
| workspace-validation-dead-path-and-duplicated-default | yes |
| git-status-timeout-docs-scope-correction | yes |
| tool-di-resolution-leak-pin-compile-check-test-run | yes |
| deep-review-shape-list-single-source | yes |

No stale rows. (`core-dto-location-quartet-consolidation-primary` also appears in the `deps` cell of `...-secondary` at `ai_docs/backlog.md:59` — that second hit is the basis of the initiative-3 warning above, not a duplicate row.)

## Recommended next step

Proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`, surfacing all five warnings in the run summary. Two are cheap to fix now and worth fixing before execute, since both change what the executor writes to the backlog:

1. **Initiative 3** — change the Backlog-sync instruction from "leave `...-secondary`'s `deps` cell unchanged" to "retarget `...-secondary`'s `deps` to the newly-filed Stage-1 row." As written the plan unblocks a row whose prerequisite has not shipped.
2. **Initiative 9** — either drop the row closure (matching initiatives 2 and 15) or convert the remaining 2 surfaces into scope. Closing a row with a self-admitted unmet acceptance criterion loses the work if the recommended follow-up row is never filed.

Scheduling note for `:execute` Step 4: initiatives 7, 8, and 12 form a triangle on `CompileCheckService.cs` and must land in three different waves; 7 and 12 additionally conflict inside the same method (`BuildHint`), so ordering between them should be explicit rather than left to rebase.
