# Plan review — 2026-06-09T13:44:05Z (cycle 0)

**Plan reviewed:** ai_docs/plans/20260609T134405Z_backlog-sweep
**Reviewer mode:** /backlog-sweep:prepare (Phase D)
**Cycle:** 0
**Outcome:** passed-with-warnings
**Initiative count:** 3 pending
**Findings:** block: 0, warn: 3, info: 2
**Anchor verification:** performed (all 3 initiatives spot-checked against source)

## Summary

The three-initiative chain is Rule 1–5 compliant: every initiative closes exactly one row (no bundling), Rule 3 file counts are within the cap (4 / 3 / 1, no exemption needed), test budgets are ≤ 3, and context estimates (45K / 55K / 30K) are well under the 80K ceiling and shape-appropriate. Anchors resolve precisely — `ResolveOptionalWorkspaceId` (WorkspaceTools.cs:560, `private static` as stated), the recovery stack in StructuredCallToolFilter.cs (361/381/503), the positional `GateMetricsDto` record, and all four pilot tools in SymbolTools.cs (each declaring `string workspaceId` without a default). Independently-recomputed conflict graph matches the orchestrator's exactly (single edge 1↔2, degrees {1:1,2:1,3:0}, zero-degree [3]). Most material finding: a scheduling gap — order 3 is file-disjoint (zero-degree) yet logically depends on order 1, and that dependency is encoded only in a non-canonical freeform `scheduleHint: "depends-on:..."` the executor's wave-batcher is not specified to parse. Warnings, not blocks: the chain is explicitly fully-serial, which mitigates it in practice.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| workspace-auto-load-on-demand | warn | C2-wave-conflict | Adjacent-order initiatives 1 and 2 share StructuredCallToolFilter.cs + GateMetricsDto.cs; mitigated by explicit fully-serial scheduling. |
| workspace-id-optional-readonly-surface-flip | warn | 5b | fanoutEstimate=null on a refactor-shaped (signature-flip) initiative; Risks explain the 4-method pilot scope and defer the ~45-method remainder. |
| workspace-id-optional-readonly-surface-flip | warn | C2-wave-conflict | Order 3 zero-degree in conflict graph but logically depends on order 1; dependency encoded only in non-canonical freeform scheduleHint, not machine-enforced by the wave-batcher. |
| workspace-id-optional-readonly-surface-flip | info | plan-consistency | plan.md header called init 3 "the catalog hotspot" but Scope excludes ServerSurfaceCatalog.Symbols.cs; header stale vs resolved pilot scope. |
| workspace-id-omitted-single-resolve | info | 5b | fanoutEstimate=null on positional GateMetricsDto param add; verified contained (builder indirection, defaulted param, no direct callers). |

## Conflict graph (reviewer-computed; agrees with orchestrator)

```json
{ "edges": [ { "a": 1, "b": 2, "sharedFiles": ["src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs", "src/RoslynMcp.Core/Models/GateMetricsDto.cs"] } ], "degrees": { "1": 1, "2": 1, "3": 0 }, "zeroDegreeInitiatives": [3] }
```

## Hotspot scheduling

No addenda-listed hotspot file (`ServerSurfaceCatalog.*`, `ServiceCollectionExtensions.cs`, `WorkspaceManager.cs`) appears in any initiative's Scope production-file list. Init 2 calls `WorkspaceManager.LoadAsync` but does not edit the file. No hotspot-adjacency finding.

## Recommended next step

passed-with-warnings → proceed to Phase F (handoff) then `/backlog-sweep:execute`, surfacing the warnings. Load-bearing: execute MUST honor the strict 1→2→3 dependency (order 3's tests require order-1's null-aware gate active) — do not parallelize zero-degree order 3 ahead of order 1.

## Orchestrator post-review actions (cycle 0, warning-driven; no code-path/scope change)

- Set order-3 `scheduleHint: "heroic-last"` (canonical, executor-recognized) so the zero-degree order 3 is scheduled after 1 & 2 — hardens the load-bearing C2-wave-conflict warn with a machine-readable mechanism instead of prose-only.
- Fixed the stale plan.md header that called init 3 "the catalog hotspot" (the pilot does not touch `ServerSurfaceCatalog.*`) — resolves the plan-consistency info finding.
