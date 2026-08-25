# promotion-tier-execution-batch — re-run promotion scorecard + ship tier promotions in batches

**row:** `promotion-tier-execution-batch` · **pri:** `Medium` · **size:** `L` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `audit-reports/_latest-promotion-scorecard.json`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.*.cs`
- `skills/promote-tier/`

## Acceptance

- [ ] Promotion scorecard re-run against the CURRENT server surface (v2.3.x); canonical snapshot refreshed from v1.38.1
- [ ] Experimental→stable tier promotions shipped in bounded batches via the `/promote-tier` skill

## Evidence

- Dedup shipped [#937](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/937); canonical scorecard snapshot is v1.38.1 vs the current v2.3.x surface.

## Context

Follow-on to the source-of-truth dedup (SHIPPED [#937](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/937): removed stale `ai_docs/audit-reports/_latest-promotion-scorecard.json`; canonical is repo-root `audit-reports/_latest-promotion-scorecard.json`, serverVersion 1.38.1).

**Hotspot** — touches `ServerSurfaceCatalog.*.cs` partials (RMCP001/RMCP002 catalog-tracking analyzers gate every promotion); schedule as its own sweep with ≤1 catalog-touching initiative per wave.

Clusters: brainstorm BRAIN-007 (stable-surface promotion scorecard system). Source: 2026-05-26 discovery-sweep + dedup half shipped 2026-06-05 (plan `20260530T233522Z` init 2).

**Sweep-shaped — `/backlog-sweep:prepare` rather than top-5 remediation.**

## Amendment — 2026-08-10 (backlog-sweep 20260810T175048Z, PR #1202 — prerequisite only, acceptance bullet 1 STILL OPEN)

- **The scorecard was NOT refreshed.** PR #1202 shipped as initiative `promotion-tier-scorecard-refresh` but delivered only the *durability prerequisite*, not the refresh. Verified on `main` after merge: `audit-reports/_latest-promotion-scorecard.json` still reads `generatedAt: 2026-05-16T06:25:47Z` / `serverVersion: 1.38.1` against a v2.3.8 server. **Acceptance bullet 1 above remains unmet.**
- **What DID ship:** the canonical scorecard is now git-tracked (single `.gitignore` negation; `git check-ignore` exits 1 and `git ls-files audit-reports/` lists it), and the surface-test skill's artifact writes are pinned to the primary checkout. So a future refresh will now produce a reviewable diff.
- **Stale anchor:** the `skills/promote-tier/` anchor above does NOT exist. The maintainer skill lives at `.claude/skills/promote-tier/`. Fix the anchor when this row is next opened.
- **No staleness alarm exists.** `.gitignore` now claims tracking means "a lost refresh shows up as an absent diff instead of failing silently", but nothing compares `generatedAt` / `serverVersion` against the current build (`rg 'serverVersion|generatedAt|stale|MaxAge' eng/aggregate-promotion-scorecards.ps1` finds only the writer and comments). Tracked by `surface-test-audit-artifact-gate-and-scorecard-staleness`.
- Acceptance bullet 2 (ship experimental->stable promotions in bounded batches) is untouched and still gated on a fresh scorecard. Promotions touch the `ServerSurfaceCatalog.*.cs` partials — an addenda-listed hotspot, RMCP001/RMCP002-gated — so schedule at most one catalog-touching initiative per wave.
## Amendment — 2026-08-25 (backlog-sweep 20260825T151721Z — initiative DEFERRED, row stays OPEN)

Initiative `promotion-tier-scorecard-refresh-execution` (acceptance bullet 1 only) was planned, scheduled `heroic-last`, executed, and **deferred without shipping**. No partial work was committed and the scorecard is untouched.

**Blocker — an environment capability gap, not a work failure.** The canonical scorecard is written only by a `--full` `/mcp-server-surface-test` run. Per `skills/mcp-server-surface-test/SKILL.md:55`, dispatching `audit-phase-runner` subagents is "the only way the `--full` tier achieves its 250+ tool-call coverage"; `:56` documents `--single-agent` as the opt-out for hosts that cannot spawn subagents, with long phases surfacing as `phase-failed-budget` partial coverage. **A workflow-dispatched executor subagent cannot itself spawn subagents**, so only the degraded path was available, and a partial scorecard is worse than a stale one — it destroys the staleness signal while looking current.

**Verified at defer time:** `audit-reports/_latest-promotion-scorecard.json` still reads `generatedAt: 2026-05-16T06:25:47Z` / `serverVersion: 1.38.1`; the worktree was clean.

**RE-SCOPE — do not re-plan this as a sweep initiative.** It must be run by a **top-level orchestrator session** that can spawn subagents: invoke `/mcp-server-surface-test --full` directly, then commit the refreshed scorecard. Nesting it inside `/backlog-sweep:execute` will fail the same way every time.

**Stale anchor (still unfixed, since the initiative shipped nothing):** the `skills/promote-tier/` anchor in this file does not exist — the maintainer skill lives at `.claude/skills/promote-tier/`.

Acceptance bullet 2 (ship experimental→stable promotions in bounded batches) remains gated on a fresh scorecard.
