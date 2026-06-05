---
category: Maintenance
---

- **Maintenance:** Removed the stale duplicate promotion scorecard at `ai_docs/audit-reports/_latest-promotion-scorecard.json` (serverVersion 1.34.2, from the retired `/audit-deep` writer). The canonical scorecard is the repo-root `audit-reports/_latest-promotion-scorecard.json` (serverVersion 1.38.1), written by the surface-test prompt and read by `eng/aggregate-promotion-scorecards.ps1`. Partial close of `promotion-scorecard-execution-batch` — source-of-truth dedup only; the tier-promotion re-run against the v2.3.x surface remains a tracked follow-on.
