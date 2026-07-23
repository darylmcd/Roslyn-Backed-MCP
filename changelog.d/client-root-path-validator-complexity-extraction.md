---
category: Maintenance
---

- **Maintenance:** Extracted lazy allowed-root enumeration, one-level parent widening, and separator-bounded root matching out of `ClientRootPathValidator.IsPathUnderAnyRoot` into three focused private helpers, reducing its cyclomatic complexity from 11 to 4 with no behavior change.
