# Plan review — 2026-08-25 (cycle 0)

**Plan reviewed:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260825T151721Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 15 pending
**Findings:** block: 0, warn: 5, info: 7
**Anchor verification:** partial

## Summary

No block findings. All 15 initiatives are within Rule 3 (max observed: 4 production files, no exemption claimed anywhere — correctly, since none is needed), Rule 4 (max 3 test files, only initiative 2 at the cap), Rule 5 (max 75000 vs the 80000 cap), and Rule 3b (every initiative carries an explicit toolPolicy; only initiative 10 is preview-then-apply). Rule 5b passes mechanically everywhere, though initiatives 1 and 2 record design-contingent fanout numbers (4 and 3) that mask real fan-ins of 26 and 15 files respectively — those numbers are only true while the optional-overload / default-interface-member design holds, and both stanzas say so explicitly, so this is recorded as info rather than a block. The reviewer-rebuilt conflict graph exactly matches the orchestrator's: one edge, 7-11 on `WorkspaceTools.cs`, non-adjacent in the order-sorted sequence, both degree 1. No addenda hotspot file (`ServerSurfaceCatalog.*`, `ServiceCollectionExtensions.cs`, `WorkspaceManager.cs`, `ParameterObjectService.cs`) is touched by any initiative, so no hotspot-adjacency finding fires. All seven referenced backlog row ids still exist. The five warnings cluster in two places: (a) the shipped-skills batches cite two *different, non-reproducible* md5 baselines for the same canonical connectivity-precheck block, and both make that hash an acceptance gate — an executor following either literally will fail its own validation; and (b) the heroic promotion-scorecard refresh sits at order 10 of 15 despite `scheduleHint: heroic-last`, at 94% of the Rule 5 cap, with a self-declared fallback path that blows the budget. Independent verification confirmed the plan's harder factual claims: 13 unswept skill files (not the item file's "~10"), `Directory.Build.props` at 4.0.0 vs the scorecard's frozen 1.38.1/2026-05-16, `.claude/skills/promote-tier/SKILL.md` existing while the item's anchor line 9 still reads `skills/promote-tier/`, and `tests/RoslynMcp.Tests/WorkspaceToolsTests.cs` being absent as initiative 11 states. The deepening is unusually well-grounded; the warnings are verification-evidence defects and a scheduling slip, not scope-rule breaches.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| shipped-skills-prefix-sweep-batch-a | warn | anchor-stale | Cited canonical md5 `036c6bba…` unreproducible; live section is 2992 chars / `ce562a64…`. The hash is an acceptance gate. |
| shipped-skills-prefix-sweep-batch-b | warn | anchor-stale | Cites `34bd5d2a…` (canonical) and `5b4704b0…` (old); ground truth `ce562a64…` / `5aac15fa…`. Contradicts sibling batch-a for the same block. |
| shipped-skills-prefix-genericity-guard-assertion | warn | backlog-sync | Closes the parent row with 5 of 13 files still unswept behind an allowlist amnesty; batch-c exists only as prose. |
| promotion-tier-scorecard-refresh-execution | warn | 5 | 75000 = 94% of cap, models orchestration only for a 90-180 min / 250+ call audit; stated fallback blows the budget. |
| promotion-tier-scorecard-refresh-execution | warn | scheduling | `heroic-last` at order 10 of 15 — five non-heroic initiatives scheduled after it, against Step 6. |
| preview-token-store-provenance-contract | info | 5b | fanoutEstimate=4 vs a measured 26-file fan-in held at zero edits only by the no-break design. |
| preview-token-apply-route-binding | info | 5b | fanoutEstimate=3 vs 15 production callers of `ApplyByTokenAsync`; optional-parameter design is load-bearing. |
| preview-token-apply-route-binding | info | 4 | testFilesAdded=3 at the cap with a stated re-scope trigger (abstract vs default interface member). |
| shipped-skills-prefix-sweep-batch-b | info | anchor-stale | "Copy from `analyze/SKILL.md:21-34`" is a fixed line span valid only for analyze; extraction must be heading-to-heading. |
| shipped-skills-prefix-genericity-guard-assertion | info | 3b | Release-managed-file PreToolUse sentinel required for `eng/verify-skills-are-generic.ps1`. |
| promotion-tier-scorecard-refresh-execution | info | 3 | productionFilesTouched=3 but productionFiles lists 2 (third is timestamp-named, unenumerable). |
| promotion-tier-scorecard-refresh-execution | info | 3b | preview-then-apply against the addenda PreToolUse `*_apply` block — known-handleable. |
| format-changed-file-gate | info | 5b | `dotnet format --include` exit-code scoping unverified; core gate behavior undetermined at plan time. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes)

```json
{
  "edges": [ { "a": 7, "b": 11, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs"] } ],
  "degrees": { "1": 0, "2": 0, "3": 0, "4": 0, "5": 0, "6": 0, "7": 1, "8": 0, "9": 0, "10": 0, "11": 1, "12": 0, "13": 0, "14": 0, "15": 0 },
  "zeroDegreeInitiatives": [1, 2, 3, 4, 5, 6, 8, 9, 10, 12, 13, 14, 15]
}
```

Orders 7 and 11 are not consecutive in the order-sorted sequence and both have degree 1, so neither the adjacent-order warn nor the degree-2 info fires. Note that `dependsOn` edges (2→1, 5→3+4, 13→12) are build-order, not file-overlap, and correctly do not appear here.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.*` | none | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` | none | n/a |

Initiative 8 touches `ParameterObjectTools.cs` (Host.Stdio surface), not the `ParameterObjectService.cs` hotspot; initiatives 6/7/9/14/15 each explicitly assert catalog non-involvement, and the `[McpToolMetadata].Summary`-vs-`[Description]` distinction they rely on is the reason no catalog edit is forced. No hotspot adjacency exists.

## Stale-row spot check

| Row id | Present? |
|---|---|
| preview-token-apply-route-provenance | yes |
| shipped-skills-hardcode-bare-roslyn-tool-prefix | yes |
| workspace-source-text-projection-extraction | yes |
| method-description-diet | yes |
| param-description-dedupe | yes |
| promotion-tier-execution-batch | yes |
| repository-dotnet-format-baseline-partition | yes |

## Anchor verification detail (partial — first 3 initiatives plus targeted spot checks)

- Initiative 1: `IPreviewStore.PeekChangedPaths` confirmed as a default interface member with the exact "null means skip, never verified" contract; `PreviewEntry` record confirmed at `PreviewStore.cs:238-249` with `MaxRedeemableVersion` positional field. Anchors fresh.
- Initiative 2: `ToolDispatch.ApplyByTokenAsync` confirmed at lines 114-133 with `RevalidateChangedPathsAsync` inside `gate.RunWriteAsync` — the guard-insertion point the Approach names exists as described. Anchors fresh.
- Initiative 3: `skills/exception-audit/SKILL.md` confirmed carrying the legacy bare-prefix precheck at the cited lines; the swept canonical block confirmed byte-identical across the four PR #1233 files. The unswept set is confirmed as exactly 13 files, validating the plan's correction of the item file's "~10". Only the cited md5 is wrong (see findings).
- Spot checks beyond budget: initiative 5's char counts (2967 / 936 / 1150) are internally consistent under a heading-excluded body measurement (live: 2992 / 961 / 1175 heading-included) — correct, no finding. Initiative 10's claims verified: `Directory.Build.props` `<Version>4.0.0</Version>`, scorecard frozen at `1.38.1` / `2026-05-16T06:25:47Z`, `.claude/skills/promote-tier/SKILL.md` present while the item's anchor line 9 still reads `skills/promote-tier/`. Initiative 11's claim that `tests/RoslynMcp.Tests/WorkspaceToolsTests.cs` does not exist is confirmed, and `WorkspaceToolsIntegrationTests.cs` does.

## Recommended next step

`passed-with-warnings` — proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`, surfacing the five warnings in the run summary. Before execution, the cheapest high-value fixes are: replace the two fabricated md5 baselines in initiatives 3 and 4 with a regeneration instruction ("hash the heading-to-next-heading section and confirm one distinct value across all 8 files") rather than a literal hash, and either re-order initiative 10 after 15 or set it `deferred` from the outset per the addenda's heroic-last case study.