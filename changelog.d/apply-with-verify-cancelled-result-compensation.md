---
category: Fixed
---

- **Fixed:** `apply_with_verify` no longer reports `applied`, `applied_with_errors`, or `rolled_back` when the underlying `compile_check` pass was itself cancelled — a cancelled pre-apply baseline now aborts before applying (zero apply/revert calls) and a cancelled post-apply verification now performs a best-effort revert before surfacing the cancellation, matching the tool's existing thrown-cancellation behavior.
