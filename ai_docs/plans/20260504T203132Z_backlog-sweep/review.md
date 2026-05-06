# Plan review — 2026-05-04T20:42:00Z
<!-- purpose: Review findings for the 20260504T203132Z backlog sweep plan. -->
<!-- scope: in-repo -->

**Plan reviewed:** `ai_docs/plans/20260504T203132Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:review
**Outcome:** passed-with-warnings
**Initiative count:** 6
**Findings:** block: 0, warn: 3, info: 2
**Anchor verification:** performed (spot-checked initiatives #1/#2/#3 against live source)

## Summary

Plan covers the 6 open backlog rows produced by today's multi-session retro. Schema is current (v2). All row ids resolve. No Rule 1 / Rule 4 / Rule 5 violations. Two structural-unit-exemption initiatives (#3, #6) under-count `productionFilesTouched` by omitting the addenda-mandatory `TestBase.cs` DI registration. Initiatives #5 and #6 are adjacent in `order` and both touch the `ServerSurfaceCatalog.*.cs` hotspot family — the addenda's case study (PRs #258/#260) shows two catalog-touching PRs in one parallel wave force a serial rebase, so reordering is recommended. No blocking findings — executor can proceed; warnings should be reconciled either by re-planning or by accepting them with the recommended mitigations below.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| `workspace-drift-check-tool` (#3) | warn | 3 (mandatory addenda) | `productionFilesTouched: 5` in state.json under-counts. Addenda lists `tests/RoslynMcp.Tests/TestBase.cs` as mandatory test-fixture DI registration for any new `[McpServerTool]` — that file must be counted in the file budget per addenda § *"Mandatory addenda (counted in file budget)"*. Honest count: 6 production files. Still within Rule 3 structural-unit exemption (4 units) so not a block; correct the count for review honesty. |
| `validate-locator-preflight-tool` (#6) | warn | 3 (mandatory addenda) | Same as #3 — `productionFilesTouched: 5` should be 6 (TestBase.cs DI registration). 4 structural units intact. |
| `find-references-project-filter` (#5) ↔ `validate-locator-preflight-tool` (#6) | warn | hotspot adjacency | Adjacent `order: 5` and `order: 6`. #5 touches `ServerSurfaceCatalog.Analysis.cs` (description-only edit, flagged in plan's Risks). #6 touches `ServerSurfaceCatalog.Symbols.cs` for new-tool registration. Both files are members of the ServerSurfaceCatalog partial family which the addenda lists as a single hotspot. Case study (PRs #258, #260, 2026-04-18): *"Two catalog-touching PRs in one wave forced second-to-merge into UNSTABLE → re-validate. ≤1 catalog-touching per wave."* In serial mode the impact is small; in parallel-mode wave assignment, place #5 and #6 in different waves, OR swap #5 and #4's order (`navigation-tools-misnamed-locator-error` → no hotspot) so #6 sits next to a non-hotspot initiative. |
| `inv-arg-envelope-schema-hint` (#1) | info | hotspot read | Plan flags catalog access as read-only consumption — *"Catalog touch is read-only lookup helper consumption; aim to avoid adding new public catalog API."* Read-only access against `ServerSurfaceCatalog.cs` does not produce parallel-merge conflicts, so this is not a hotspot adjacency in the merge-friction sense. Listed in `hotspotFiles` for visibility; advisory only — if the executor confirms zero new public surface added, downgrade to none. |
| `find-references-project-filter` (#5) | info | anchor freshness | Plan's Diagnosis already corrects the backlog row's stale `SymbolReferenceService.cs` anchor to live `ReferenceService.cs`. Executor briefed via Diagnosis text. No action required. |

## Rule-by-rule walk

**Rule 1 (bundling):** All 6 initiatives have `rowsClosedCount: 1`. No bundling. ✓

**Rule 3 (file count):**
- #1: 2 prod files ✓
- #2: 0 prod files (investigation/report only) ✓
- #3: 5 declared, 6 actual (TestBase.cs uncounted) — within 4-unit structural-unit exemption ✓ but under-counted (warn)
- #4: 3 prod files ✓
- #5: 4 prod files (at cap) ✓
- #6: 5 declared, 6 actual (TestBase.cs uncounted) — within 4-unit structural-unit exemption ✓ but under-counted (warn)

**Rule 3b (toolPolicy):** All `edit-only`. No solution-wide rename/extract work in scope. ✓

**Rule 4 (test budget):** Max 2 test files (#5 extends two existing fixtures); all under 3. ✓

**Rule 5 (context budget):** Max 55K (#3, #6); all under 80K. ✓

**Hotspot scheduling:** see warn finding above. #1 catalog touch is read-only (info). #3 (catalog + WorkspaceManager) at order 3. #5 (catalog desc-only) and #6 (catalog new-tool) at orders 5 and 6 — adjacent (warn).

**Anchor freshness (spot-check #1/#2/#3):**
- #1 `ToolErrorHandler.cs` line 49 (ArgumentException → InvalidArgument) and lines 264–288 (parameter-shape envelopes): verified via Grep before plan write. ✓
- #2 `ApplyWithVerifyTool.cs`, `EditService.cs`: verified via `ls`. ✓
- #3 `WorkspaceManager.cs`, `WorkspaceManagerEvictionTests.cs`: verified via `ls`. ✓

## Recommended next step

`passed-with-warnings`. Two reconciliation paths:

1. **Re-plan** with `/backlog-sweep:plan plan-id=ai_docs/plans/20260504T203132Z_backlog-sweep/` to (a) bump `productionFilesTouched` from 5 → 6 on #3 and #6, and (b) swap orders 4 and 5 so the lineup becomes #4 (no hotspot, order 4) → #5 (description-only catalog touch, order 5) → #6 (new-tool catalog, order 6) is no longer adjacent to two catalog-touching initiatives. Wait — that doesn't help; #5 → #6 is the adjacent pair. Better: insert `apply-with-verify-false-positive-audit` (#2) between #5 and #6, or swap #4 and #6's order so #6 sits next to #4 (no hotspot). Cleanest fix: reorder to **#1 → #4 → #2 → #3 → #5 → #6** (separates #3 and #6 by 3 positions and breaks #5/#6 adjacency by interleaving #5 mid-sequence, but this is over-engineered). Simplest: **reorder #5 ↔ #4** so the sequence becomes `#1 #2 #3 #5 #4 #6` — #5 catalog-touch is now adjacent to #3 (catalog+ws hotspot), still adjacent. Cleanest after consideration: **swap #6's `order` with #4's** → final order `#1 #2 #3 #6 #5 #4` — but #3 and #6 then adjacent. There is **no swap that fully separates the three catalog-touchers (#3, #5, #6) without 4 positions between hotspots**. Given only #3 and #6 are full new-tool registrations and #5 is description-only, accept the #5/#6 adjacency as low-risk in serial mode and resolve by **assigning #5 and #6 to different parallel waves at execute-time** — add a `waveHint` to state.json rather than changing `order`.

2. **Accept warnings.** In serial-mode execution (one PR at a time, merged before next), hotspot adjacency is harmless. The mandatory-addenda undercounts on #3/#6 are honesty issues, not budget violations. Executor proceeds at user discretion.

Recommendation: **Option 2** — accept warnings. Update state.json to bump `productionFilesTouched` on #3/#6 for honesty (purely a documentation fix; doesn't change budget compliance). Add a `waveHint` on #5 and #6 marking them for separate parallel-mode waves.

If executing in serial mode: no change needed.
