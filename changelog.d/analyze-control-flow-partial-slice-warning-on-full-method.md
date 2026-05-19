---
category: Fixed
---

- **Fixed:** `analyze_control_flow` no longer emits a spurious "partial-slice" warning when the supplied line range covers an entire method body and the analysis succeeds with zero explicit control-flow exits (void method or complete block with no returns). The warning is now suppressed when `Succeeded = true` and entry/exit/return counts are all zero. Fixes gh #743.
