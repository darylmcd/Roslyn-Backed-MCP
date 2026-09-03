---
category: Fixed
---

- **Fixed:** `find_unused_symbols` and `remove_dead_code_preview` treating a failed reference scan as a confident zero-reference result — a scan failure for a candidate now surfaces as an explicit error instead of a silent false "unused" claim, and the removal guard refuses removal when its own verification scan fails rather than proceeding on an unverifiable answer.
