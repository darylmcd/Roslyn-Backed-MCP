# ci-artifact-owner-quality-deadline — Separate required quality work from the artifact test deadline

**row:** `ci-artifact-owner-quality-deadline` · **pri:** `High` · **size:** `S`

## Anchors

- `.github/workflows/ci.yml`
- `CI_POLICY.md`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs`

## Acceptance

- [ ] Observe a new topology regression fail on the old workflow, then pass with audit and formatting outside the six test shards in a parallel required quality job. Preserve all test partitions, SDK-floor isolation, release/publish artifacts and existing deadlines.
- [ ] Run audit on every previously applicable event. Establish fresh-runner restore and required analyzer outputs before formatting; retain exact changed-file/event/base semantics.
- [ ] Aggregate must reject failed, cancelled or unexpectedly skipped quality on code routes; docs/evidence require the intended skipped quality alongside their existing required outcomes. Exercise actual aggregate script for positive and negative routes.
- [ ] Require cold review, affected contract gates and all required exact-head hosted checks, including real audit/format and owner artifacts. No retry of unchanged failures or deadline increase.

## Evidence

- PR1846 head a9df5cd36fb12625333c9ab837e8ab0996eb763e; run40345610532 job121651721892 annotation: The job has exceeded the maximum execution time of 30m0s. 2650 tests passed, zero failed, 24 platform skips; publish succeeded before formatter was cancelled. Independent prerequisite; product source remains fixed.
