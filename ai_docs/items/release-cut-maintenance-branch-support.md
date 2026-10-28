# release-cut-maintenance-branch-support — Let release-cut cut a minor/patch from a maintenance branch when main carries pending BREAKING fragments

**row:** `release-cut-maintenance-branch-support` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.claude/skills/release-cut/SKILL.md`
- `.claude/skills/bump/SKILL.md`
- `eng/verify-release.ps1`
- `.github/workflows/publish-nuget.yml`
- `docs/release-policy.md`

## Acceptance

- [ ] `release-cut` documents and supports a maintenance-branch mode: input names the base tag or branch (for example `release/4.x` at `v4.3.0`); refusal conditions check that branch instead of `main`; tag and Layer 1/2 steps are unchanged.
- [ ] Forward-port step is explicit: the cut's code commits, its fragment and its `## [X.Y.Z]` CHANGELOG section land on `main` in a follow-up PR without changing `main`'s version files.
- [ ] `docs/release-policy.md` states when a maintenance cut is the required route (a deprecation window must close while `main` already holds BREAKING fragments).
- [ ] The first use (4.4.0) is run through the documented mode, not by hand.

## Evidence

- `release-cut` SKILL refuses when the branch is not `main` (Refusal conditions) and assumes the Unreleased section of `main` is what ships.
- 9 `Changed — BREAKING` fragments are merged on `main` (2026-10-06..2026-10-24) and `-RequireConsumedFragments` (also run by `publish-nuget.yml:64`) forces any cut that consumes them to a major, so a minor carrying only the locationdto stage-3 deprecation cannot be cut from `main`. See `release-plan-4.4.0-then-5.0.0` decisions D1.

## Context

Needed by `release-4-4-0-locationdto-deprecation-cut`; policy basis `docs/release-policy.md:38` (deprecate at least one released minor before removal).

## Notes

2026-10-28 learnings from the first maintenance cut (4.4.0): (1) the tag-time verify-changelog-fragments diffed against origin/main and rejected the release; fixed in verify-changelog-fragments.ps1 (tag runs use HEAD^) and covered by ChangelogFragmentRequirementTests. (2) A fix landing after the release commit needs its fragment, but the tag gate needs none pending, so a short re-assembly PR (CHANGELOG-only diff) must follow before tagging. (3) Rebase merges are disabled, so squash only; the forward-port cherry-picks from the original commits, not the squash. (4) The unpublished v4.4.0 tag had to be deleted and recreated after the failed first run (nothing reached NuGet). (5) Host precheck counts dotnet processes, and each Roslyn MCP server is two. The skill still needs: maintenance mode input, forward-port step, and a dry-run path that does not diff against main.
