---
category: Fixed
---

- **Fixed:** Discover exported code-fix providers through each analyzer reference's dependency loader without resolving unrelated helper types, preserving real provider failures; honor diagnostic descriptor help links with compiler-only fallback; and count diagnostic summaries separately by severity and category while retaining distinct-ID totals. Closes `diagnostic-details-repo-provider-load-failures`, `diagnostic-details-descriptor-help-link`, and `diagnostics-summary-mixed-severity-same-id`.
