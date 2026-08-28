# ci-merge-gate-publish-gate-shape-divergence — Merge gate never runs the publish gate's shape

**row:** `ci-merge-gate-publish-gate-shape-divergence` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.github/workflows/ci.yml` — `New-Leg` matrix; all legs pass `-TestShardIndex`/`-TestShardCount`
- `.github/workflows/publish-nuget.yml:60` — invokes the release gate with no shard args

## Acceptance

- [ ] At least one CI leg runs `verify-release.ps1` **unsharded on Windows**, matching the publish gate's execution shape.
- [ ] That leg runs before a tag can be pushed — nightly schedule and/or a required pre-release check — not only on `v*` tag push.
- [ ] The leg's invocation is kept in sync with `publish-nuget.yml`'s, or both derive from one source, so the shapes cannot drift apart again.
- [ ] `CI_POLICY.md` records why an unsharded leg exists (sharding changes semantics, not just speed).

## Evidence

- 2026-08-28 release cut: `verify-release.ps1` failed locally at Step 3 in a shape CI had never exercised. The failure turned out to be environmental, but the gap it exposed is real — no CI leg runs the publish gate's shape before the tag.

## Context

Execution shapes today:

| Gate | Shape |
|---|---|
| PR merge (`ci.yml`) | 4 Windows shards + 2 Linux shards |
| Publish (`publish-nuget.yml`) | Unsharded, `ubuntu-latest` |
| Local release-cut Step 3 | Unsharded, Windows |

Sharding is a speed optimization that changes semantics: any defect depending on accumulated single-process state (leaked load contexts, static caches, background work racing teardown, cross-test ordering) is structurally invisible to a sharded run. A PR can be green four ways and still be broken in the shape the publish gate uses.

The publish gate is currently the first unsharded execution in the pipeline, and it runs **after** the tag push. Discovery there means a tag exists for a build that cannot publish.

## Notes

- Not a request to unshard the merge gate — the parallelism is worth keeping. One additional leg is enough.
- Windows specifically: the existing unsharded execution is Linux-only, so Windows-only single-process behavior has no unsharded coverage anywhere.
