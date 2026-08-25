# Plan review — 2026-08-25 (cycle 0)

**Plan reviewed:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260825T214500Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** failed
**Initiative count:** 15 pending
**Findings:** block: 3, warn: 6, info: 8
**Anchor verification:** partial

## Summary

One initiative fails hard and everything else is structurally sound. `preview-token-route-binding-editing-family` (order 2) is over three separate ceilings at once — 11 production files against a Rule 3 cap of 4 (and against the gate-forced-companion ceiling of 7), 85,000 estimated tokens against the Rule 5 cap of 80,000, and a self-declared `fanoutOversize: true`. The stanza is honest about this: its own Scope says "NO Rule 3 exemption is claimed or claimable ... MUST be re-split before execution" and its Risks proposes a concrete zero-overlap 2a/2b partition. That candour does not downgrade the finding — the plan as written cannot execute, so this is three `block`s and the plan fails. The remediator should take the stanza's own recommended split: fold `PreviewKind.cs` + `ToolDispatch.cs` + `IPreviewStore.cs` into order 1, then cut 2a (`EditService`/`MultiFileEditTools`/`CodeActionService`/`CodeActionTools`) and 2b (`FileOperationService`/`FileOperationTools`/`FixAllService`/`FixAllTools`), and assign `PreviewKind.FileCreate` to exactly one child so it stops colliding with order 4.

The one non-mechanical `warn` is on `preview-token-route-binding-extraction-family` (order 3): it declares 5 files with gate-forced-companion arithmetic `4 + 1 = 5`, but its own fanout probe counts 6 must-touch files including "the centralized map file", and order 1's Approach explicitly lands that map as a `static readonly` field private to `ToolDispatch.cs`. So order 3's negative space and order 1's design are incompatible as authored — order 3 instructs the executor to STOP and request remediation in precisely that case. Either order 1 exposes the map Core-side (next to the enum), or order 3 must own a 6th file and gain the `1<->3` ToolDispatch conflict edge. Fix it at plan time, not at execute time.

The remaining warns are the five adjacent-order conflict pairs (1-2, 2-3, 3-4, 4-5, 6-7). All five are deliberate `dependsOn` chains rather than planner oversights, but Step 6's "don't give adjacent order values to conflicting initiatives" still fires and the wave picker will serialize them. Rule 1 is not engaged anywhere (no initiative closes more than one row), Rule 4 is clean (max 3, at cap on order 5), every initiative carries an explicit `edit-only` toolPolicy, and no initiative touches an addenda hotspot file — note that order 15's `ParameterObjectTools.cs` is the Host.Stdio wrapper, not the hotspot `Roslyn/Services/ParameterObjectService.cs`. The gate-forced-companion citations on orders 3 and 5 were spot-checked against live source: `PreviewKind` really does declare only five concrete members (`src/RoslynMcp.Core/Models/PreviewKind.cs`), so the CS0117 claim holds. My independently rebuilt conflict graph matches the orchestrator's edge-for-edge.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| preview-token-route-binding-editing-family | block | 3 | productionFilesTouched=11 — over the Rule 3 cap of 4 AND over the gate-forced-companion ceiling of 7; Scope itself states no exemption is claimable and the initiative MUST be re-split. |
| preview-token-route-binding-editing-family | block | 5 | estimatedContextTokens=85000 exceeds the Rule 5 cap of 80000 — split or accept explicit user override. |
| preview-token-route-binding-editing-family | block | 5b | Planner flagged fanoutOversize=true; scheduleHint is heroic-last but no valid Rule-3 exemption is (or can be) cited. |
| preview-token-route-binding-extraction-family | warn | 3 | Declares 5 files (`4+1=5`) but its own probe counts 6 including the map file; order 1 lands that map ToolDispatch-private, so the real count is 6 and the `1<->3` edge is missing. |
| preview-token-route-binding-editing-family | warn | C2-wave-conflict | Adjacent-order 1 and 2 share `ToolDispatch.cs`. |
| preview-token-route-binding-extraction-family | warn | C2-wave-conflict | Adjacent-order 2 and 3 share `PreviewKind.cs`. |
| preview-token-route-binding-orchestration-family | warn | C2-wave-conflict | Adjacent-order 3 and 4 share `PreviewKind.cs`. |
| preview-token-route-binding-bulk-family | warn | C2-wave-conflict | Adjacent-order 4 and 5 share `PreviewKind.cs` + `ToolDispatch.cs`. |
| shipped-skills-prefix-batch-c2 | warn | C2-wave-conflict | Adjacent-order 6 and 7 share `eng/banned-skill-markers.json` (plus the same ratchet test file). |
| preview-token-route-map-centralization | info | C2-wave-conflict | Degree 4 (peers 2, 4, 5, 14); expect serial scheduling. |
| preview-token-route-binding-extraction-family | info | C2-wave-conflict | Degree 3 (peers 2, 4, 5); expect serial scheduling. |
| preview-token-route-binding-orchestration-family | info | C2-wave-conflict | Degree 4 (peers 1, 2, 3, 5); expect serial scheduling. |
| preview-token-route-binding-bulk-family | info | C2-wave-conflict | Degree 4 (peers 1, 2, 3, 4); expect serial scheduling. |
| preview-token-route-binding-orchestration-family | info | C2-wave-conflict | Initiatives 2 and 4 BOTH add `PreviewKind.FileCreate` and BOTH tag `FileOperationService.cs:53` — semantic duplicate, not just file overlap. |
| preview-token-route-binding-bulk-family | info | 3 | Gate-forced-companion citation is complete and 6 <= 7, but half the cited gate (order 1's pin test) does not exist on main yet. |
| param-description-dedupe-refactoring-core | info | C2-wave-conflict | Shares `ParameterDescriptionCanonicalizationTests.cs` with order 15; unmodeled by the production-file graph. |
| param-description-dedupe-validation-signature | info | C2-wave-conflict | Same shared test file as order 14; order 13 also mints an assembly-wide guard over the same phrasings. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes — 11 edges, identical set)

```json
{
  "edges": [
    { "a": 1, "b": 2, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] },
    { "a": 1, "b": 4, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] },
    { "a": 1, "b": 5, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] },
    { "a": 1, "b": 14, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs"] },
    { "a": 2, "b": 3, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs"] },
    { "a": 2, "b": 4, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs", "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs", "src/RoslynMcp.Roslyn/Services/FileOperationService.cs"] },
    { "a": 2, "b": 5, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs", "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] },
    { "a": 3, "b": 4, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs"] },
    { "a": 3, "b": 5, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs"] },
    { "a": 4, "b": 5, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs", "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] },
    { "a": 6, "b": 7, "sharedFiles": ["eng/banned-skill-markers.json"] }
  ],
  "degrees": { "1": 4, "2": 4, "3": 3, "4": 4, "5": 4, "6": 1, "7": 1, "8": 0, "9": 0, "10": 0, "11": 0, "12": 0, "13": 0, "14": 1, "15": 0 },
  "zeroDegreeInitiatives": [8, 9, 10, 11, 12, 13, 15]
}
```

Latent edge not in the graph: if order 1 keeps the map private to `ToolDispatch.cs`, order 3 must edit that file too, adding `1<->3`, `2<->3` (ToolDispatch) and `3<->4` / `3<->5` (ToolDispatch) — turning orders 1-5 into a near-complete K5 on that one file. Resolve the seam before execute so the wave picker sees the truth.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (+ partials) | none (order 1 READS it, does not edit) | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` | none (order 15 touches `Tools/ParameterObjectTools.cs`, a different file) | n/a |

No hotspot-adjacency findings.

## Stale-row spot check

| Row id | Present? |
|---|---|
| `preview-token-apply-route-binding-remaining-families` | yes (backlog.md:89) |
| `shipped-skills-hardcode-bare-roslyn-tool-prefix` | yes (backlog.md:58) |
| `method-description-diet` (parent, not closed) | yes (backlog.md:59) |
| `param-description-dedupe` (parent, not closed) | yes (backlog.md:132) |
| `skill-prefix-guard-tests-assert-bcl-not-gate` (referenced, not closed) | yes (backlog.md:85) |

## Anchor freshness (first 3 initiatives, verified live on main)

| Anchor | Result |
|---|---|
| `ToolDispatch.cs:185-188` / `:196-200` / `:207-215` / `:221-229` | resolves — early return, throw, `PreviewToolFor`, `ApplyRouteFor` all at the cited lines |
| `RefactoringTools.cs` `applyRoute:` at 60/95/130/169/210 | resolves — all five literals present |
| `PreviewKind.cs` — 5 concrete members + `Unspecified` | resolves — confirms the CS0117 gate claims on orders 3 and 5 |
| `MultiFileEditTools.cs:110`, `FileOperationTools.cs:58/95/139`, `CodeActionTools.cs:85`, `FixAllTools.cs:47` | all resolve |
| `ExtractMethodTools.cs:51`, `InterfaceExtractionTools.cs:50`, `TypeExtractionTools.cs:55`, `TypeMoveTools.cs:54` | all resolve |
| `IPreviewStore.cs:23/31/37/57` + `PeekKind` default at `:157` | resolves |
| `ServerSurfaceCatalog.cs:42-69` `PreviewApplyRoutes` (24 entries, no `extract_interface_preview`) | resolves — order 3's bad-code observation is correct |

No stale anchors found. Verification depth: `partial` (first-three budget).

## Recommended next step

`failed` — Phase E should spawn a remediator for `preview-token-route-binding-editing-family` (three block findings, all resolvable by the stanza's own recommended 2a/2b re-split plus moving `PreviewKind.cs` / `ToolDispatch.cs` / `IPreviewStore.cs` into order 1) and should resolve the order-1/order-3 map-seam warn in the same cycle, since both remediation edits land in the same two stanzas.