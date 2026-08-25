# Plan review — 20260825T214500Z (cycle 1)

**Plan reviewed:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260825T214500Z_backlog-sweep`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 1
**Outcome:** failed
**Initiative count:** 17 pending
**Findings:** block: 3, warn: 9, info: 8
**Anchor verification:** partial

## Summary

The cycle-0 remediation of `preview-token-route-binding-editing-family` worked on the merits: the 11-file / 85K / fanout-oversize parent is gone, replaced by three children at 3/4/4 production files and 32K/45K/50K context — all three cycle-0 blocks are genuinely resolved and the file sets are honest. But all three children were authored with the same mechanical defect: each Risks field states a fanout probe RAN and records a concrete count ("3 production files", "4 production files, enumerated by walking each of the … routes … Probe ran, count recorded"), while `stateFields.fanoutEstimate` is `null`. That is the exact shape Rule 5b's consumer-side backstop blocks on, so the block count stays at 3 — flat, not rising, and one remediation pass that fills in `fanoutEstimate: 3/4/4` clears all three. Two substantive warns also came in with the split, both verified live against `main`: (a) `editing-substrate` under-counts its file set, because order 1 sources the apply-route half from `ServerSurfaceCatalog.PreviewApplyRoutes` and pins map exhaustiveness, yet that 24-entry dictionary has no `preview_multi_file_edit` or `fix_all_preview` key — so two of its six new `PreviewKind` members red order 1's own pin test unless the child also edits the hotspot catalog file; and (b) both binding children instruct the executor to pass an `applyRoute:` named argument that order 1 deletes from `ApplyByTokenAsync` (`ToolDispatch.cs:133`), a guaranteed CS1739 on the post-order-1 base they are required to branch from. Separately, order 5's `dependsOn` still names the retired `preview-token-route-binding-editing-family`; an absent dep counts as satisfied, and the three children are `heroic-last`, so order 5 will land first and close the parent backlog row while three of its children are unshipped — fix that before execute even though it grades as a warn. The conflict graph I rebuilt from the deepened Scope lists matches the orchestrator's edge-for-edge (12 edges, identical degrees, identical zero-degree set).

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| preview-token-route-binding-editing-substrate | block | 5b | NEW since cycle 0. Risks records a real probe ("3 production files … Probe ran, count recorded") but `fanoutEstimate: null`. Record 3. |
| preview-token-route-binding-multifile-codeaction | block | 5b | NEW since cycle 0. Risks records "4 production files, enumerated by walking each of the two routes …" but `fanoutEstimate: null`. Record 4. |
| preview-token-route-binding-fileops-fixall | block | 5b | NEW since cycle 0. Risks records "4 production files, enumerated by walking each of the four routes …" but `fanoutEstimate: null`. Record 4. |
| preview-token-route-binding-editing-substrate | warn | 3 | Under-counts: `ServerSurfaceCatalog.PreviewApplyRoutes` (`ServerSurfaceCatalog.cs:42-67`, read live) lacks `preview_multi_file_edit` and `fix_all_preview`, so MultiFileEdit/FixAll red order 1's exhaustiveness pin unless the catalog (an addenda hotspot) is edited too → 4 files, hotspot wave rule engaged. Approach also registers (previewTool, applyRoute) PAIRS into order 1's kind→previewTool-only map. |
| preview-token-route-binding-multifile-codeaction | warn | 3b | Approach adds `applyRoute:` named args to `ApplyByTokenAsync`; order 1 deletes that parameter (`ToolDispatch.cs:133`) — guaranteed CS1739 on the required base. |
| preview-token-route-binding-fileops-fixall | warn | 3b | Same defect at four call sites (`create_file_apply` / `delete_file_apply` / `move_file_apply` / `fix_all_apply`). |
| preview-token-route-binding-bulk-family | warn | dependsOn-dangling | `dependsOn` names the retired `preview-token-route-binding-editing-family`; absent deps count as satisfied and the replacement children are `heroic-last`, so order 5 lands first and closes the parent row prematurely. |
| preview-token-route-binding-extraction-family | warn | 3 | Carried forward: gate-forced arithmetic 4+1=5 vs its own 6-file probe including "the centralized map file"; order 1 lands that map ToolDispatch-private, which this child's negative space forbids. |
| preview-token-route-binding-editing-substrate | warn | C2-wave-conflict | Adjacent orders 1 and 2.01 share `ToolDispatch.cs`. |
| preview-token-route-binding-orchestration-family | warn | C2-wave-conflict | Adjacent orders 3 and 4 share `PreviewKind.cs`. |
| preview-token-route-binding-bulk-family | warn | C2-wave-conflict | Adjacent orders 4 and 5 share `PreviewKind.cs` + `ToolDispatch.cs`. |
| shipped-skills-prefix-batch-c2 | warn | C2-wave-conflict | Adjacent orders 6 and 7 share `eng/banned-skill-markers.json` plus the ratchet test file. |
| preview-token-route-map-centralization | info | C2-wave-conflict | Degree 4 (2.01, 4, 5, 14); expect serial scheduling. |
| preview-token-route-binding-extraction-family | info | C2-wave-conflict | Degree 3 (2.01, 4, 5) on `PreviewKind.cs`. |
| preview-token-route-binding-orchestration-family | info | C2-wave-conflict | Degree 5 — highest in the graph (1, 2.01, 2.03, 3, 5). |
| preview-token-route-binding-bulk-family | info | C2-wave-conflict | Degree 4 (1, 2.01, 3, 4). |
| preview-token-route-binding-orchestration-family | info | C2-wave-conflict | Semantic duplicate survives the split: 2.01 and 4 both add `PreviewKind.FileCreate`; 2.03 and 4 both tag `FileOperationService.cs:53`. |
| preview-token-route-binding-bulk-family | info | 3 | Carried forward: gate-forced-companion citation complete (CS0117 verified, total 6 ≤ 7) but half the cited gate (order 1's pin test) does not exist on main yet. |
| param-description-dedupe-refactoring-core | info | C2-wave-conflict | Carried forward: 14 and 15 both extend `ParameterDescriptionCanonicalizationTests.cs`; unmodeled (test files excluded). |
| param-description-dedupe-validation-signature | info | C2-wave-conflict | Carried forward: same shared test file plus initiative 13's new assembly-wide guard. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes — 12 edges, identical degrees and zero-degree set)

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
| `ServerSurfaceCatalog.cs` (+ partials) | none as declared (order 1 READS it; 2.01 will be forced to EDIT it — see finding) | n/a today; re-check after 2.01 remediation |
| `ServiceCollectionExtensions.cs` | none | no |
| `WorkspaceManager.cs` | none | no |
| `ParameterObjectService.cs` | none (order 15 touches `ParameterObjectTools.cs`, not the service) | no |

## Stale-row spot check

| Row id | Present? |
|---|---|
| `preview-token-apply-route-binding-remaining-families` | yes (`ai_docs/backlog.md:89`) |
| `shipped-skills-hardcode-bare-roslyn-tool-prefix` | yes (`ai_docs/backlog.md:58`) |
| `method-description-diet` | yes (`ai_docs/backlog.md:59`) |
| `param-description-dedupe` | yes (`ai_docs/backlog.md:132`) |

## Anchor verification

Partial — verified live on `main` for orders 1, 2.01, 2.02, 2.03: `ToolDispatch.cs` `PreviewToolFor`/`ApplyRouteFor` switches and the optional `applyRoute` parameter at `:133`; `PreviewKind.cs` exactly five concrete members; `IPreviewStore.cs` kind-carrying `changes`-shaped overload + `PeekKind` default body; `EditService.cs:302`, `CodeActionService.cs:152`, `FixAllService.cs:207` on the untagged 4-arg `Store`; `FileOperationService.cs:53/106/156` on the `changes`-shaped 5-arg `Store`; unbound `ApplyByTokenAsync` call sites at `MultiFileEditTools.cs:110`, `CodeActionTools.cs:85`, `FileOperationTools.cs:58/95/139`, `FixAllTools.cs:47`. No stale anchors found. Orders 3–15 were not re-verified this cycle.

## Recommended next step

Failed — Phase E should spawn remediators for the three block findings. All three are the same one-field fix (`fanoutEstimate: 3 / 4 / 4`). While a remediator is in each stanza, also fix in the same pass: (1) `editing-substrate` must either add `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` to its file set for the missing `preview_multi_file_edit` / `fix_all_preview` route entries (4 files, hotspot wave rule) or order 1 must not source the apply half from the catalog; (2) drop the `applyRoute:` named arguments from `multifile-codeaction` and `fileops-fixall` Approaches; (3) re-point order 5's `dependsOn` at the three replacement children so the parent backlog row cannot close early; (4) de-duplicate `PreviewKind.FileCreate` / `FileOperationService.cs:53` between 2.01/2.03 and order 4.