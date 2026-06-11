# workspace-process-pool-or-daemon — daemon/process-pool design (parked on worse-profile evidence)

**row:** `workspace-process-pool-or-daemon` · **pri:** `Defer` · **size:** `—` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Acceptance

- [ ] (on unblock) bounded design note comparing daemon, process-pool, and shared-workspace approaches, including lifecycle and failure-isolation hooks

## Evidence

- `docs/large-solution-profiling-baseline.md` recorded OrchardCore run; local raw artifacts under `artifacts/large-solution-profiling/20260426T212443Z/`.

## Context

Unblock trigger: future worse-profile evidence. Representative 227-project OrchardCore profile captured on 2026-04-26 did not justify daemon/process-pool implementation: `workspace_load` P95 was 44.85s, `symbol_search` P95 was 1.18s, and `find_references` P95 was 997ms, all below `docs/large-solution-profiling-baseline.md` thresholds. Keep deferred unless a larger/worse customer-scale profile or daily-use evidence shows `workspace_load` / reload P95 blocking work after `workspace_warm`.
