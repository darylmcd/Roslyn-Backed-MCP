---
category: Fixed
---

- **Fixed:** `apply_with_verify` now compares the COMPLETE pre/post error-identity baseline (not just the default 50-diagnostic page) when deciding whether to auto-revert, so a newly introduced error sorting beyond the first diagnostic page is no longer silently missed.
