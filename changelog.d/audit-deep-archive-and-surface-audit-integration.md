---
category: Added
---

- **Added:** `/roslyn-mcp:audit-deep` Phase 0 delegates drift detection to `/surface-audit` when available, and ships `skills/audit-deep/scripts/archive-old-reports.ps1` (with `-OlderThanDays`, `-DryRun`, `-ReportsRelativePath` flags) that moves audit-report markdown files older than 30 days into `archive/<YYYY>/`. `-ReportsRelativePath` keeps the shipped skill generic (consumer repos can point it anywhere). Closes the (S2) + (S5) follow-on pieces of `audit-deep-skill-migration` (paired with initiative #12).
