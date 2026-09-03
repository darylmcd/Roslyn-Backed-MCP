# Adversarial Plan Review — 20260903T174035Z_backlog-remediate

## Header

- **Plan dir:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260903T174035Z_backlog-remediate`
- **Cycle:** 0
- **Outcome:** `passed-with-warnings`
- **Findings:** block 0 · warn 5 · info 1
- **Anchor verification:** `performed`

## Summary

All 5 initiatives are internally well-scoped against Rules 1/3/3b/4/5 — file counts, test counts, and token estimates match their stated Scope tables exactly, all 5 `backlogRowsClosed` ids are still live in `ai_docs/backlog.md`, and every spot-checked `file:line` anchor resolved exactly as claimed. The plan's own cross-reference correction (initiatives 2 and 4 redirecting a stale `justfile` cross-reference in the source backlog row from `ci-router-pure-decision` to `ci-actionlint-pinned-gate`) was independently verified accurate. The reviewer's one substantive catch — an undocumented `CI_POLICY.md` overlap between `ci-router-pure-decision` (order 4, router-prose section) and `ci-actionlint-pinned-gate` (order 5, local-validation table + composition sentence) — has been remediated: both stanzas' Risks fields now document the collision and the "do not co-schedule in the same parallel wave" instruction. Rule 5b fanout probes for `tool-update-owned-process-shutdown` and `ci-router-pure-decision` have also been added (both confirm `fanoutEstimate: 0`, not under-scoped). No blocks.

## Findings

| Initiative | Severity | Rule | Evidence | Status |
|---|---|---|---|---|
| tool-update-owned-process-shutdown | warn | 5b | No documented fanout probe for the `Stop-OwnedRoslynMcpProcess` → `Stop-OwnedToolStoreProcess` signature change. | Remediated — probe added to Risks, `fanoutEstimate: 0`. |
| ci-router-pure-decision | warn | 5b | No documented fanout probe for the 113-line PowerShell extraction + 48-assertion relocation. | Remediated — probe added to Risks, `fanoutEstimate: 0`. |
| ci-router-pure-decision | warn | C2-wave-conflict | Consecutive-order sibling `ci-actionlint-pinned-gate` also touches `CI_POLICY.md`; undocumented. | Remediated — documented in Risks, scheduling constraint added. |
| ci-actionlint-pinned-gate | warn | C2-wave-conflict | Same collision from the other side; undocumented. | Remediated — documented in Risks, scheduling constraint added. |
| (plan-level) | warn | C2-graph-disagreement | `state.json.conflictGraph` cache is empty; disagrees with the rebuilt graph. | Expected — `bsweep-state.mjs generations` recomputes from live `productionFiles` on every call (the stored graph is only a cache); will resolve automatically at Step 3 scheduling. |
| ci-actionlint-pinned-gate | info | C2 (degree ≥ 2) | Rebuilt-graph degree 2 (`justfile` + `CI_POLICY.md`), `scheduleHint: null`. | Accepted — both edges are simple two-way file overlaps (not a fanout hazard), `heroic-last` not warranted. |

## Conflict graph (reviewer-computed, matches the now-corrected Scope lists)

```json
{
  "edges": [
    { "a": 2, "b": 5, "sharedFiles": ["justfile"] },
    { "a": 4, "b": 5, "sharedFiles": ["CI_POLICY.md"] }
  ],
  "degrees": { "1": 0, "2": 1, "3": 0, "4": 1, "5": 2 },
  "zeroDegreeInitiatives": [1, 3]
}
```

## Hotspot table

None of the 5 initiatives' Scope files touch any addenda-listed hotspot file. No findings.

## Stale-row table

All 5 `backlogRowsClosed` ids confirmed present in `ai_docs/backlog.md` (lines 50, 58, 66, 69, 70). No findings.

## Tooling gap surfaced during this review (not a plan defect)

`~/.claude/scripts/bsweep-state.mjs` provides no writer for `state.json`'s top-level `reviewStatus` / `lastReviewFindings` fields (only the skeleton default of `'pending'` exists; `hash --mark-reviewed` stamps hashes, not review outcome). Per repo convention this is a global-tooling defect, not a Roslyn-Backed-MCP backlog item — tracked at closeout via a row in `~/.claude/ai_docs/backlog.md` rather than here. Practical effect on this run: `exec-args`'s warn-aware wave-size cap falls back to unconstrained sizing, which is not a blocker for this plan (the one relevant warn — the `CI_POLICY.md` overlap — is a cross-generation edge, not an intra-wave one).

## Recommended next step

Proceed to execute. Do not co-schedule `ci-router-pure-decision` and `ci-actionlint-pinned-gate` in the same parallel generation — `bsweep-state.mjs generations` will place them in separate generations automatically once it recomputes the conflict graph from live `productionFiles`.
