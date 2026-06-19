---
category: Maintenance
---

- **Maintenance:** `eng/aggregate-promotion-scorecards.ps1` now probes the canonical repo-root `audit-reports/_latest-promotion-scorecard.json` BEFORE the deprecated `ai_docs/audit-reports/` fallback (the stale duplicate removed in #937), so a leftover `ai_docs/` copy can no longer shadow the live scorecard. Reconciled the stale "per-repo canonical = `ai_docs/audit-reports/`" claims in the aggregator help, `process-audit-reports.ps1`, and the `publish-preflight` / `promote-tier` maintainer skills, and added a regression test asserting canonical-first preference. Closes `aggregate-scorecard-stale-search-path`.
