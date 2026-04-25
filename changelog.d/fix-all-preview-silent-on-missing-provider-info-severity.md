---
category: Fixed
---

- **Fixed:** `fix_all_preview` now returns the same actionable "no fix provider loaded" guidance regardless of the diagnostic severity (Info, Warning, Error) — previously Info-severity ids returned empty/null guidance indistinguishable from "no occurrences" (`FixAllService`). Closes `fix-all-preview-silent-on-missing-provider-info-severity`.
