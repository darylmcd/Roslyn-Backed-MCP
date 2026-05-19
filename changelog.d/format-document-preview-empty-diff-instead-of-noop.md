---
category: Fixed
---

- **Fixed:** `format_document_preview` now returns `changes: []` for a no-op format pass (already-formatted document). Previously `DiffGenerator` emitted a header-only unified diff string (`--- a/...` / `+++ b/...` with no `@@` hunks) when the formatted document text was identical to the original, causing callers that check `changes.length > 0` to misidentify the result as pending changes. Fixes gh #739.
