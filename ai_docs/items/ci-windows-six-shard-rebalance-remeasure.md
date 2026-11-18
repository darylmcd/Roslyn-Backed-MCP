# ci-windows-six-shard-rebalance-remeasure — Re-measure and rebalance Windows under six shards

**row:** `ci-windows-six-shard-rebalance-remeasure` · **pri:** `Medium` · **size:** `M` · **verified:** `b525794bae630b1baa525694726a98b4095fd1e7`

## Anchors

- `eng/shard-duration-profile.json` — committed profile from runs 43762219005, 43765191714, 43774416634, 44121901489, 44128243501 (all on the superseded 4 + 4 topology); 512 profiled classes per image, 10 on the static fallback.
- `eng/collect-hosted-shard-timings.ps1` — the only sanctioned generator (`-ProfileJson`) and the timing report.
- `ai_docs/references/ci-internals.md` — § Duration-profile refresh: refresh procedure.
- `ai_docs/references/ci-topology-decision-log.md` — 2026-11-17 six Windows + five Linux entry: the known-hole table and the estimate against run 44176184128.

## Acceptance

- [ ] Five complete green first-attempt code `pull_request` runs under the 6 + 5 topology are measured with `eng/collect-hosted-shard-timings.ps1`; run ids, heads and the per-image table are recorded in `ai_docs/references/ci-topology-decision-log.md` (standing rules stay in `CI_POLICY.md`).
- [ ] `eng/shard-duration-profile.json` is regenerated from those runs by the documented procedure (never hand-edited). `InterfaceParameterSemanticsDefaultsRouteTests`, `InterfaceParameterSemanticsAttributesRouteTests`, `InterfaceParameterSemanticsModifiersRouteTests`, `CrossProjectInterfaceBindingIntegrationTests` and `WorkspaceCloseDrainTests` carry measured weights, or the row records which of them changed again and still fall back.
- [ ] Across five further complete green runs on the regenerated profile, the median slowest `windows-latest` leg is within the collector's single-leg noise band of the mean Windows leg. If it is not, the cause of the slow legs is identified and recorded before any further count or cap change.

## Evidence

- Run 44176184128 (PR 2135, head a6ac242a, first attempt, green; the only 6 + 5 run so far). Windows leg job seconds 997 / 893 / 1413 / 1148 / 1227 / 1361 against a pre-change estimate of about 1043 each; Windows `dotnet test` seconds summed to 5940 (5447 on four shards in run 44128243501), spread 490, although the planner predicted equal shards. Linux 861 / 845 / 682 / 843 / 826 against an estimate of about 857.
- Fallback table, previous measured cumulative case seconds to current static fallback (`windows-latest` / `ubuntu-latest`): Defaults 341.1 / 248.2 to 70.0 / 54.5; Attributes 266.1 / 209.1 to 58.3 / 45.5; Modifiers 110.3 / 82.9 to 26.3 / 20.5; `CrossProjectInterfaceBindingIntegrationTests` 168.2 / 121.3 to 55.4 / 43.2; `WorkspaceCloseDrainTests` 0.7 / 0.8 to 102.1 / 79.5. The first four hide about 676 s (Windows) and 498 s (Linux) from the planner.
- Collector on the five profile input runs (4 + 4 topology): `windows-latest` critical path 1808.4 s, balanced floor 1483.8 s, noise band 205.6 s.

## Context

- No qualifying input set closed the hole on 2026-11-17: the parameter-semantics classes had their current case counts in four complete green first-attempt code runs, `CrossProjectInterfaceBindingIntegrationTests` (changed by PR 2132) in one, `WorkspaceCloseDrainTests` (changed by PR 2133) in two.
- The cause of the long `windows-hosted-3-of-6` and `windows-hosted-6-of-6` legs in run 44176184128 is not identified. The profile hole is a plausible partial cause, not a proven one.
- Uploaded TRX expires after 14 days; download inside the window. Related: `ci-shard-duration-profile-hosted`, `ci-restore-linux-leg-timeout-30-after-rebalance`.
