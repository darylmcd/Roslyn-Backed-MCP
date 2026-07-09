# Plan review — 20260709T033118Z (cycle 0)

**Plan reviewed:** ai_docs/plans/20260709T033118Z_backlog-sweep
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 10 pending
**Findings:** block: 0, warn: 3, info: 1
**Anchor verification:** partial (first 3 initiatives spot-checked against on-disk source)

## Summary

The plan is structurally sound: no initiative breaches Rule 3 (max 4 production files, at cap on init 8), Rule 4 (max 2 test files), Rule 5 (max 55K, well under 80K), or Rule 5b (no fanoutOversize, no fanout>prod+2 — init 2 and 9 both report fanout=3 matching their 3-file counts). No multi-row bundles, so Rule 1 does not fire. The reviewer-rebuilt conflict graph exactly matches the orchestrator's (single edge 4—5). The most impactful finding is a wave-scheduling miss: initiatives 4 and 5 both edit ValidationBundleTools.cs and TestCoverageTools.cs and were placed at adjacent orders 4/5 despite init 5's own Risks explicitly warning against adjacency. Two fanout-recording gaps (init 3 and init 8 record null on signature-change/cross-file-consolidation shapes) and one cross-initiative staleness (init 3 assumes a fail-open default that init 1 flips first) round out the warnings. All are proceed-with-caveat, not blocks.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| workspace-fork-apply-robustness-cancellation | warn | C2-wave-conflict | Orders 4 and 5 share ValidationBundleTools.cs + TestCoverageTools.cs; adjacent order, Step 6 should have separated (init 5 Risks(c) even flags it). |
| host-refactor-tools-root-boundary-validation | warn | 5b | fanoutEstimate=null on a signature-change shape; identical sibling init 2 recorded fanout=3. |
| refactor-services-duplicate-code-sweep | warn | 5b | fanoutEstimate=null on a 4-file consolidation already at the Rule-3 cap — no file headroom if the extraction ripples. |
| host-refactor-tools-root-boundary-validation | info | anchor-stale | Risks assume PathValidationFailOpen default=true; init 1 (ships first) flips it to false — validation should track fail-closed. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes)

```json
{
  "edges": [ { "a": 4, "b": 5, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs", "src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs"] } ],
  "degrees": { "1": 0, "2": 0, "3": 0, "4": 1, "5": 1, "6": 0, "7": 0, "8": 0, "9": 0, "10": 0 },
  "zeroDegreeInitiatives": [1, 2, 3, 6, 7, 8, 9, 10]
}
```

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs (+partials) | none | n/a |
| src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs | none (init 9 uses `new`, not DI registration) | n/a |
| src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs | 9 only | no — orders 8 and 10 touch no hotspot |

No adjacent-order pair both touches a hotspot file; no hotspot-adjacency finding.

## Stale-row spot check

| Row id | Present? |
|---|---|
| security-options-fail-open-default | yes |
| host-analysis-tools-missing-clientroot-path-validation | yes |
| host-refactor-tools-root-boundary-validation | yes |
| workspace-fork-apply-security-hardening | yes |
| workspace-fork-apply-robustness-cancellation | yes |
| apply-with-verify-cancellation-and-compile-scope | yes |
| refactoringservice-god-class-decomposition | yes |
| refactor-services-duplicate-code-sweep | yes |
| workspace-manager-decompose-restore-and-analyzer-subsystems | yes |
| workspace-validation-service-validateinternal-decompose | yes |

## Recommended next step

- Outcome is passed-with-warnings: proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`, surfacing the four warnings in the run summary. In particular, the execute wave-batcher must keep initiatives 4 and 5 in separate waves (the conflict edge enforces this), and the executor for init 2/3 should assume the post-init-1 fail-closed default when writing validation.
