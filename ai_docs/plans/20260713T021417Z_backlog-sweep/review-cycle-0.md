# Plan review — 2026-07-12T00:00Z (cycle 0)

**Plan reviewed:** ai_docs/plans/20260713T021417Z_backlog-sweep
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 10 pending
**Findings:** block: 0, warn: 1, info: 6
**Anchor verification:** partial (inits 1-3 spot-checked)

## Summary

The plan is well-formed and passes all hard scope caps: every initiative is within Rule 3 (≤4 production files), Rule 4 (≤3 test files), Rule 5 (≤80K tokens), and shows no Rule 5b fanout-oversize flag. Schema is version 4; all 10 `backlogRowsClosed` ids still resolve in `ai_docs/backlog.md` (no stale rows). The reviewer-rebuilt conflict graph is byte-identical to the orchestrator's. The single warn is a genuine coupling the file-level conflict graph masks: initiatives 1 and 6 both restructure the same two methods in `StructuredCallToolFilter.cs` — init 1 physically relocates them to a new file while init 6 rewrites their bodies — so whichever merges second faces a semantic rebase, not a textual one, and init 6's declared Scope would be wrong post-init-1. The six info findings are bookkeeping/awareness items (four `fanoutEstimate: null` recorded despite documented reference probes; one at-cap colocated-helper design choice; one partial-acceptance row-close that correctly matches the backlog do-cell).

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| solutiondiscoveryhelper-hotpath-perf (6) | warn | C2-wave-conflict | Inits 1 & 6 both restructure IsWorkspaceIdRecoveryAllowedFor/IsWorkspaceIdAutoResolveAllowedFor in StructuredCallToolFilter.cs — init 1 moves them to ElicitationAllowlistPolicy.cs, init 6 rewrites their bodies. Edge (1,6) captured, orders non-adjacent, but second-to-merge needs a semantic rebase / Scope re-point. Sequence 1→6. |
| solutiondiscoveryhelper-hotpath-perf (6) | info | 5b | fanoutEstimate null though a find_references probe (45 refs, 2 caller sites) is documented in Risks; record the numeric value. |
| msbuild-evaluation-uncached-perf (7) | info | 5b | fanoutEstimate null; 4-call-site probe documented, interface unchanged. Contained; record numeric. |
| refactor-services-full-solution-scan-perf (8) | info | 5b | fanoutEstimate null; grep-confirmed private/internal methods, zero external callers. Record numeric. |
| analysis-services-uncached-full-solution-scans (9) | info | 5b | fanoutEstimate null; private static helpers, one caller each (grep-confirmed). Record numeric. |
| refactor-services-non-atomic-write-rollback (4) | info | 3 | At 4-file cap; AtomicFileWriter deliberately colocated in CompositeApplyOrchestrator.cs to avoid a 5th file. Within Rule 3's letter; awareness. |
| structuredcalltoolfilter-god-class-decompose (1) | info | 1 | Closes row on partial acceptance (only allowlist+metrics extracted, 4 hotspot methods untouched). Matches backlog do-cell next-deliverable; follow-up row recommended. |

## Conflict graph

(Reviewer-computed; agreement with orchestrator: yes — exact match)

```json
{
  "edges": [
    { "a": 1, "b": 6, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs"] },
    { "a": 3, "b": 7, "sharedFiles": ["src/RoslynMcp.Roslyn/Services/MsBuildEvaluationService.cs"] }
  ],
  "degrees": { "1": 1, "2": 0, "3": 1, "4": 0, "5": 0, "6": 1, "7": 1, "8": 0, "9": 0, "10": 0 },
  "zeroDegreeInitiatives": [2, 4, 5, 8, 9, 10]
}
```

## Hotspot scheduling

| Hotspot | Initiatives | Adjacent? |
|---|---|---|
| src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs | 6 only | n/a (single toucher) |
| src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs | none (init 3 references RoslynMcp.Roslyn/ServiceCollectionExtensions.cs but does not touch it) | no |
| src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs | none | no |

No two adjacent-order initiatives both touch a hotspot file.

## Stale-row spot check

| Row id | Present? |
|---|---|
| structuredcalltoolfilter-god-class-decompose | yes |
| workspace-infra-resource-cleanup-hygiene | yes |
| build-test-services-swallowed-exceptions-no-logging | yes |
| refactor-services-non-atomic-write-rollback | yes |
| paramvalidation-pagination-upper-bound-clamp | yes |
| solutiondiscoveryhelper-hotpath-perf | yes |
| msbuild-evaluation-uncached-perf | yes |
| refactor-services-full-solution-scan-perf | yes |
| analysis-services-uncached-full-solution-scans | yes |
| host-tools-layer-test-coverage-gap | yes |

## Recommended next step

- Outcome is `passed-with-warnings`: proceed to Phase F (handoff-readiness) then `/backlog-sweep:execute`, surfacing the init-1/init-6 sequencing warning in the run summary. Ensure the executor schedules init 1 before init 6 (they are already non-adjacent and never share a wave via the conflict edge) and that init 6's executor re-points its `IsWorkspaceIdRecoveryAllowedFor`/`IsWorkspaceIdAutoResolveAllowedFor` edits into `ElicitationAllowlistPolicy.cs` if init 1 has already merged.
