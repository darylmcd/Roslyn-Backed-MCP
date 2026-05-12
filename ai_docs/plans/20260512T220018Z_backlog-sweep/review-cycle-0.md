# Plan review — 2026-05-12T22:00:18Z (cycle 0)

**Plan reviewed:** `ai_docs/plans/20260512T220018Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 5
**Findings:** block: 0, warn: 2, info: 2
**Anchor verification:** performed

## Summary

The plan is structurally sound. All 5 initiatives respect Rules 3, 4, and 5; toolPolicy is set explicitly on each (Rule 3b); the conflict graph is correctly empty (no two initiatives share any production file); and no two adjacent-order initiatives touch the addenda-listed hotspot files. Anchor spot-checks on `ToolErrorHandler.cs`, `AnalyzerInfoService.cs:42-43`, `FileWatcherService.cs:147-151`, `CompileCheckTools.cs:19`, and `ScaffoldingService.cs` (2776 lines) all resolve cleanly; the planner has already flagged the known stale LOC count on the scaffolding row. The two warnings concern mis-cited Rule 3 exemptions on initiatives 1 and 2 — the "tool-surface-only" exemption is being cited where it does not apply (Initiative 1's Approach modifies `Program.cs`, not a `Tools/*.cs` wrapper or `Models/*Dto.cs`; Initiative 2 edits a service implementation, not a tool wrapper). Both initiatives sit comfortably under Rule 3's 4-file hard cap regardless, so the mis-citations are documentation hygiene issues rather than budget violations. Initiative 4 is unusual but intentional: `backlogRowsClosed: []` reflects a wave-1-of-12 strategy where the master row stays open until wave 12; flagged as info.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| `compile-check-not-connected-raw-transport-error-envelope` | warn | 3 | Scope cites "Rule 3 exemption: tool-surface-only, 2 files" but Approach path (b) modifies `src/RoslynMcp.Host.Stdio/Program.cs` — the addenda's tool-surface-only exemption is specifically scoped to `Tools/{Tool}Tools.cs` + `Models/*Dto.cs`, not Program.cs transport-wrapping. The 2-file count is under the 4-file hard cap so no exemption is actually needed; the mis-citation should be corrected or removed. |
| `list-analyzers-totalrules-variance` | warn | 3 | Scope cites "Rule 3 exemption: tool-surface-only shape applies (fix is inside the registered tool's backing service implementation)" — the addenda restricts tool-surface-only to wrapper-layer edits, not Core/Roslyn service implementations. The single file (`AnalyzerInfoService.cs`) is well under the 4-file cap; the exemption is unnecessary and incorrectly invoked. |
| `skill-namespace-installed-as-bulk-frontmatter-migration` | info | 1 | `backlogRowsClosed: []` and `rowsClosedCount: 0` — intentional wave-1-of-12 with master row staying open until wave 12. Executor must add 11 spin-off rows at Step 7 sync per the plan's Backlog sync field. Verified the master row id exists at `backlog.md` line 71. |
| `scaffolding-service-split-by-scaffold-type` | info | anchor-stale | Plan self-flags anchor staleness (cited 2521 LOC, current 2776 LOC — reviewer confirmed). Method-entry line anchors (38, 92, 331, 458) not independently re-verified; executor should confirm in Step 4. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: **yes** — both show zero edges.)

```json
{
  "edges": [],
  "degrees": { "1": 0, "2": 0, "3": 0, "4": 0, "5": 0 },
  "zeroDegreeInitiatives": [1, 2, 3, 4, 5]
}
```

File sets per initiative (production only, excluding tests/CHANGELOG/backlog):

- Initiative 1: `ToolErrorHandler.cs`, `Program.cs`
- Initiative 2: `AnalyzerInfoService.cs`
- Initiative 3: `FileWatcherService.cs`
- Initiative 4: 4× `.claude/skills/<name>/SKILL.md`
- Initiative 5: `ScaffoldingService.cs` + 3 new partial files

No pairwise intersection. Zero-edge graph confirmed.

## Hotspot scheduling

| Hotspot | Initiatives touching | Adjacent? |
|---|---|---|
| `ServerSurfaceCatalog.cs` (+ partials) | none | n/a |
| `ServiceCollectionExtensions.cs` | none | n/a |
| `WorkspaceManager.cs` | none | n/a |

No hotspot adjacency issues.

## Stale-row spot check

| Row id | Present in backlog.md? |
|---|---|
| `compile-check-not-connected-raw-transport-error-envelope` | yes (line 60) |
| `list-analyzers-totalrules-variance` | yes (line 65) |
| `workspace-staleness-cross-workspace-contamination` | yes (line 66) |
| `skill-namespace-installed-as-bulk-frontmatter-migration` | yes (line 71) — master row, stays open |
| `scaffolding-service-split-by-scaffold-type` | yes (line 70) |

## Recommended next step

`passed-with-warnings` — orchestrator may proceed to Phase F (handoff-readiness) and then `/backlog-sweep:execute`. The two Rule 3 exemption mis-citations are documentation hygiene issues; file counts are under the hard cap in both cases. Consider removing the unnecessary exemption claims from Scope on initiatives 1 and 2 to avoid confusing future re-reads. Initiative 4's wave-1-of-12 shape is intentional but the executor must execute the spin-off-row obligation at Step 7.
