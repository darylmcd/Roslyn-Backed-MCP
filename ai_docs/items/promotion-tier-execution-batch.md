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
