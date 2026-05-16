---
category: Fixed
---

- **Fixed:** `symbol_refactor_preview` incorrectly applying each chained sub-operation to the workspace during preview, writing files to disk and recording ledger entries before the agent called any `*_apply` tool. Preview now chains operations purely in-memory and stores the accumulated mutations under one `apply_composite_preview` token (fixes gh #770).
