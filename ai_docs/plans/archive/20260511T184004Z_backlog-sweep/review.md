# Plan review — 20260511T184004Z

**Plan reviewed:** `ai_docs/plans/20260511T184004Z_backlog-sweep/`
**Reviewer mode:** `/backlog-sweep:review`
**Outcome:** passed-with-warnings
**Initiative count:** 25 (24 pending + 1 deferred from outset)
**Findings:** block: 0, warn: 5, info: 3
**Anchor verification:** performed (spot-check 5 rows; all present)

## Summary

All 24 pending initiatives pass every hard rule: no Rule 1 bundles (all `rowsClosedCount: 1`), no Rule 3 violations (max 3 production files; tool-surface-only exemptions cited where claimed), no Rule 4 violations (max 2 test files), no Rule 5 violations (max 45K tokens against the 80K ceiling), and all `toolPolicy` values are `edit-only` which is correct given the addenda's self-edit caveat (no `*_apply` in main checkout). The warnings are scheduling reminders the planner already documented: pairs #5+#15 (both touching `WorkspaceManager.cs`) and #11+#22 (both touching `SyntaxService.cs`) must not land in the same parallel wave — `order` gaps are 10 and 11 respectively so accidental co-scheduling is prevented by the planner's sort discipline. A third soft conflict exists at #25 (degree 3 in `SymbolTools.cs`) but the three overlapping initiatives (#3, #16, #25) touch distinct methods within the file. The three info-level findings are cosmetic.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| #5 `parallel-fanout-auto-reload-timeout-floor` | warn | C2 wave-conflict | Shares `WorkspaceExecutionGate.cs` + `WorkspaceManager.cs` with #15. Orders 5 and 15 are non-adjacent (gap=10); hotspot scheduling rule satisfied. Confirm executor does NOT place in same parallel wave. |
| #15 `workspace-reloaded-during-call-conflates-notfound` | warn | C2 wave-conflict | Same pair as above. |
| #11 `get-syntax-tree-maxtotalbytes-not-enforced` | warn | C2 wave-conflict | Shares `SyntaxService.cs` + `SyntaxTools.cs` with #22. Orders 11 and 22 non-adjacent (gap=11). Confirm not in same parallel wave. |
| #22 `get-syntax-tree-range-truncates-at-statement` | warn | C2 wave-conflict | Same pair as above. |
| #25 `find-overrides-interface-root-empty` | warn | C2 wave-conflict | Degree 3 in `SymbolTools.cs` (shares with #3, #16, #25) plus conditional share of `ConsumerAnalysisService.cs` with #10. Each initiative touches distinct methods; serial mode is safe. Parallel mode: don't co-schedule with #3 or #16. |
| #8 `file-lock-aware-prompt-validation-guidance` | info | anchor-stale | Scope field contains an inline self-correction mid-sentence (citing the deleted `maintainer-overlay.md` then retracting). Executor must treat as `[stale anchor]` and rewrite to canonical `full.md` path. Planner notes + Risks already flag this. |
| #5 (cosmetic) | info | encoding artifact | CHANGELOG entry draft contains a stray "北" character (`CHANGELOG entry (北 draft)`). Cosmetic; executor should normalize when writing the fragment. |
| #25 (soft conflict) | info | C2 wave-conflict (conditional) | Scope says "possibly `ConsumerAnalysisService.cs`" — conditional file overlap with #10. Orders 10 and 25 non-adjacent (gap=15); low parallel-wave risk regardless. |

## Conflict graph

```json
{
  "edges": [
    { "a": 1,  "b": 18, "sharedFiles": ["ProjectMutationTools.cs"] },
    { "a": 3,  "b": 16, "sharedFiles": ["SymbolTools.cs"] },
    { "a": 3,  "b": 25, "sharedFiles": ["SymbolTools.cs"] },
    { "a": 5,  "b": 15, "sharedFiles": ["WorkspaceExecutionGate.cs", "WorkspaceManager.cs"] },
    { "a": 7,  "b": 8,  "sharedFiles": ["skills/mcp-server-surface-test/prompts/full.md"] },
    { "a": 10, "b": 25, "sharedFiles": ["ConsumerAnalysisService.cs (conditional)"] },
    { "a": 11, "b": 22, "sharedFiles": ["SyntaxService.cs", "SyntaxTools.cs"] },
    { "a": 16, "b": 25, "sharedFiles": ["SymbolTools.cs"] }
  ],
  "degrees": {
    "1": 1, "3": 2, "5": 1, "7": 1, "8": 1, "10": 1,
    "11": 1, "15": 1, "16": 2, "18": 1, "22": 1, "25": 3
  },
  "zero_degree_initiatives": [2, 4, 6, 9, 12, 13, 17, 19, 20, 21, 23, 24]
}
```

**Planner-flagged pairs confirmed:** #5+#15 (`WorkspaceManager.cs`), #11+#22 (`SyntaxService.cs`). **Additional pairs surfaced by graph:** #1+#18 (`ProjectMutationTools.cs`), #7+#8 (`full.md`), #3+#16+#25 (triangle in `SymbolTools.cs`).

## Hotspot scheduling

| Hotspot | Initiatives touching it | Orders | Adjacent? |
|---|---|---|---|
| `WorkspaceManager.cs` | #5, #15 | 5, 15 | No (gap=10) — OK |
| `ScaffoldingService.cs` (2521 LOC) | #17 | 17 | Single — no conflict |
| `ServerSurfaceCatalog.*` partials | (none) | — | OK |
| `ServiceCollectionExtensions.cs` | (none) | — | OK |

All addenda-listed hotspots pass the adjacency check.

## Stale-row spot check

| Row id | Present in backlog.md |
|---|---|
| `apply-project-mutation-not-registered-revert` | YES |
| `parallel-fanout-auto-reload-timeout-floor` | YES |
| `get-syntax-tree-maxtotalbytes-not-enforced` | YES |
| `reconcile-backlog-vs-github-issues` | YES |
| `find-overrides-interface-root-empty` | YES |

All spot-checked rows present.

## Recommended next step

- **Proceed to `/backlog-sweep:execute`.** All hard rules pass; the warnings are scheduling-discipline reminders the planner already noted in `state.json.notes`. The executor's parallel-mode wave-batching MUST use the conflict graph above to avoid co-scheduling pairs #5/#15, #11/#22, and #1/#18.
- For the #8 anchor staleness: executor should treat as `[stale anchor]` per the planner's flag and rewrite to the canonical `skills/mcp-server-surface-test/prompts/full.md` path.
- For the #5 cosmetic encoding artifact: executor normalizes when emitting the `changelog.d/parallel-fanout-auto-reload-timeout-floor.md` fragment.
