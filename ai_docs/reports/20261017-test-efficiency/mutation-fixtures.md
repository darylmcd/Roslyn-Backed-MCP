# Project-mutation fixture efficiency

<!-- scope: in-repo -->

| Field | Evidence |
|---|---|
| Baseline | PR #1792, commit 38fab417bd5e095255574ee6e1fdd961ed5f1d51 |
| Selection | Highest remaining class in both hosted OSes on CI run 39074897621 attempt 1: Windows 340.220s, Linux 221.034s |
| Scope | ProjectMutationIntegrationTests and owned fixture factory; no product or CI policy changes |
| Ownership | Production TestServiceContainer per invocation; independent project-preview, undo, change-tracker, workspace and watcher state |
| Smaller fixture | 21 methods / 24 cases load the existing restored SampleLib project |
| Full fixture | Nine methods / nine cases retain SampleSolution for references, cycles and central-package behavior |
| Isolation | Fresh copied files and services per case; no mutable reset protocol |
| Cleanup | Existing failure-safe owned lifecycle closes workspaces before provider disposal and root deletion |
| Coverage | All 30 original method/data/assertion token sequences preserved after explicit fixture/service substitutions; all 33 runtime identities preserved |
| Tracking | workspace-fixture-load-amortization-wave-2 stays open for CompilationCacheAdoptionTests |

## Local Windows comparison

| Run | Before seconds | After seconds | Passed | Skipped |
|---|---:|---:|---:|---:|
| 1 | 118.410 | 77.769 | 33 | 0 |
| 2 | 139.449 | 123.462 | 33 | 0 |
| Median | 128.930 | 100.616 | — | — |

- Median reduction: 22.0%; both slower samples retained.
- Interval: first case start to last case end, from TRX; excludes build/restore/adapter startup.
- Selection: exact ClassName=RoslynMcp.Tests.ProjectMutationIntegrationTests, Release, eng/ci.runsettings, --no-build --no-restore -p:TestFixturesPrepared=true.
- All four runs passed the identical 33-case multiset with no skips.
- Additional mixed validation with RefactoringFixtureOwnershipTests: 36 passed, zero skipped.
- Sample variation prevents a guaranteed whole-CI saving claim. Hosted before/after evidence and current-head full qualification belong to the implementation PR.

## Artifacts

- [Timing samples and identities](mutation-local-timings.json).
- [Method-to-fixture mapping](mutation-fixture-map.json).
- [Repository audit](report.md); [first implementation](implementation.md).

## Qualification

| Check | Evidence |
|---|---|
| Initial changed-tree gate | One verifier output-drain timeout; clean base did not reproduce it. |
| Prerequisite repair | [Controlled output-drain diagnosis, negative controls and repair](output-drain.md). Original incident attribution remains limited. |
| Focused validation | 60 cases passed, zero skips: runner, verifier, mutation and owned-fixture suites. Three boundary/probe cases passed after the final EOF check and atomic PID publication. |
| Final local / hosted validation | Required unchanged full gate and repeated hosted Windows/Linux measurements; results belong to the implementation PR. |
| Coverage | All 33 mutation identities and every original assertion retained; output-reader regressions are additive. |

