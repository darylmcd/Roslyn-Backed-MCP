---
category: Maintenance
---
- **Maintenance:** CI: pull requests that change only audit evidence, backlog item details or report archives (`ai_docs/audits/**`, `ai_docs/reports/**`, `ai_docs/items/**`, `audit-reports/**`, minus files tests read) now take a pwsh-only `evidence-lint` route with no build or test legs. `CHANGELOG.md`-only changes take the docs route, which now runs the version-drift and breaking-version gates, instead of the full Windows/Linux matrix. `audit-reports/` no longer requires a changelog fragment. Closes `ci-evidence-tier-route`.
