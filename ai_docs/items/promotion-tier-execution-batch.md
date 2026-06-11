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
