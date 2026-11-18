# CI Topology Decision Log

<!-- purpose: Dated history and measurements behind CI shard topology and docs-route decisions; evidence only, never a source of rules (rules: CI_POLICY.md). -->

Newest first. Each entry is a snapshot at its date; none describes current state. Current rules: `CI_POLICY.md`. Maintenance checklists: `ai_docs/references/ci-internals.md`. Hosted TRX artifacts expire after 14 days, so re-derive any figure from fresh runs before relying on it.

## Index

| Date | Decision | Open follow-up row |
|---|---|---|
| 2026-11-17 | Policy-docs route runs one leg with docs-tier consumer classes only | none |
| 2026-11-17 | Code route runs 6 Windows + 5 Linux shards | `ci-windows-six-shard-rebalance-remeasure`, `ci-linux-calibration-sibling-wait-coupling`, `ci-restore-linux-leg-timeout-30-after-rebalance` |
| 2026-11-08 | Linux shards 3 to 4 (legs hit the 30-minute cap) | closed by the entry above |
| 2026-10-29 | Case-count weighting replaced by a hosted duration profile | `ci-shard-duration-profile-hosted` |
| 2026-10-26 | Linux shards 2 to 3 | closed by the entries above |
| 2026-08-24 | Baseline before sharding; repository-level runner retired | none |

## 2026-11-17: policy-docs route runs docs-tier consumer classes only

Operator decision. Supersedes the rule that a policy-only documentation PR runs the complete class partition (four Linux shards). Current contract: `CI_POLICY.md` § Docs Route Test Selection.

Evidence (hosted `pull_request` runs of `ci.yml`, 2026-10-20 to 2026-11-17):

| Measurement | Value |
|---|---|
| Runs | 600, of which 170 docs-only (28%) |
| Docs-only run before the decision | About 22 min wait (median leg 1244-1410 s), about 85 runner-minutes, all 6,677 cases |
| Test failures caused by the docs change, in those 170 runs | 0 |
| Docs-caused failures, all from gates the route keeps | 8: `eng/verify-ai-docs.ps1` 4, `eng/verify-changelog-fragments.ps1` 4 |
| Red runs unrelated to the docs change | 6: five flaky tests in classes that read no docs (runs 43637525418, 43634218943, 43377558878, 42450892059, 40359428470), one build break on `main` (run 42266554397) |
| Text scan at decision time | 45 test-class files with a docs-tier token |
| List at the change | 16 consumer classes, 43 reviewed non-consumer classes, 4 reviewed helper files |
| Local run of the route (Windows dev host, one run, not hosted) | 16 of 523 classes, 143 cases (142 passed, 1 platform skip); test phase 104 s; whole verifier 135 s |

Limit: 157 of the 170 docs-only runs changed `ai_docs/backlog.md`, and `README.md` never changed in the window, so history barely exercised the documents tests read. The guard test, not the history, makes the selection sound. The decision shipped without a hosted run of the one-leg route; the first docs-only `validate` run after it is the first measurement and was not recorded here.

## 2026-11-17: six Windows + five Linux code shards

Operator decision. Supersedes four Windows + four Linux. Windows legs `windows-hosted-N-of-6`, Linux legs `linux-N-of-5`. Per-leg cap stayed 45 minutes; the Linux 45 is temporary (row `ci-restore-linux-leg-timeout-30-after-rebalance`).

Basis (hosted, superseded 4 + 4 topology):

| Measurement | Value |
|---|---|
| `pull_request` runs, 2026-10-20 to 2026-11-17 | 600: 425 code, 170 docs-only |
| Slowest Windows shard of a code PR, last 30 runs | Median 1657 s (27.6 min); Linux shards 1059-1239 s |
| `dotnet test` share of `Verify release build` (runs 44128243501, 44121901489) | 94-99% |
| `Verify release build` share of the job | 89-91% Linux, 91-96% Windows |
| Fixed cost before the first test | About 110 s Linux, 135 s Windows |
| Run 44128243501 test phase per shard, Linux | 917 / 967 / 917 / 936 s (sum 3737) |
| Run 44128243501 test phase per shard, Windows | 1089 / 1218 / 1752 / 1388 s (sum 5447, spread 663) |
| Hosted concurrency | 40 concurrent jobs; a code run uses 11 legs + quality + SDK floor = 13 |

First run of the new topology, run 44176184128 (head a6ac242a, first attempt, green), against the pre-change estimate:

| Quantity | Estimate | Observed |
|---|---|---|
| Linux leg job seconds | about 857 each | 861 / 845 / 682 / 843 / 826 |
| Windows leg job seconds | about 1043 each | 997 / 893 / 1413 / 1148 / 1227 / 1361 |
| Windows `dotnet test` seconds, summed | 5447 | 5940, spread 490 |
| Run creation to `validate` | about 17-18 min | 24 min |

- Linux met its estimate. Windows did not: the slowest leg took 1413 s, legs were unequal, and the cause of the long `windows-hosted-3-of-6` and `-6-of-6` legs is unidentified.
- Against the prior 27.6-minute slowest-leg median the run is about 4 minutes faster; one run, Windows single-leg noise band about 205 s, so the gain is not established.
- Part of the Windows spread plausibly comes from the profile hole (`ci-internals.md` § Duration-profile refresh). Re-measurement: `ci-windows-six-shard-rebalance-remeasure`.
- Run 44182325844 (head 49e7e206, attempt 1): one degraded Linux runner delayed its calibration build; the other four Linux legs failed in `Resolve Linux shard build times` after 3 min 1 s; a rerun of failed jobs recovered it. Tracked: `ci-linux-calibration-sibling-wait-coupling`.

Re-check collector run over the five most recent complete green first-attempt code runs on the superseded 4 + 4 topology (40 leg observations; runs 43762219005, 43765191714, 43774416634, 44121901489, 44128243501):

| Hosted image | Legs | Critical path | Balanced floor | Achievable gain | Single-leg noise band | Worst same-leg spread | Between-leg spread |
|---|---:|---:|---:|---:|---:|---:|---:|
| `windows-latest` | 4 | 1808.4s | 1483.8s | 324.7s (18.0%) | 205.6s | 1.67x | 1.32x |
| `ubuntu-latest` | 4 | 1308.2s | 1178.4s | 129.8s (9.9%) | 216.5s | 1.61x | 1.24x |

The same five runs regenerated `eng/shard-duration-profile.json` (512 profiled classes per image). Planner prediction for 522 classes: Windows 6 shards 3117.5 s each, Linux 5 shards 2914.8 s each (cumulative case seconds, not wall time). Scored with the mean class seconds of runs 44121901489 and 44128243501 the regenerated partition ranged 2936-3405 s (Windows) and 2535-3060 s (Linux); those two runs are also profile inputs, so the scores are partly in-sample, and the Linux scores omit per-run build-time scaling.

Known hole in the committed profile at this date (static fallback mis-weights five classes; do not hand-edit; fix by the documented refresh):

| Class | Static cases | Previous windows / ubuntu s | Fallback windows / ubuntu s |
|---|---:|---:|---:|
| `InterfaceParameterSemanticsDefaultsRouteTests` | 24 | 341.1 / 248.2 | 70.0 / 54.5 |
| `InterfaceParameterSemanticsAttributesRouteTests` | 20 | 266.1 / 209.1 | 58.3 / 45.5 |
| `InterfaceParameterSemanticsModifiersRouteTests` | 9 | 110.3 / 82.9 | 26.3 / 20.5 |
| `CrossProjectInterfaceBindingIntegrationTests` | 19 (measured 18) | 168.2 / 121.3 | 55.4 / 43.2 |
| `WorkspaceCloseDrainTests` | 35 (measured 33) | 0.7 / 0.8 | 102.1 / 79.5 |

## 2026-11-08: four Linux shards

PR #2032 run 42490458875 cancelled docs shard 2 at the 30-minute cap after 2,297 tests passed in 28m18s. PR #2027 run 42487026249 finished code shard 2 in 29m25s (35 s margin). Code and policy-doc routes then partitioned the same class set over four Linux legs, cap 30 minutes, one artifact owner.

## 2026-10-29: duration profile replaces case-count weighting

Five complete green first-attempt PR runs (35 leg observations; 4 Windows + 3 Linux legs; runs 40783926346, 40788319028, 40788434338, 40800095395, 40803366211). The re-check trigger fired because the Linux shard count changed and non-test work moved to the quality job.

| Hosted image | Legs | Critical path | Balanced floor | Achievable gain | Single-leg noise band | Worst same-leg spread | Between-leg spread |
|---|---:|---:|---:|---:|---:|---:|---:|
| `windows-latest` | 4 | 1827.2s | 1348.9s | 478.4s (26.2%) | 325.9s | 1.93x | 2.09x |
| `ubuntu-latest` | 3 | 1322.8s | 1093.8s | 229.0s (17.3%) | 224.2s | 1.68x | 1.98x |

- Achievable gain exceeded the noise band on both images and summed case duration ranked legs, so the earlier decision (static case-count weights, no OS-specific profile) no longer stood; implementation: `ci-shard-duration-profile-hosted`.
- Static case counts misjudged cost. Mean TRX case seconds on `windows-latest`: `InterfaceBaseTypeInsertionIntegrationTests` 1013 s, `ForkNativeAdmissionTests` 759 s, `MovedDeclarationFileOwnershipIntegrationTests` 569 s, `CrossProjectReceivingNamespaceIntegrationTests` 511 s (same four lead `ubuntu-latest`). A class runs its cases serially, so the largest class floors its leg. Cost rows: `test-heavy-integration-class-cost` and `test-heavy-class-cost-*`.
- Windows critical path rose from 720.4 s (about 2,500 cases) to 1827.2 s (about 5,600 cases): mostly suite growth, not topology.
- Limits: five runs across five heads, image build versions not captured, per-leg wall spread 1.4x-1.9x.
- Superseded earlier decision: at a four-Windows + two-Linux calibration (5 runs, 30 observations) the achievable gain (10.2% Windows, 10.8% Linux) was below the noise band (82.9 s, 140.3 s), so case-count weights stood.

## 2026-10-26: three Linux shards

Operator decision. Two Linux legs no longer fit with margin: code run 40401658309 legs 27m45 / 22m; run 40419476050 (#1855) 27m51 / 17m; run 40412863293 (#1854) linux-1 cancelled at 30 min after 2,660 passed; docs runs for #1853 / #1848 22-27m27 per leg. A third shard kept the 30-minute cap and the single artifact owner.

## 2026-08-24 baseline before sharding

| Metric | Repository-level Windows | Hosted Linux |
|---|---:|---:|
| Median job (last five successful PRs) | 16m09s | 25m10s |
| Median `verify-release` | 15m42s | 24m33s |
| Median `dotnet test` | 15m23s | 23m40s |

- Windows TRX baseline: 2,536 cases in 16m30s; about 6m13s parallel phase and 10m17s serialized `[DoNotParallelize]` tail.
- Local timing probe (2026-09-29): removing the `[DoNotParallelize]` opt-out from `WorkspaceValidationTimeoutTests` cut the serialized tail for the two probed classes from 14-23 s to about 6.7 s; a bounded local result, not a hosted baseline.
- First hosted two-shard calibration: Windows 19m50s and 16m29s, no better than the 16m09s median, so code PRs moved to four Windows shards.
- GitHub-hosted public-repository `ubuntu-latest` and `windows-latest` runners provide 4 CPUs and 16 GB RAM; do not assume 2 vCPU.
