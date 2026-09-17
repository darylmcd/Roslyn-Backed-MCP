---
category: Fixed
---

- **Fixed:** The `mcp-server-surface-test` apply phase no longer reports the long-fixed `change_signature_preview` callsite-summary gap as a known limitation (it now asserts the preview enumerates every callsite file and `callsiteUpdates` counts), and a static test rejects shipped-skill citations of backlog rows that no longer exist. Closes `change-signature-callsite-summary-stale-row-comments`.
