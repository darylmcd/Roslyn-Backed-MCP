---
category: Changed
---

- **Changed:** `apply_composite_preview` now states why its `_preview` suffix is retained (it names the preview token it redeems; the name is kept for API stability), and a catalog test rejects any other `_preview`-suffixed tool that is not read-only and non-destructive.
