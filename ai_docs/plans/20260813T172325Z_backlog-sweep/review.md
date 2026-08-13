# Plan review — 2026-08-13 (cycle 1)

**Plan reviewed:** `C:/Code-Repo/Roslyn-Backed-MCP/ai_docs/plans/20260813T172325Z_backlog-sweep/`
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 1
**Outcome:** failed
**Initiative count:** 18 pending
**Findings:** block: 5, warn: 2, info: 11
**Anchor verification:** performed

## Summary

All four cycle-0 block findings are genuinely resolved, not paper-shuffled. `type-extraction-member-shape-validation` and `legacy-sln-slnx-parity-drift` now record the measured fanout (1 and 2). `shipped-skills-hardcode-bare-roslyn-tool-prefix` was split into three children whose oversize forcing is actually removed — the parent's fanout=25 came from bundling a deterministic all-17-file genericity assertion, and 6.03 explicitly defers that assertion, so each child's blast radius really is its own file list (reviewer-verified: exactly 17 gate-carrying `SKILL.md` files, exactly 6 non-`SKILL.md` files under `skills/`, so 6.03's "6 scanned / 3 with hits" claim checks out). `elicitation-doc-drift-and-delegate-chain` split 4 test files into 3 + 1, clearing the Rule 4 breach, with a correct `dependsOn` edge encoding the load-bearing land order.

The plan nevertheless fails on a single recurring defect class: **all five newly-authored split children record `fanoutEstimate: null` while their own prose reports a measured probe**. Two are flat contradictions — 13.01's Risks literally writes `` `fanoutEstimate: 4` `` and 13.02's writes `` `fanoutEstimate: 3` `` while both state blocks say `null` (reviewer-verified the underlying greps: 5 and 4 `src/` files respectively). This is the exact Rule 5b consumer-side backstop that blocked two initiatives last cycle; the remediation fixed the two flagged instances and then reproduced the defect in every new stanza it wrote. Each fix is a one-field edit with the correct value already stated in the stanza — no re-scoping, no re-splitting.

Conflict graph agrees with the orchestrator exactly (5 edges, identical degrees, identical zero-degree set). No hotspot adjacency (no initiative touches `ServerSurfaceCatalog.cs`, `ServiceCollectionExtensions.cs`, or `WorkspaceManager.cs`). All 15 backlog rows still present. Anchors spot-checked live for initiatives 1, 2, 3 plus all scoped 6.x/13.x stanzas — every cited `file:line` resolves and matches its description.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| shipped-skills-prefix-gate-batch-2 | block | 5b | `fanoutEstimate: null` while Diagnosis cites a Grep returning exactly 17 gate files and concludes "blast radius is then exactly its own self-contained file list, verified"; Risks reports 13 remaining. Record 4. *(new since cycle 0 — introduced by remediation)* |
| shipped-skills-prefix-usage-audit-tail | block | 5b | `fanoutEstimate: null` while Diagnosis states "Fanout: ... blast radius equals the file list" and Risks cites grep counts ("confirmed by grep"). Record 4. *(new since cycle 0 — introduced by remediation)* |
| shipped-skills-prefix-genericity-guard-glob | block | 5b | `fanoutEstimate: null` while Risks reports "Fanout is bounded and verified ... exactly 6 files to the scan and exactly 3 of them produce hits, so the blast radius is 1 script plus 3 content files". Record 4. *(new since cycle 0 — introduced by remediation)* |
| elicitation-forwarder-collapse-haselicitation | block | 5b | Risks literally writes `` `fanoutEstimate: 4`, equal to `productionFilesTouched` `` (grep over `src/` returns 5 files — verified) while stateFields says `null`. *(new since cycle 0 — introduced by remediation)* |
| elicitation-forwarder-collapse-trychoice-docs | block | 5b | Risks literally writes `` `fanoutEstimate: 3`, equal to `productionFilesTouched` `` (grep over `src/` returns 4 files — verified) while stateFields says `null`. *(new since cycle 0 — introduced by remediation)* |
| edit-preview-validation-decomposition | warn | C2-wave-conflict | Adjacent-order 11 and 12 share `src/RoslynMcp.Roslyn/Services/EditService.cs`; Step 6 should have separated them. Unchanged from cycle 0. |
| elicitation-forwarder-collapse-trychoice-docs | warn | C2-wave-conflict | Adjacent-order 13.01 and 13.02 share 3 production files. `dependsOn` makes the serialization deliberate, but the order values should also separate them. |
| path-boundary-link-swap-toctou | info | C2-wave-conflict | Degree 2 (orders 11, 12) on `EditService.cs`; expect serial scheduling. |
| edit-preview-validation-decomposition | info | C2-wave-conflict | Degree 2 (orders 1, 12) on `EditService.cs`; expect serial scheduling. |
| extract-atomicfilewriter-encoding-helper | info | C2-wave-conflict | Degree 2 (orders 1, 11) on `EditService.cs`; expect serial scheduling. |
| extract-atomicfilewriter-encoding-helper | info | 3 | gate-forced-companion citation complete (gate: CS1061/CS0117; evidence: 4+3 refs across 5 files; total 4+3=7) and sits exactly at the 7-file ceiling — one more compiler-surfaced call site forces a split. |
| elicitation-inflight-cancellation-test-harness-deadlock | info | 3 | Bimodal Scope: state records only the guaranteed-floor branch (0 prod, 2 test). Contingency adds up to 2 prod + 1 test. |
| elicitation-inflight-cancellation-test-harness-deadlock | info | 5 | 55000 tokens for a 0-production-file investigation; Rule 5 is a diff-size proxy (doc-only band ~15-25K). |
| elicitation-inflight-cancellation-test-harness-deadlock | info | C2-wave-conflict | Contingency names `ElicitationChoicePrompt.cs` (now owned by 13.01/13.02) and `WorkspaceExecutionGate.cs` (order 8) — latent edges the graph cannot see because `productionFiles` is `[]`. |
| gate-owned-timeout-cts-oce-classification-audit | info | 3 | Zero production and zero test changes; the audit rationale must land in `ai_docs/items/<id>.md` + a changelog fragment or the PR is an empty diff. |
| filewatcher-clearstale-timeout-flake-triage | info | 3 | `productionFilesTouched=0` but Scope also edits `ai_docs/known-flakes.md`, which is not a Rule 3 exclusion — should count as 1. Inconsistent with initiatives 5 and 14. |
| shipped-skills-prefix-gate-batch-2 | info | backlog-sync-consistency | All three children declare `rowsClosed: []`; the parent row stays OPEN after the whole sweep by design (13 of 17 gate files remain). Execute-side row-closing must not close it from `state.json.backlogRowsClosed`. |
| shipped-skills-prefix-gate-batch-2 | info | schedule-hint | 6.01/6.02/6.03 inherit `scheduleHint: heroic-last` from the split parent despite being cheap 4-file doc batches with `fanoutOversize: false`. Per the addenda case study, heroic-last often becomes defer-at-closeout — which would drop all three and leave the parent row untouched. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes — 5 edges, identical degrees, identical zero-degree set)

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

Checked and confirmed non-edges: 6.02 ∩ 6.03 is empty (`skills/mcp-server-surface-test/README.md` vs `full.md`/`apply-and-test.md`/`output-and-close.md`/`verify-skills-are-generic.ps1`) — both stanzas flag the guard-rerun coupling in prose, which is correct handling of a non-edge.

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (+ partials) | none | n/a |
| `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs` | none | n/a |
| `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` | none | n/a |

No hotspot-adjacency findings.

## Stale-row spot check

| Row id | Present? |
|---|---|
| path-boundary-link-swap-toctou | yes |
| type-extraction-member-shape-validation | yes |
| elicitation-inflight-cancellation-test-harness-deadlock | yes |
| test-run-failures-pagination-truncation | yes |
| surface-test-audit-artifact-gate-and-scorecard-staleness | yes |
| shipped-skills-hardcode-bare-roslyn-tool-prefix | yes (stays open — 3 children close nothing) |
| group-c-compilation-cache-gate-hardening | yes |
| gate-timeout-exception-drops-inner-oce | yes |
| gate-owned-timeout-cts-oce-classification-audit | yes |
| filewatcher-clearstale-timeout-flake-triage | yes |
| edit-preview-validation-decomposition | yes |
| extract-atomicfilewriter-encoding-helper | yes |
| elicitation-doc-drift-and-delegate-chain | yes (closed by 13.02) |
| deep-review-shape-list-single-source | yes |
| legacy-sln-slnx-parity-drift | yes |

## Cycle-1 delta

| Cycle-0 block | Status |
|---|---|
| type-extraction-member-shape-validation / 5b | resolved — `fanoutEstimate: 1` recorded with probe method in Risks |
| shipped-skills-hardcode-bare-roslyn-tool-prefix / 5b | resolved — split into 6.01/6.02/6.03; the forcing all-17-file assertion is deferred, so no child is oversize |
| elicitation-doc-drift-and-delegate-chain / 4 | resolved — split into 13.01 (3 test files) + 13.02 (1), with `dependsOn` encoding the land order |
| legacy-sln-slnx-parity-drift / 5b | resolved — `fanoutEstimate: 2` recorded with classified rg output |

Block count 4 -> 5. The increase is entirely the same Rule-5b null-drop class reproduced in the five newly-authored split children, each fixable by copying a number already present in the stanza prose into `state.json`. This is a mechanical regression, not a scoping oscillation.

## Recommended next step

- `failed`: Phase E should spawn remediators for the 5 block findings. All five are single-field state edits — set `fanoutEstimate` to 4 / 4 / 4 / 4 / 3 for `shipped-skills-prefix-gate-batch-2`, `shipped-skills-prefix-usage-audit-tail`, `shipped-skills-prefix-genericity-guard-glob`, `elicitation-forwarder-collapse-haselicitation`, `elicitation-forwarder-collapse-trychoice-docs` respectively (values taken verbatim from each stanza's own Risks/Diagnosis and independently verified this cycle). No stanza content needs rewriting. While in those stanzas, consider clearing the inherited `scheduleHint: heroic-last` on 6.01/6.02/6.03 (info finding) and separating the order values of 13.01/13.02 (warn finding).