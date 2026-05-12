# Plan review — 2026-05-12T13:51:06Z (cycle 0)

**Plan reviewed:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260512T135106Z_backlog-sweep/`
**Reviewer mode:** `/backlog-sweep:prepare` (Phase D)
**Cycle:** 0
**Outcome:** failed
**Initiative count:** 22 (18 actionable: 1 obsolete, 3 deferred)
**Findings:** block: 3, warn: 4, info: 5
**Anchor verification:** performed (first 3 initiatives spot-checked, all anchors fresh)

## Summary

The plan is well-structured and honestly self-reports its problems via the deepener-notes pattern. Three block findings:

- (a) Initiative 17 (`skill-namespace-and-semantic-search-discoverability`) ships with `fanoutOversize: true` and `productionFilesTouched: 47` — well above Rule 3's 4-file cap with no applicable exemption. The planner annotated this with `scheduleHint: "heroic-last"` and explicit re-vet/split guidance, but Rule 5b spec mandates a block when `fanoutOversize: true` is set.
- (b) Initiative 19 (`filepaths-array-vs-stringified-tool-description-clarification`) reports `fanoutEstimate: 9` vs `productionFilesTouched: 4` — a delta of 5, exceeding the +2 threshold; the planner explicitly scoped 4 of 9 files with a spin-off-row note, which is honest design but trips Rule 5b's mechanical block.
- (c) The conflict-graph dependency: initiative 17's 46 SKILL.md bulk edit creates virtual conflict edges with 6 other initiatives (2/3/4/12/13/14); these are correctly captured but require strict serial-after-everything-else execution.

Anchor verification on initiatives 1–3 confirms the cited file:line references (`WorkspaceManager.cs:192-195`, `WorkspaceTools.cs:18/98`, `refactor/SKILL.md` line 18) are all live and accurate. Stale-row check: all 22 backlog row ids resolve. Conflict graph independently rebuilt matches the orchestrator's edge set exactly.

Recommended path: the orchestrator's Phase E remediation should either split #17 at intake (preferred — the planner's own Approach field outlines a 3-row split: infrastructure + 2 bulk-edit rows), or set `vettingOverride: true` to acknowledge the heroic-last schedule. For #19, the cleanest fix is to either (a) accept the explicit scoping with `vettingOverride`, or (b) reduce `fanoutEstimate` to 4 and document the remaining 5 as a follow-on row directly in state.json rather than via free-text in plan.md.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| `skill-namespace-and-semantic-search-discoverability` (#17) | block | 5b | `fanoutOversize: true` set by planner. Per Rule 5b: "Planner flagged fanout-oversize. Either split or accept heroic-last scheduling with explicit user override." |
| `skill-namespace-and-semantic-search-discoverability` (#17) | block | 3 | `productionFilesTouched: 47` exceeds Rule 3's 4-file cap with no applicable structural-unit or tool-surface exemption. Plan's own Scope field states: "Rule 3 violation confirmed: 46 production files + 1 new eng script = 47 touched; cap is 4. No exemption applies." |
| `filepaths-array-vs-stringified-tool-description-clarification` (#19) | block | 5b | `fanoutEstimate: 9` vs `productionFilesTouched: 4` (delta 5 > +2 threshold). `scheduleHint` is null, not heroic-last. Scope does not cite Rule-3 exemption. Planner scoped sub-problem honestly with spin-off note, but trips Rule 5b mechanically. |
| `skill-namespace-and-semantic-search-discoverability` (#17) | warn | 5 | `estimatedContextTokens: 50000` for 47-file bulk-edit + script + test looks under-budget; solution-wide SKILL.md edits typically run higher. |
| `roslyn-skill-prompts-exceed-read-token-cap` (#15) AND `surface-test-skill-self-correction-observability` (#16) | warn | C2-wave-conflict | Adjacent-order initiatives share `skills/mcp-server-surface-test/prompts/full.md`. The planner correctly added `scheduleHint: "execute AFTER order=15"` on #16, so this is mitigated, but per Rule 6 spec the planner should have separated them by order gap. |
| `parallel-mode-workspace-cap-lru-or-raise` (#1) AND `workspace-close-with-drain-processes-for-teardown` (#3) | warn | C2-wave-conflict | Orders 1 and 3 share `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` — not adjacent (gap of 1 via #2), but parallel-mode execution would conflict. Conflict-graph correctly captures this edge. |
| `parallel-mode-workspace-cap-lru-or-raise` (#1) | warn | 3 (Rule-3 risk note) | Plan acknowledges `IWorkspaceManager` interface may push file count to 4 at re-vet time. Executor confirms; mitigation already documented. |
| `skill-namespace-and-semantic-search-discoverability` (#17) | info | C2-wave-conflict | Conflict-degree=6 on #17 (with #2, #3, #4, #12, #13, #14). `scheduleHint: "heroic-last"` is correctly set; serial scheduling expected. |
| `parameter-naming-canonicalization-experimental-surface` (#5) | info | anchor-stale | Planner flagged `anchorStale=true` for `IssueTemplateAndLabelSeedTests.cs` citation; deepener notes executor verifies at runtime. Self-flagged; advisory only. |
| `roslyn-skill-prompts-exceed-read-token-cap` (#15) | info | anchor-stale | Planner flagged `anchorStale=true` for deleted `maintainer-overlay.md` reference; deepener notes the stale anchor is excluded from scope. Self-flagged. |
| `surface-test-skill-self-correction-observability` (#16) | info | anchor-stale | Planner flagged `anchorStale=true` for both `maintainer-overlay.md` (deleted) and `phases/output-and-close.md` (created by #15). Self-flagged. |
| `workspace-close-with-drain-processes-for-teardown` (#3) | info | C2-wave-conflict | Initiative 3 shares `WorkspaceTools.cs` with #1; conflict-graph edge correctly captured. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: **yes** — independent rebuild matches orchestrator's edge set exactly.)

9 edges, max degree=6 (#17), 8 zero-degree initiatives (5, 6, 7, 8, 9, 10, 11, 19).

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | #1 only | n/a (single touchpoint) |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (+ partials) | none touched by this plan | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none touched by this plan | n/a |

No hotspot adjacency violations.

## Stale-row spot check

| Row id | Present? |
|---|---|
| All 22 row ids | yes (Grep confirms 22/22 present in `ai_docs/backlog.md`) |

## Recommended next step

- Outcome is `failed` due to 3 block findings, all concentrated in initiatives 17 and 19.
- Phase E remediation options:
  - (a) **Preferred for #17:** split per its own planner-documented 3-row decomposition (infrastructure row with `eng/list-skills.ps1` + test = 2 files; then 2 bulk-edit rows by directory).
  - (b) **Preferred for #19:** reduce `fanoutEstimate` to 4 (matching the actually-scoped sub-problem) and file the spin-off row immediately in state.json, OR keep `fanoutEstimate=9` and set `vettingOverride: true` to acknowledge intentional scoping.
  - (c) Accept all via `vettingOverride: true` if the user is comfortable with #17's heroic-last execution and #19's documented scoping.
- Re-review (cycle 1) after remediation should confirm block count drops to 0.

