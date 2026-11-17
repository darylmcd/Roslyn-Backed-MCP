# ci-linux-calibration-sibling-wait-coupling — One late Linux calibration build fails every Linux leg

**row:** `ci-linux-calibration-sibling-wait-coupling` · **pri:** `Low` · **size:** `M` · **verified:** `b525794bae630b1baa525694726a98b4095fd1e7`

## Anchors

- `eng/resolve-linux-shard-build-times.ps1:73-81` — every Linux leg polls until all sibling calibration builds of the run have completed, and throws after 3 minutes.
- `.github/workflows/ci.yml:212` — the resolving step has a 4-minute cap.
- `eng/resolve-ci-topology.ps1:91-95` — the code route has five Linux legs, each waiting on the other four.
- `tests/RoslynMcp.Tests/LinuxShardBuildTimesContractTests.cs` — contract tests of the resolver.

## Acceptance

- [ ] Decide and record in `CI_POLICY.md` § Linux runner capacity whether a late or failed sibling calibration build may fail the other Linux legs, with hosted evidence for the choice.
- [ ] If the coupling is reduced: a contract test fails on the current resolver for a late or failed sibling and passes after the change, and every leg of one run still uses the same partition.

## Evidence

- `eng/resolve-linux-shard-build-times.ps1:73-81`: the loop exits only when `$firstBuilds.Count` equals the shard count, else throws `Timed out waiting for Linux calibration builds` at 3 minutes. One sibling whose calibration build is late or failed therefore fails all five Linux legs of the run.
- Run 44176184128 (five Linux legs, first attempt, green): the resolving step took 2 to 36 seconds per leg.
- Job count: a code run holds 11 legs + quality + SDK floor = 13 jobs, plus the repository's CodeQL jobs, against an account limit of 40 concurrent jobs; three overlapping code runs exceed it, and a queued sibling starts its calibration build late.

## Context

- The behaviour is fail-closed and recoverable by re-running the failed jobs: a partial rerun reads the earliest successful build per shard from the same run.
- PR 2135 raised the sibling count on the code route from four to five; the mechanism predates it.
- No failure from this mechanism has been observed.
