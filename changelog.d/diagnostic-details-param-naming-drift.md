---
category: Fixed
---

- **Fixed:** `diagnostic_details` now accepts `startLine`/`startColumn` as aliases for `line`/`column`, matching every other positional tool (`find_references`, `goto_definition`, `get_code_actions`, …). Conflicting-but-unequal pairs or missing-both-pairs inputs raise a clear `ArgumentException` instead of returning a misleading "diagnostic not found" envelope (`AnalysisTools`). (`diagnostic-details-param-naming-drift`)
