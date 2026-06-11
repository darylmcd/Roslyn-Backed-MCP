# aggregate-scorecard-stale-search-path — drop the removed ai_docs scorecard probe from the aggregator

**row:** `aggregate-scorecard-stale-search-path` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `eng/aggregate-promotion-scorecards.ps1:111`
- `.claude/skills/publish-preflight/SKILL.md:104`
- `.claude/skills/promote-tier/SKILL.md:49`

## Acceptance

- [ ] `ai_docs/audit-reports/...` entry dropped from `$ScorecardSearchPaths` (or ordered below the canonical path)
- [ ] Doc inconsistency reconciled where `publish-preflight`/`promote-tier` SKILLs say the aggregator IGNORES the `ai_docs/` path while the script probes it first
- [ ] Regression: a grep/test asserts the canonical repo-root path is the preferred probe

## Evidence

- 2026-06-05 backlog-sweep init-2 cold spec-compliance review (plan 20260530T233522Z).

## Context

`eng/aggregate-promotion-scorecards.ps1` lists `ai_docs/audit-reports/_latest-promotion-scorecard.json` as the FIRST per-repo scorecard search-path probe (`$ScorecardSearchPaths` default), but that path was removed as a stale duplicate (#937) — the canonical file is the repo-root `audit-reports/_latest-promotion-scorecard.json` (the second probe). Currently graceful (missing scorecards are not errors; the fallback resolves), so nothing breaks — but the stale first-probe is misleading and, if any sibling repo still emits the old path, the aggregator would prefer the stale copy over the canonical one.
