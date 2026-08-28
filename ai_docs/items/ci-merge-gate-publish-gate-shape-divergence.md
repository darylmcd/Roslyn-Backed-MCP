# ci-merge-gate-publish-gate-shape-divergence — Unsharded coverage exists only on a developer box

**row:** `ci-merge-gate-publish-gate-shape-divergence` · **pri:** `Low` · **size:** `M`

## Anchors

- `.github/workflows/ci.yml` — `New-Leg` matrix; every leg passes `-TestShardIndex`/`-TestShardCount`
- `.github/workflows/publish-nuget.yml:60` — invokes the release gate with no shard args

## Acceptance

- [ ] One CI leg runs `verify-release.ps1` unsharded on Windows, so unsharded coverage does not depend on a developer workstation being healthy.
- [ ] That leg runs on a schedule (nightly is sufficient) — it does not need to gate every PR.
- [ ] Its invocation stays in sync with `publish-nuget.yml`'s, or both derive from one source.

## Evidence

- 2026-08-28 release cut: the unsharded Windows run (release-cut Step 3) produced a red that cost a multi-hour false-lead investigation, because the workstation it ran on was memory-exhausted. The run itself was the right shape; the environment was not trustworthy. See `release-cut-step3-environment-precheck`.

## Context

Execution shapes:

| Gate | Shape |
|---|---|
| PR merge (`ci.yml`) | 4 Windows shards + 2 Linux shards |
| Publish (`publish-nuget.yml`) | Unsharded, `ubuntu-latest` |
| Release-cut Step 3 | Unsharded, Windows — **runs before Ship and Tag** |

Sharding changes semantics, not just speed: a defect depending on accumulated single-process state is invisible to a sharded run. Unsharded coverage therefore has value beyond the sharded matrix.

That coverage is not missing — Step 3 provides it before the tag, and `publish-nuget` provides it after. The gap is narrower: **the only pre-tag unsharded run happens on a developer machine**, whose health is uncontrolled. A scheduled CI leg would give an independent, trustworthy signal to check a local red against.

## Notes

- Priority is Low deliberately. An earlier draft of this row claimed the unsharded shape was "never exercised before the tag." That was false — Step 3 exercises it — and the corrected argument is materially weaker.
- Not a request to unshard the merge gate; the parallelism is worth keeping.
