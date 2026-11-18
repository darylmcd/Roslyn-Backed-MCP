# ci-linux-calibration-sibling-wait-coupling — One late Linux calibration build fails every Linux leg

**row:** `ci-linux-calibration-sibling-wait-coupling` · **pri:** `Medium` · **size:** `M`

## Anchors

- `eng/resolve-linux-shard-build-times.ps1:74-82` — every Linux leg polls until all sibling calibration builds of the run have completed, and throws after 3 minutes.
- `.github/workflows/ci.yml:212` — the resolving step has a 4-minute cap.
- `eng/resolve-ci-topology.ps1:97-101` — the code route has five Linux legs, each waiting on the other four.
- `tests/RoslynMcp.Tests/LinuxShardBuildTimesContractTests.cs` — contract tests of the resolver.

## Acceptance

- [ ] Decide and record in `CI_POLICY.md` § Linux runner capacity whether a late or failed sibling calibration build may fail the other Linux legs, with hosted evidence for the choice.
- [ ] If the coupling is reduced: a contract test fails on the current resolver for a late or failed sibling and passes after the change, and every leg of one run still uses the same partition.

## Evidence

- `eng/resolve-linux-shard-build-times.ps1:74-82`: the loop exits only when `$firstBuilds.Count` equals the shard count, else throws `Timed out waiting for Linux calibration builds` at 3 minutes. One sibling whose calibration build is late or failed therefore fails all five Linux legs of the run.
- Run 44176184128 (five Linux legs, first attempt, green): the resolving step took 2 to 36 seconds per leg.
- Observed failure: run 44182325844 (head 49e7e206, 2026-11-17), attempt 1, the second run of the five-leg topology. Leg `linux-1-of-5` ran on a degraded runner: step `Initialize PowerShell startup cache` took 21:41:20Z to 21:53:24Z (normally about 5 s) and `Linux host diagnostics` took 2 min 6 s, so its calibration build finished at 21:58:33Z. Legs 2 to 5 each failed in `Resolve Linux shard build times` after 3 min 1 s (for example `linux-2-of-5`: 21:43:04Z to 21:46:05Z). `validate` was red; all six Windows legs were green. Re-running the failed jobs recovered it (attempt 2 green, merged).
- Job count: a code run holds 11 legs + quality + SDK floor = 13 jobs, plus the repository's CodeQL jobs, against an account limit of 40 concurrent jobs; three overlapping code runs exceed it, and a queued sibling starts its calibration build late.

## Context

- The behaviour is fail-closed and recoverable by re-running the failed jobs: a partial rerun reads the earliest successful build per shard from the same run.
- PR 2135 raised the sibling count on the code route from four to five; the mechanism predates it.
- Raised from Low to Medium on 2026-11-17: the failure was observed once in the first two runs of the five-leg topology, and it turned a required check red for a cause unrelated to the change under test.
- The policy-docs route is not exposed: since 2026-11-17 it is one unsharded leg that skips calibration (`CI_POLICY.md` § Docs Route Test Selection).
