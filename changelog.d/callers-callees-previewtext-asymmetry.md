---
category: Fixed
---

- **Fixed:** `callers_callees` no longer returns `null` `previewText` on every callee entry while populating it correctly for callers. Callees now use the same per-location source-extract path as callers, with the callee document resolved via `solution.GetDocument(invokedLoc.SourceTree)` and falling back to the caller's document for the invocation-site fallback path. Fixes gh #742.
