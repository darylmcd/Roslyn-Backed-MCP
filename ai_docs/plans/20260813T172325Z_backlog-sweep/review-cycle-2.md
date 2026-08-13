# Plan review — 2026-08-13 (cycle 2)

**Plan reviewed:** `ai_docs/plans/20260813T172325Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 2
**Outcome:** passed-with-warnings
**Initiative count:** 18 pending
**Findings:** block: 0, warn: 2, info: 11
**Anchor verification:** performed (scoped re-review — all five re-reviewed stanzas verified live this cycle; the remaining stanzas carried from cycle 1, unchanged)

## Summary

All five cycle-1 block findings are genuinely resolved, and resolved correctly rather than by asserting a number. Every recorded `fanoutEstimate` was re-derived from a live probe this session, not taken on the stanza's word: 6.01's gate inventory is exactly 17 `SKILL.md` files carrying `Roslyn MCP is not connected`; 6.03's broadened glob really does add exactly 6 non-`SKILL.md` files under `skills/`; `HasElicitation` really does appear in 5 `src/` files (4 edited + `SymbolTools.cs`, which already calls the canonical member); `TryElicitChoiceAsync` in 4 (3 edited + the same `SymbolTools.cs`). The recorded values (4 / 4 / 4 / 4 / 3) match `productionFilesTouched` in every case and sit well inside Rule 5b's margin against the measured blast radii. No stanza prose was rewritten to fit the number — the remediation was the single-field state edit the cycle-1 review prescribed, which is the desired shape.

Remediation introduced no new block findings. Every scoped initiative re-passes Rules 1, 3, 3b, 4, 5, and 5b independently: 6.01/6.02/6.03 at 4 production files with a plain no-exemption Rule 3 citation and 0–1 test files; 13.01 at 4 production / 3 test (exactly at the Rule 4 ceiling, correctly counted under the modified arm); 13.02 at 3 / 1 with a load-bearing `dependsOn` on 13.01. Anchors for all five stanzas were spot-checked live and every cited `file:line` resolves and matches its description, including the three `HasElicitation` definitions, the coordinator's production caller at line 91, and the guard script's `:15`/`:30`/`:39` anchors. The 13.01/13.02 CHANGELOG claim "all members are `internal`; no published-surface change" was independently verified — all four containing classes are `internal static`, so the forwarder deletions are safe on a published artifact.

Two warnings remain, both unchanged from cycle 1 and both scheduling-shaped rather than scope-shaped: adjacent-order pairs (11, 12) and (13.01, 13.02) each carry a conflict edge. Neither is a correctness defect — 13.02's `dependsOn` already forces the right serial order — but the wave scheduler must honor the edges rather than the order gap. The conflict graph agrees with the orchestrator exactly (5 edges, identical degrees, identical zero-degree set), rebuilt independently from plan.md Scope fields. No initiative touches an addenda-listed hotspot. All 15 backlog rows still exist and the backlog snapshot (`2026-08-13T05:18:24Z`) is unchanged since planning. Block count 5 -> 0; no oscillation.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| edit-preview-validation-decomposition | warn | C2-wave-conflict | Adjacent-order initiatives 11 and 12 share `src/RoslynMcp.Roslyn/Services/EditService.cs`; planner Step 6 should have separated them in the order. Unchanged from cycles 0 and 1. |
| elicitation-forwarder-collapse-trychoice-docs | warn | C2-wave-conflict | Adjacent-order 13.01 and 13.02 share all 3 of 13.02's production files (`StructuredCallToolFilter.cs`, `StructuredCallElicitationCoordinator.cs`, `ElicitationChoicePrompt.cs`). The `dependsOn` edge makes the serialization deliberate and correct, but separating the order values would make wave-separation structural rather than dependent on the scheduler reading `dependsOn`. Unchanged from cycle 1. |
| path-boundary-link-swap-toctou | info | C2-wave-conflict | Initiative 1 conflicts with 2 peers (orders 11, 12) on `src/RoslynMcp.Roslyn/Services/EditService.cs`; expect serial scheduling. |
| edit-preview-validation-decomposition | info | C2-wave-conflict | Initiative 11 conflicts with 2 peers (orders 1, 12) on `EditService.cs`; expect serial scheduling. |
| extract-atomicfilewriter-encoding-helper | info | C2-wave-conflict | Initiative 12 conflicts with 2 peers (orders 1, 11) on `EditService.cs`; expect serial scheduling. |
| extract-atomicfilewriter-encoding-helper | info | 3 | gate-forced-companion citation is complete (gate: C# compiler CS1061/CS0117; evidence: `find_references` returned 4 + 3 refs across 5 files with file:line; total: 4 base + 3 = 7) and sits exactly at the 7-file ceiling — one additional compiler-surfaced call site forces a split. |
| elicitation-inflight-cancellation-test-harness-deadlock | info | 3 | Scope is bimodal: state fields record only the guaranteed-floor branch (0 production files, 2 test files). The declared contingency branch adds up to 2 production files + 1 new test file; still within Rules 3/4, but every recorded budget field describes the floor only. |
| elicitation-inflight-cancellation-test-harness-deadlock | info | 5 | `estimatedContextTokens=55000` for a 0-production-file investigation/doc-comment change; Rule 5 is a diff/PR-size proxy (doc-only band ~15–25K), so the estimate reflects investigation reading cost rather than PR scope. |
| elicitation-inflight-cancellation-test-harness-deadlock | info | C2-wave-conflict | Contingency branch names `src/RoslynMcp.Host.Stdio/Elicitation/ElicitationChoicePrompt.cs` (owned by 13.01/13.02) and `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs` (owned by order 8) — latent edges (3,13.x) and (3,8) the graph cannot see because `productionFiles` is `[]`. |
| gate-owned-timeout-cts-oce-classification-audit | info | 3 | Zero production and zero test file changes; the deliverable is prose in the PR description + the backlog item's Evidence. The audit rationale must land in a tracked artifact (`ai_docs/items/<id>.md` + a `changelog.d` fragment) or the PR is an empty diff. |
| filewatcher-clearstale-timeout-flake-triage | info | 3 | `productionFilesTouched=0` but Scope also modifies `ai_docs/known-flakes.md`, which is not in Rule 3's exclusion list (tests, CHANGELOG, backlog.md, plan/state.json) — should count as 1. No cap impact; inconsistent with initiatives 5 and 14, which do count `ai_docs/` files. |
| shipped-skills-prefix-gate-batch-2 | info | backlog-sync-consistency | All three split children (6.01/6.02/6.03) declare `backlogRowsClosed: []` — the parent row `shipped-skills-hardcode-bare-roslyn-tool-prefix` stays OPEN after the whole sweep by design (13 of 17 gate files remain). Execute-side row-closing must tolerate the empty set and not close the row from `state.json.backlogRowsClosed`. |
| shipped-skills-prefix-gate-batch-2 | info | schedule-hint | 6.01/6.02/6.03 still carry `scheduleHint: heroic-last` inherited from the split parent, yet each is a cheap doc-only batch (26–45K, 4 files, `fanoutOversize: false`, fanout == files). Per the addenda case study, heroic-last often resolves to defer-at-closeout — which would drop all three and leave the parent row wholly untouched. Not cleared by cycle-1 remediation; consider clearing now that the oversize forcing is gone. |

## Conflict graph

(Reviewer-computed from plan.md Scope fields; agreement with orchestrator: yes — 5 edges, identical degrees, identical zero-degree set)

```json
{
  "edges": [
    { "a": 1, "b": 11, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/EditService.cs"] },
    { "a": 1, "b": 12, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/EditService.cs"] },
    { "a": 5, "b": 6.03, "sharedFiles": ["skills/mcp-server-surface-test/prompts/phases/output-and-close.md"] },
    { "a": 11, "b": 12, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/EditService.cs"] },
    { "a": 13.01, "b": 13.02, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs", "src/RoslynMcp.Host.Stdio/Middleware/StructuredCallElicitationCoordinator.cs", "src/RoslynMcp.Host.Stdio/Elicitation/ElicitationChoicePrompt.cs"] }
  ],
  "degrees": { "1": 2, "2": 0, "3": 0, "4": 0, "5": 1, "6.01": 0, "6.02": 0, "6.03": 1, "7": 0, "8": 0, "9": 0, "10": 0, "11": 2, "12": 2, "13.01": 1, "13.02": 1, "14": 0, "15": 0 },
  "zeroDegreeInitiatives": [2, 3, 4, 6.01, 6.02, 7, 8, 9, 10, 14, 15]
}
```

Order-sorted sequence used for adjacency: 1, 2, 3, 4, 5, 6.01, 6.02, 6.03, 7, 8, 9, 10, 11, 12, 13.01, 13.02, 14, 15. Edge (5, 6.03) is NOT adjacent (5 -> 6.01 -> 6.02 -> 6.03), so it earns no adjacency warn.

Confirmed non-edges: 6.02 ∩ 6.03 is empty (`skills/mcp-server-surface-test/README.md` vs `full.md` / `apply-and-test.md` / `output-and-close.md` / `verify-skills-are-generic.ps1`). Both stanzas flag the guard-rerun coupling in prose rather than declaring a false dependency, which is the correct handling — and independently checked this cycle: 6.02's README rewording (prefix-form examples) introduces none of the guard's banned patterns, so either merge order is safe.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (+ partials) | none | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | none | n/a |

No hotspot-adjacency findings. 13.01 and 13.02 both explicitly assert none of their files is an addenda-listed hotspot — verified correct.

## Stale-row spot check

| Row id | Present? |
|---|---|
| path-boundary-link-swap-toctou | yes |
| type-extraction-member-shape-validation | yes |
| elicitation-inflight-cancellation-test-harness-deadlock | yes |
| test-run-failures-pagination-truncation | yes |
| surface-test-audit-artifact-gate-and-scorecard-staleness | yes |
| shipped-skills-hardcode-bare-roslyn-tool-prefix | yes (stays open — 6.01/6.02/6.03 close nothing) |
| group-c-compilation-cache-gate-hardening | yes |
| gate-timeout-exception-drops-inner-oce | yes |
| gate-owned-timeout-cts-oce-classification-audit | yes |
| filewatcher-clearstale-timeout-flake-triage | yes |
| edit-preview-validation-decomposition | yes |
| extract-atomicfilewriter-encoding-helper | yes |
| elicitation-doc-drift-and-delegate-chain | yes (closed by 13.02) |
| deep-review-shape-list-single-source | yes |
| legacy-sln-slnx-parity-drift | yes |

Backlog `updated_at` is `2026-08-13T05:18:24Z`, matching `state.json.backlogSnapshot` — no snapshot drift.

## Cycle-2 delta

| Cycle-1 block | Status | Reviewer-verified evidence |
|---|---|---|
| shipped-skills-prefix-gate-batch-2 / 5b | resolved — `fanoutEstimate: 4` | a search for `Roslyn MCP is not connected` under `skills/` returns exactly 17 `SKILL.md` files; batch owns 4, none referenced or asserted-against by another shipped file |
| shipped-skills-prefix-usage-audit-tail / 5b | resolved — `fanoutEstimate: 4` | all four cited anchors resolve live (`roslyn-mcp-multisession-retro.md:17,:70`, `refactor/SKILL.md:148`, `refactor-loop/SKILL.md:132`, `mcp-server-surface-test/README.md:14`); the two skill lines are byte-identical as claimed |
| shipped-skills-prefix-genericity-guard-glob / 5b | resolved — `fanoutEstimate: 4` | exactly 6 non-`SKILL.md` files exist under `skills/`; guard anchors `:15` (`schemaVersion` pattern), `:30` (`-Filter 'SKILL.md'`), `:39` (URL strip) all resolve verbatim |
| elicitation-forwarder-collapse-haselicitation / 5b | resolved — `fanoutEstimate: 4` | a search for `HasElicitation` under `src/` returns 5 files: the 4 in Scope plus `SymbolTools.cs`, which needs no edit; three `HasElicitation` definitions and the production caller at `StructuredCallElicitationCoordinator.cs:91` all confirmed at the cited lines |
| elicitation-forwarder-collapse-trychoice-docs / 5b | resolved — `fanoutEstimate: 3` | a search for `TryElicitChoiceAsync` under `src/` returns 4 files: the 3 in Scope plus `SymbolTools.cs`; the stale `<returns>` at `ElicitationChoicePrompt.cs:76` and the hardcoded guard-test FQN at `:22-23` both confirmed present |

Block count 5 -> 0. No new block findings introduced by remediation. The two warnings and eleven infos are all carried unchanged from cycle 1 — none was in the remediation's mandate, and none is a regression. Recording `4` rather than the raw measured `5` for 13.01 (and `3` rather than `4` for 13.02) is correct and matches the cycle-1 review's own prescription: the extra file is `SymbolTools.cs`, which already calls the canonical member and needs no edit. Even the raw measured counts sit inside Rule 5b's margin, so neither reading blocks.

## Recommended next step

- `passed-with-warnings`: proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`. Surface both warnings in the run summary and ensure the wave scheduler consumes `conflictGraph.edges` (not order adjacency) so (11, 12) and (13.01, 13.02) are never co-scheduled — and that 13.01 lands before 13.02 per `dependsOn`. The two non-blocking cleanups worth doing opportunistically at execute time: clear the inherited `scheduleHint: heroic-last` on 6.01/6.02/6.03 so they are not silently deferred at closeout (which would leave the parent row wholly untouched), and make sure execute-side row-closing tolerates the empty `backlogRowsClosed` on all three.

## Orchestrator action taken on this review

- `scheduleHint: "heroic-last"` cleared to `null` on `shipped-skills-prefix-gate-batch-2`, `shipped-skills-prefix-usage-audit-tail`, and `shipped-skills-prefix-genericity-guard-glob` (the schedule-hint info finding above). Each is a 4-file doc/script batch with `fanoutOversize: false` and `fanoutEstimate == productionFilesTouched`; the hint was inherited from the split parent, where it encoded an oversize condition that no longer exists in any child.
