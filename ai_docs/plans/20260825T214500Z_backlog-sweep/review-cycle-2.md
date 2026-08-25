# Plan review — 2026-08-25 (cycle 2)

**Plan reviewed:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260825T214500Z_backlog-sweep`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 2
**Outcome:** passed-with-warnings
**Initiative count:** 17 pending
**Findings:** block: 0, warn: 9, info: 9
**Anchor verification:** partial

## Summary

The cycle-1 remediation succeeded on exactly the thing it was sent to fix: all three Rule 5b blocks are gone. `preview-token-route-binding-editing-substrate`, `-multifile-codeaction` and `-fileops-fixall` now carry `fanoutEstimate` 3 / 4 / 4, each equal to its `productionFilesTouched`, with the probe method spelled out in Risks — no `null`-vs-recorded-probe contradiction remains. Block count fell 3 -> 0, so this is not an oscillating remediation and the plan clears the gate. Schema is 4 (accepted). All four cited parent backlog rows still exist. My independently rebuilt conflict graph is edge-for-edge identical to the orchestrator's (12 edges, same `sharedFiles`, same degrees).

What did NOT get fixed are the three cycle-1 warns, and I re-verified each against live `main` rather than inheriting the prior grade. (1) `ServerSurfaceCatalog.PreviewApplyRoutes` (`src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs:42-68`) still has 24 entries with **no** `preview_multi_file_edit` and **no** `fix_all_preview` — so initiative 2.01, which registers both those kinds against order 1's catalog-derived apply-route half and order 1's "every non-Unspecified member resolves to BOTH" pin test, still cannot go green at 3 files; order 1's self-contained-pair fallback does not rescue it, because its coherence assertion has no catalog entry to agree with. Fixing it pulls in `ServerSurfaceCatalog.cs` — a 4th file and an addenda hotspot. (2) and (3) `ToolDispatch.ApplyByTokenAsync` still declares `string? applyRoute = null` at `ToolDispatch.cs:133`, order 1 still deletes it, and 2.02 / 2.03 still instruct the executor to add `applyRoute:` named arguments at 2 and 4 call sites respectively — on a post-order-1 base (guaranteed by their transitive `dependsOn`) that is a certain CS1739. These stay `warn` rather than `block`: the executor eats a compile error it can trivially resolve by dropping the argument, and each stanza's own Validation runs `compile_check` per edit. But they are cheap textual fixes the remediator left on the table.

Also still open from cycle 1 and carried forward verbatim on unchanged initiatives: order 5's `dependsOn` still names the retired `preview-token-route-binding-editing-family`, which means order 5 can land and close the parent row while 2.01/2.02/2.03 are unshipped — the single most consequential warn in this review for closeout correctness. One new `info` this cycle (reviewer-derived, not a regression): 2.01 has conflict-degree 4 and cycle 1 omitted its degree finding.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| preview-token-route-binding-editing-substrate | warn | 3 | UNRESOLVED since cycle 1. Live read: `ServerSurfaceCatalog.PreviewApplyRoutes` (`ServerSurfaceCatalog.cs:42-68`, 24 entries) has no `preview_multi_file_edit` / `fix_all_preview` entry. 2.01's MultiFileEdit + FixAll members red order 1's pin test unless it also edits `ServerSurfaceCatalog.cs` (4 files, addenda hotspot). The order-1 fallback pair-map does not rescue it — no catalog entry to agree with. Its Approach also registers `(previewTool, applyRoute)` PAIRS into a map order 1 defines as kind->previewTool only. |
| preview-token-route-binding-multifile-codeaction | warn | 3b | UNRESOLVED since cycle 1. `ToolDispatch.cs:133` still declares `string? applyRoute = null`; order 1 deletes it. 2.02 Approach steps (2)/(4) still add `applyRoute:` named args at `MultiFileEditTools.cs:110` and `CodeActionTools.cs:85` — CS1739 on a post-order-1 base. |
| preview-token-route-binding-fileops-fixall | warn | 3b | UNRESOLVED since cycle 1. Same defect at four verified call sites: `FileOperationTools.cs:58/95/139` and `FixAllTools.cs:47`. |
| preview-token-route-binding-editing-substrate | warn | C2-wave-conflict | Adjacent-order 1 and 2.01 share `ToolDispatch.cs`; Step 6 should have separated them (`dependsOn` serializes, but the wave picker still sees adjacency). |
| preview-token-route-binding-bulk-family | warn | dependsOn-dangling | Order 5's `dependsOn` still names the retired `preview-token-route-binding-editing-family`; an absent dep counts as SATISFIED, so order 5 lands first and closes `preview-token-apply-route-binding-remaining-families` while 2.01/2.02/2.03 are unshipped. Re-point at the three children or move the row closure. |
| preview-token-route-binding-extraction-family | warn | 3 | Carried forward: Scope declares 5 files (4+1=5) but its own probe counts 6 including "the centralized map file"; order 1 lands that map inside `ToolDispatch.cs`, which this child's negative space forbids. |
| preview-token-route-binding-orchestration-family | warn | C2-wave-conflict | Adjacent-order 3 and 4 share `PreviewKind.cs`. |
| preview-token-route-binding-bulk-family | warn | C2-wave-conflict | Adjacent-order 4 and 5 share `PreviewKind.cs` and `ToolDispatch.cs`. |
| shipped-skills-prefix-batch-c2 | warn | C2-wave-conflict | Adjacent-order 6 and 7 share `eng/banned-skill-markers.json` plus the same ratchet test file. |
| preview-token-route-map-centralization | info | C2-wave-conflict | Order 1 conflicts with 4 peers (2.01, 4, 5, 14); expect serial scheduling. |
| preview-token-route-binding-editing-substrate | info | C2-wave-conflict | NEW this cycle (reviewer-derived, not a remediation regression): 2.01 has degree 4 (peers 1, 3, 4, 5) and no heroic-last hint. Cycle 1 omitted this node's degree info. |
| preview-token-route-binding-extraction-family | info | C2-wave-conflict | Order 3 conflicts with 3 peers (2.01, 4, 5) on `PreviewKind.cs`. |
| preview-token-route-binding-orchestration-family | info | C2-wave-conflict | Order 4 conflicts with 5 peers — highest-degree node in the graph. |
| preview-token-route-binding-bulk-family | info | C2-wave-conflict | Order 5 conflicts with 4 peers on `PreviewKind.cs` / `ToolDispatch.cs`. |
| preview-token-route-binding-orchestration-family | info | C2-wave-conflict | 2.01 and 4 BOTH add `PreviewKind.FileCreate`; 2.03 and 4 BOTH tag `FileOperationService.cs:53` with FileCreate. Assign FileCreate to exactly one child. |
| preview-token-route-binding-bulk-family | info | 3 | Gate-forced-companion citation complete (CS0117 verified — `PreviewKind.cs` has 5 concrete members) and 6 <= 7, but half the cited gate (order 1's pin test) does not exist on main yet. |
| param-description-dedupe-refactoring-core | info | C2-wave-conflict | 14 and 15 both extend `ParameterDescriptionCanonicalizationTests.cs`; test files are excluded from the graph so the rebase hazard is unmodeled. |
| param-description-dedupe-validation-signature | info | C2-wave-conflict | Shares that same test file with 14; 13 mints an assembly-wide guard over the same phrasings. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: **yes** — 12 edges, identical `sharedFiles` and degrees.)

```json
{
  "edges": [
    { "a": 1, "b": 2.01, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] },
    { "a": 1, "b": 4, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] },
    { "a": 1, "b": 5, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] },
    { "a": 1, "b": 14, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs"] },
    { "a": 2.01, "b": 3, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs"] },
    { "a": 2.01, "b": 4, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs", "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] },
    { "a": 2.01, "b": 5, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs", "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] },
    { "a": 2.03, "b": 4, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/FileOperationService.cs"] },
    { "a": 3, "b": 4, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs"] },
    { "a": 3, "b": 5, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs"] },
    { "a": 4, "b": 5, "sharedFiles": ["src/RoslynMcp.Core/Models/PreviewKind.cs", "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs"] },
    { "a": 6, "b": 7, "sharedFiles": ["eng/banned-skill-markers.json"] }
  ],
  "degrees": { "1": 4, "2.01": 4, "2.02": 0, "2.03": 1, "3": 3, "4": 5, "5": 4, "6": 1, "7": 1, "8": 0, "9": 0, "10": 0, "11": 0, "12": 0, "13": 0, "14": 1, "15": 0 },
  "zeroDegreeInitiatives": [2.02, 8, 9, 10, 11, 12, 13, 15]
}
```

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (+ partials) | none declared (order 1 READS but does not edit; 2.01 may be forced to edit it — see its Rule 3 warn) | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` | none (order 15 touches `ParameterObjectTools.cs`, not the service) | n/a |

No hotspot-adjacency finding. Caveat: resolving 2.01's Rule 3 warn by editing `ServerSurfaceCatalog.cs` would make 2.01 hotspot-touching and engage the <=1-per-wave rule against orders 3/4/5.

## Stale-row spot check

| Row id | Present? |
|---|---|
| `preview-token-apply-route-binding-remaining-families` | yes (`ai_docs/backlog.md:89`) |
| `shipped-skills-hardcode-bare-roslyn-tool-prefix` | yes (`ai_docs/backlog.md:58`) |
| `method-description-diet` | yes (`ai_docs/backlog.md:59`) |
| `param-description-dedupe` | yes (`ai_docs/backlog.md:132`) |

## Anchor verification (partial)

Verified live on `main` for orders 1, 2.01, 2.02, 2.03: `ToolDispatch.cs:133` (`string? applyRoute = null`), `:183`, `:185-188`, `:196-200`, `:207-215`, `:221-229`; `ServerSurfaceCatalog.cs:42-68` + `TryGetCompatibleApplyRoute:242-246`; `PreviewKind.cs` (5 concrete members + `Unspecified = 0`); `IPreviewStore.cs:23/31/37/57-64`; `PreviewStore.cs:68/87/94/102/115/142`; `MultiFileEditTools.cs:110`; `CodeActionTools.cs:85`; `FileOperationTools.cs:58/95/139`; `FixAllTools.cs:47`; `EditService.cs:302`; `CodeActionService.cs:152`; `FixAllService.cs:207`; `FileOperationService.cs:53/106/156`. Zero stale anchors. Orders 5-15 were not re-anchored this cycle (unchanged, cleared in prior cycles).

## Recommended next step

`passed-with-warnings` — proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`, surfacing the nine warnings in the run summary. Two are worth a cheap pre-execute edit rather than discovering them at run time: (a) re-point order 5's `dependsOn` at the three split children so the parent row cannot close early, and (b) delete the `applyRoute:` named arguments from 2.02's and 2.03's Approach text so the executor does not follow a stanza into a CS1739.