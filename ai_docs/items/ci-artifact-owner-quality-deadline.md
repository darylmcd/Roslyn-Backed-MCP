# ci-artifact-owner-quality-deadline — Keep quality and cache work outside test tails

**row:** `ci-artifact-owner-quality-deadline` · **pri:** `High` · **size:** `L`

## Anchors

- `.github/workflows/ci.yml`
- `CI_POLICY.md`
- `eng/prepare-validation-dependencies.ps1` (new canonical main-plus-sample preparation)
- `eng/verify-release.ps1` (reuse preparation before restored-license/build/test/publish)
- `tests/RoslynMcp.Tests/TestFixturePreparationContractTests.cs`
- `tests/RoslynMcp.Tests/VerifyReleaseChildScriptTests.cs`
- `tests/RoslynMcp.Tests/CiEvidenceTierConsumerContractTests.cs` (classify only new synthetic routing payload)
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`

## Acceptance

- [ ] Observe a new topology regression fail on the old workflow, then pass with audit and formatting outside the six test shards in a parallel required quality job. Preserve all test partitions, SDK-floor isolation, release/publish artifacts and existing deadlines.
- [ ] Run audit on every previously applicable event. Establish fresh-runner restore and required analyzer outputs before formatting; retain exact changed-file/event/base semantics.
- [ ] Aggregate must reject failed, cancelled or unexpectedly skipped quality on code routes; docs/evidence require the intended skipped quality alongside their existing required outcomes. Exercise actual aggregate script for positive and negative routes.
- [ ] Require cold review, affected contract gates and all required exact-head hosted checks, including real audit/format and owner artifacts. No retry of unchanged failures or deadline increase.

- [ ] Replace both combined cache post-save sites with restore-only readers and designated OS/route writers that explicitly save before tests, using the restore primary key and identical shared package path. Preserve cache persistence/ref visibility, every check and existing deadlines.
- [ ] Share checked ordinary main-plus-sample preparation between cache writers and release verification; retain strict audited main restore in quality and canonical sample discovery. Preserve restored-license input authority and its placement before build/tests/publication. Prove real preparation failures stop subsequent samples/save/build, and successful miss persistence precedes tests in hosted logs.

## Evidence

- PR1846 head a9df5cd36fb12625333c9ab837e8ab0996eb763e; run40345610532 job121651721892 annotation: The job has exceeded the maximum execution time of 30m0s. 2650 tests passed, zero failed, 24 platform skips; publish succeeded before formatter was cancelled. Independent prerequisite; product source remains fixed.

- Current08f8538c run40362766596 Linux1 job121712429653 passed2652/0/42 and uploadedTRX, then exceeded30m during post-job NuGet tar/zstd compression. This same required-path non-test deadline witness forces cache-write ownership and shared preparation extraction; quality/floor/otherfivecohorts passed. No unchanged rerun, timeout increase or renderer change.

## Scope

- Anchors identify three behavior files and four edited test files. Unchanged CiTopologyDecisionContractTests and TestShardPlanContractTests remain required affected consumers in full, alongside runner parity and extraction/child tests; removing an unchanged consumer from edit anchors does not remove it from verification.
- Shared preparation extraction is forced by reuse of the existing main-plus-sample contract; no file-count cap governs the correct implementation. Native renderer exit139 remains a separate owner.

- Forced sizeL after implementation exposed the evidence-consumer guard classification for its new synthetic routing payload. That own-diff classification ships atomically with the test that introduces it; no real evidence reads or exclusions are broadened. The shared main/sample preparation and its existing callers also cannot be split half-migrated. This remains one required-path deadline mechanism, not a sweep of unrelated CI work. Current code PRs have no independently mergeable green head; all ordinary closures stay trailing docs.
