---
category: Added
---

- **Added:** `/audit-deep` (modes `full` and `promotion-only`) now writes a machine-readable promotion scorecard at `ai_docs/audit-reports/_latest-promotion-scorecard.json` (schema v1) alongside the human-readable `.md` report. `/publish-preflight` gains Step 8 — reads the scorecard, checks freshness (≤30 days proceed; 30–90 WARN; >90 ignore), and surfaces any `recommendation: "promote"` rows as a manual checklist with the exact `Edit:` instructions for the `[McpToolMetadata]` tier flip + matching `ServerSurfaceCatalog` entry. Step 8 is advisory, never blocks publish. `mode=read-only` skips scorecard emission (writer recommendations would mislead without apply round-trips).
